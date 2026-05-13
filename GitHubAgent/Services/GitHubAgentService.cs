using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace GitHubAgent.Services
{
    public class GitHubAgentService
    {
        private readonly IConfiguration _config;
        private Kernel? _kernel;
        private IChatCompletionService? _chatService;
        private HttpClient? _httpClient;
        private bool _isInitialized = false;
        private int _requestId = 0;
        private readonly Uri _mcpEndpoint = new("https://api.githubcopilot.com/mcp/");
        private readonly AsyncLocal<List<string>?> _currentToolCalls = new();

        // Ezt a Blazor injektálja be
        public GitHubAgentService()
        {
            var confbuilder = new ConfigurationBuilder();

            string appSettingsPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, @"..\..\..\..\FunChatBotApp\appsettings.json"));
            confbuilder.AddJsonFile(appSettingsPath, optional: true);

            string keyVaultUrl = "https://funchatbot-akv.vault.azure.net/";
            confbuilder.AddAzureKeyVault(new Uri(keyVaultUrl), new DefaultAzureCredential());

            _config = confbuilder.Build();
        }

        // Aszinkron inicializálás (csak az első hívásnál fut le)
        private async Task EnsureInitializedAsync(CancellationToken cancellationToken = default)
        {
            if (_isInitialized) return;

            // 1. Semantic Kernel inicializálása
            var builder = Kernel.CreateBuilder();

            // Cseréld ki a saját Azure OpenAI (vagy sima OpenAI) adataidra az appsettings.json-ből
            builder.AddAzureOpenAIChatCompletion(
                deploymentName: _config["OpenAI:DeploymentName"] ?? "",
                endpoint: _config["OpenAI:Endpoint"] ?? "",
                apiKey: _config["OpenAI:ApiKey"] ?? "");

            _kernel = builder.Build();
            _chatService = _kernel.GetRequiredService<IChatCompletionService>();

            // 2. A GitHub Token beolvasása
            string githubToken = _config["GitHub:PatToken"] ?? throw new ArgumentNullException("Hiányzik a GitHub token!");

            // 3. HTTP Kliens a Remote MCP-hez
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromMinutes(5)
            };
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {githubToken}");
            _httpClient.DefaultRequestHeaders.Accept.ParseAdd("application/json");
            _httpClient.DefaultRequestHeaders.Accept.ParseAdd("text/event-stream");

            // Opcionális: Insiders funkciók bekapcsolása (ahogy a doksi írta)
            // _httpClient.DefaultRequestHeaders.Add("X-MCP-Insiders", "true");

            await InitializeMcpAsync(cancellationToken);
            var tools = await GetToolsAsync(cancellationToken);
            RegisterTools(tools);

            _isInitialized = true;
        }

        // Ezt fogja hívni a Home.razor
        public async Task<AgentResponse> RunAgentAsync(IReadOnlyList<ChatTurn> history)
        {
            await EnsureInitializedAsync();

            _currentToolCalls.Value = new List<string>();

            var chatHistory = new ChatHistory("Te egy GitHub asszisztens vagy. Használd a GitHub MCP toolokat, ha szükséges.");
            foreach (var turn in history)
            {
                switch (turn.Role)
                {
                    case ChatRole.User:
                        chatHistory.AddUserMessage(turn.Content);
                        break;
                    case ChatRole.Assistant:
                        chatHistory.AddAssistantMessage(turn.Content);
                        break;
                }
            }

            // Engedélyezzük, hogy az AI magától hívhassa a GitHub toolokat
            var executionSettings = new OpenAIPromptExecutionSettings
            {
                ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions
            };

            // AI válasz generálása (itt hívódnak meg az MCP toolok a háttérben)
            var response = await _chatService!.GetChatMessageContentAsync(
                chatHistory,
                executionSettings,
                _kernel);

            var content = response.Content ?? "Sajnos nem tudtam válaszolni.";
            var toolsUsed = _currentToolCalls.Value?.Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
                ?? Array.Empty<string>();

            _currentToolCalls.Value = null;

            return new AgentResponse(content, toolsUsed);
        }

        private async Task InitializeMcpAsync(CancellationToken cancellationToken)
        {
            var initializeParams = new
            {
                protocolVersion = "2024-11-05",
                clientInfo = new { name = "GitHubAgent", version = "1.0" },
                capabilities = new
                {
                    tools = new { }
                }
            };

            await SendRpcAsync("initialize", initializeParams, cancellationToken);

            var notification = new
            {
                jsonrpc = "2.0",
                method = "notifications/initialized"
            };

            await SendNotificationAsync(notification, cancellationToken);
        }

        private async Task<IReadOnlyList<McpToolDefinition>> GetToolsAsync(CancellationToken cancellationToken)
        {
            var result = await SendRpcAsync("tools/list", null, cancellationToken);

            if (!result.TryGetProperty("tools", out var toolsElement) || toolsElement.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<McpToolDefinition>();
            }

            var tools = new List<McpToolDefinition>();
            foreach (var toolElement in toolsElement.EnumerateArray())
            {
                if (!toolElement.TryGetProperty("name", out var nameElement))
                {
                    continue;
                }

                string name = nameElement.GetString() ?? string.Empty;
                string? description = toolElement.TryGetProperty("description", out var descriptionElement)
                    ? descriptionElement.GetString()
                    : null;
                JsonElement? inputSchema = null;
                if (toolElement.TryGetProperty("inputSchema", out var schemaElement))
                {
                    inputSchema = schemaElement;
                }

                if (!string.IsNullOrWhiteSpace(name))
                {
                    tools.Add(new McpToolDefinition(name, description, inputSchema));
                }
            }

            return tools;
        }

        private void RegisterTools(IReadOnlyList<McpToolDefinition> tools)
        {
            if (_kernel == null || tools.Count == 0)
            {
                return;
            }

            var functions = new List<KernelFunction>();
            foreach (var tool in tools)
            {
                var parameters = BuildParameters(tool.InputSchema);
                Func<KernelArguments, CancellationToken, Task<string>> handler = (args, cancellationToken)
                    => InvokeToolAsync(tool, args, cancellationToken);

                var function = KernelFunctionFactory.CreateFromMethod(
                    handler,
                    tool.Name,
                    tool.Description,
                    parameters);

                functions.Add(function);
            }

            var plugin = KernelPluginFactory.CreateFromFunctions("RemoteGitHubPlugin", functions);
            _kernel.Plugins.Add(plugin);
        }

        private static IReadOnlyList<KernelParameterMetadata> BuildParameters(JsonElement? schema)
        {
            if (schema is null || schema.Value.ValueKind == JsonValueKind.Undefined)
            {
                return Array.Empty<KernelParameterMetadata>();
            }

            if (!schema.Value.TryGetProperty("properties", out var propertiesElement)
                || propertiesElement.ValueKind != JsonValueKind.Object)
            {
                return Array.Empty<KernelParameterMetadata>();
            }

            var required = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (schema.Value.TryGetProperty("required", out var requiredElement)
                && requiredElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var requiredItem in requiredElement.EnumerateArray())
                {
                    var requiredName = requiredItem.GetString();
                    if (!string.IsNullOrWhiteSpace(requiredName))
                    {
                        required.Add(requiredName);
                    }
                }
            }

            var parameters = new List<KernelParameterMetadata>();
            foreach (var property in propertiesElement.EnumerateObject())
            {
                var description = property.Value.TryGetProperty("description", out var descriptionElement)
                    ? descriptionElement.GetString()
                    : null;
                var parameter = new KernelParameterMetadata(property.Name)
                {
                    Description = description,
                    IsRequired = required.Contains(property.Name)
                };

                parameters.Add(parameter);
            }

            return parameters;
        }

        private async Task<string> InvokeToolAsync(McpToolDefinition tool, KernelArguments arguments, CancellationToken cancellationToken)
        {
            RecordToolUsage(tool.Name);
            var toolArguments = BuildToolArguments(tool, arguments);
            var result = await SendRpcAsync("tools/call", new { name = tool.Name, arguments = toolArguments }, cancellationToken);
            return ExtractToolResponse(result);
        }

        private void RecordToolUsage(string toolName)
        {
            var tools = _currentToolCalls.Value;
            if (tools == null)
            {
                return;
            }

            tools.Add(toolName);
        }

        private static JsonObject BuildToolArguments(McpToolDefinition tool, KernelArguments arguments)
        {
            var payload = new JsonObject();
            if (tool.InputSchema is null)
            {
                foreach (var entry in arguments)
                {
                    payload[entry.Key] = entry.Value is null ? null : JsonSerializer.SerializeToNode(entry.Value);
                }

                return payload;
            }

            if (tool.InputSchema.Value.TryGetProperty("properties", out var propertiesElement)
                && propertiesElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in propertiesElement.EnumerateObject())
                {
                    if (arguments.TryGetValue(property.Name, out var value))
                    {
                        payload[property.Name] = value is null ? null : JsonSerializer.SerializeToNode(value);
                    }
                }
            }

            return payload;
        }

        private async Task<JsonElement> SendRpcAsync(string method, object? parameters, CancellationToken cancellationToken)
        {
            if (_httpClient == null)
            {
                throw new InvalidOperationException("HTTP kliens nincs inicializálva.");
            }

            var request = new
            {
                jsonrpc = "2.0",
                id = Interlocked.Increment(ref _requestId),
                method,
                @params = parameters
            };

            var payload = JsonSerializer.Serialize(request);
            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            using var response = await _httpClient.PostAsync(_mcpEndpoint, content, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"MCP hiba: {(int)response.StatusCode} {response.StatusCode}. Válasz: {responseBody}");
            }

            var jsonPayload = ExtractJsonPayload(responseBody);
            if (!LooksLikeJson(jsonPayload))
            {
                throw new InvalidOperationException($"MCP válasz nem JSON formátumú. Válasz: {jsonPayload}");
            }

            using var document = JsonDocument.Parse(jsonPayload);
            if (document.RootElement.TryGetProperty("error", out var errorElement))
            {
                throw new InvalidOperationException(errorElement.GetRawText());
            }

            return document.RootElement.GetProperty("result").Clone();
        }

        private async Task SendNotificationAsync(object notification, CancellationToken cancellationToken)
        {
            if (_httpClient == null)
            {
                throw new InvalidOperationException("HTTP kliens nincs inicializálva.");
            }

            var payload = JsonSerializer.Serialize(notification);
            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            using var response = await _httpClient.PostAsync(_mcpEndpoint, content, cancellationToken);
            response.EnsureSuccessStatusCode();
        }

        private static string ExtractToolResponse(JsonElement result)
        {
            if (result.TryGetProperty("content", out var contentElement)
                && contentElement.ValueKind == JsonValueKind.Array)
            {
                var builder = new StringBuilder();
                foreach (var contentItem in contentElement.EnumerateArray())
                {
                    if (contentItem.TryGetProperty("type", out var typeElement)
                        && typeElement.ValueKind == JsonValueKind.String)
                    {
                        var type = typeElement.GetString();
                        if (type == "text" && contentItem.TryGetProperty("text", out var textElement))
                        {
                            builder.Append(textElement.GetString());
                        }
                        else if (type == "json" && contentItem.TryGetProperty("json", out var jsonElement))
                        {
                            builder.Append(jsonElement.GetRawText());
                        }
                    }
                }

                var combined = builder.ToString();
                if (!string.IsNullOrWhiteSpace(combined))
                {
                    return combined;
                }
            }

            return result.GetRawText();
        }

        private static string ExtractJsonPayload(string responseBody)
        {
            if (string.IsNullOrWhiteSpace(responseBody))
            {
                return responseBody;
            }
            var lines = responseBody.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var builder = new StringBuilder();
            foreach (var line in lines)
            {
                var current = line.Trim();
                if (current.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                {
                    var payload = current[5..].Trim();
                    if (payload.Length == 0 || payload == "[DONE]")
                    {
                        continue;
                    }

                    builder.Append(payload);
                }
            }

            var combined = builder.ToString().Trim();
            if (!string.IsNullOrWhiteSpace(combined))
            {
                return combined;
            }

            return responseBody;
        }

        private static bool LooksLikeJson(string? payload)
        {
            if (string.IsNullOrWhiteSpace(payload))
            {
                return false;
            }

            var trimmed = payload.TrimStart();
            return trimmed.StartsWith("{") || trimmed.StartsWith("[");
        }

        private sealed record McpToolDefinition(string Name, string? Description, JsonElement? InputSchema);

        public sealed record AgentResponse(string Content, IReadOnlyList<string> ToolsUsed);

        public sealed record ChatTurn(ChatRole Role, string Content);

        public enum ChatRole
        {
            User,
            Assistant
        }
    }
}
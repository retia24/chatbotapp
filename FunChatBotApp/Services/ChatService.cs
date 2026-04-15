using Azure;
using Azure.AI.OpenAI;
using FunChatBotApp.Models;
using FunChatBotApp.Services;
using FunChatBotApp.Plugins;
using OpenAI.Chat;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using System.ClientModel;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace FunChatBotApp.Services;

public class ChatService
{
    private readonly ChatClient _chatClient;
    private readonly CosmosDbService _cosmosDb;
    private readonly Kernel _kernel;

    private readonly ThemeService _themeService;

    private readonly TextAnalyticsService _textAnalyticsService;

    // Beállítások a memóriához
    private const int SlidingWindowSize = 10; // Csak az utolsó N üzenetet küldjük a fő chatnél
    private const int SummarizationTrigger = 6; // Minden N-edik üzenetváltás után frissítjük a közös memóriát

    public ChatService(IConfiguration config, CosmosDbService cosmosDb, Kernel kernel, ThemeService themeService, TextAnalyticsService textAnalyticsService)
    {
        _cosmosDb = cosmosDb;
        _kernel = kernel;
        _themeService = themeService;
        _textAnalyticsService = textAnalyticsService;

        var endpoint = config["OpenAI:Endpoint"];
        var apiKey = config["OpenAI:ApiKey"]; // Key Vaultból
        var deploymentName = config["OpenAI:DeploymentName"];

        var azureClient = new AzureOpenAIClient(
            new Uri(endpoint!),
            new AzureKeyCredential(apiKey!));

        _chatClient = azureClient.GetChatClient(deploymentName);
    }

    public async Task<string> SendMessageAsync(List<OpenAI.Chat.ChatMessage> history)
    {
        ClientResult<ChatCompletion> completion = await _chatClient.CompleteChatAsync(history);
        return completion.Value.Content[0].Text;
    }

    /// <summary>
    /// Projekt alapú beszélgetés - csatolt közös memóriával és szummarizációval.
    /// </summary>
    public async Task<string> ProcessProjectMessageAsync(Project project, ChatSession chat, List<Models.ChatMessage> currentMessages, string userMessage, string userId, bool includeDocumentContext = false, int? maxSentences = null)
    {
        string safeUserMessage = await _textAnalyticsService.RedactPiiAsync(userMessage);
        // 1. Felhasználói üzenet rögzítése és mentése a CosmosDB-be
        var userMsg = new Models.ChatMessage 
        { 
            ChatId = chat.Id,
            ProjectId = project.Id,
            UserId = userId,
            Role = "user", 
            Content = safeUserMessage
        };
        currentMessages.Add(userMsg);
        await _cosmosDb.UpsertMessageAsync(userMsg, userId);

        var chatKernel = _kernel.Clone();
        var plugin = new UserStrategistPlugin(_cosmosDb, userId);
        chatKernel.Plugins.AddFromObject(plugin, "Strategist");
        chatKernel.Plugins.AddFromObject(new ThemePlugin(_themeService), "Theme");

        var topicFilter = new TopicExtractionFilter();
        chatKernel.FunctionInvocationFilters.Add(topicFilter);

        var executionSettings = new OpenAIPromptExecutionSettings
        {
            ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions
        };

        var chatCompletion = chatKernel.GetRequiredService<Microsoft.SemanticKernel.ChatCompletion.IChatCompletionService>();
        var chatHistory = new Microsoft.SemanticKernel.ChatCompletion.ChatHistory();

        if (!string.IsNullOrEmpty(project.JointSummary))
        {
            // A projekt globális memóriáját System promptként adjuk át
            chatHistory.AddSystemMessage($"Project Context/Memory: {project.JointSummary}");
        }

        // Külön tárolt dokumentum beemelése szükség esetén
        if (includeDocumentContext && !string.IsNullOrEmpty(chat.ActiveDocumentText))
        {
            var textToInject = chat.ActiveDocumentText;
            if (textToInject.Length > 30000)
            {
                textToInject = textToInject.Substring(0, 30000) + "\n... [TARTALOM LEVÁGVA A HOSSZ MIATT]";
            }
            chatHistory.AddSystemMessage($"Reference Document:\n<document>\n{textToInject}\n</document>");
        }

        if (maxSentences.HasValue && maxSentences.Value > 0)
        {
            chatHistory.AddSystemMessage($"Please answer in at most {maxSentences.Value} sentences.");
        }

        // System prompt, hogy az LLM pontosan tudja, mi a dolga általában
        chatHistory.AddSystemMessage("Te egy hasznos és intelligens AI asszisztens vagy. CSAK AKKOR használj eszközöket (tools), ha a felhasználó KIFEJEZETTEN témát kér, vagy az adatait szeretné elemeztetni, vagy a TÉMÁT/KINÉZETET (theme) szeretné sötétre vagy világosra váltani. Egyéb üzenetekre válaszolj normálisan, eszközhasználat nélkül.");

        // Sliding window a teljes projekt üzeneteiből
        var allProjectMessages = await _cosmosDb.GetProjectMessagesAsync(project.Id, userId, SlidingWindowSize);
        if (!allProjectMessages.Any(m => m.Id == userMsg.Id))
        {
            allProjectMessages.Add(userMsg);
            allProjectMessages = allProjectMessages
                .GroupBy(m => m.ChatId)
                .SelectMany(g => g.TakeLast(SlidingWindowSize))
                .OrderBy(m => m.Timestamp)
                .ToList();
        }

        foreach (var msg in allProjectMessages)
        {
            if (msg.Role == "user")
                chatHistory.AddUserMessage(msg.Content);
            else if (msg.Role == "assistant")
                chatHistory.AddAssistantMessage(msg.Content);
            else
                chatHistory.AddSystemMessage(msg.Content);
        }

        // 3. Valódi automata tool calling (LLM dönti el, hogy meghívja-e az elemző+ajánló ágenseket a pluginból)
        var result = await Microsoft.SemanticKernel.ChatCompletion.ChatCompletionServiceExtensions.GetChatMessageContentAsync(chatCompletion, chatHistory, executionSettings, chatKernel);
        var assistantResponse = result.Content ?? "";

        // Ha meghívta a Téma Generáló toolt, felülírjuk a szöveget
        if (topicFilter.ExtractedTopics != null && topicFilter.ExtractedTopics.Any())
        {
            assistantResponse = "Ezeket a témákat találtam neked:";
        }

        // --- ÚJ: Az AI válaszának maszkolása ---
        assistantResponse = await _textAnalyticsService.RedactPiiAsync(assistantResponse);

        // 4. Asszisztens válaszának rögzítése és mentése a CosmosDB-be
        var assistantMsg = new Models.ChatMessage
        {
            ChatId = chat.Id,
            ProjectId = project.Id,
            UserId = userId,
            Role = "assistant",
            Content = assistantResponse, // Itt már a maszkolt szöveg kerül mentésre
            Topics = topicFilter.ExtractedTopics
        };
        currentMessages.Add(assistantMsg);
        await _cosmosDb.UpsertMessageAsync(assistantMsg, userId);

        allProjectMessages.Add(assistantMsg);

        // 5. Szummázás és mentés
        if (allProjectMessages.Count(m => m.Role == "user") % SummarizationTrigger == 0)
        {
            await UpdateProjectJointSummaryAsync(project, allProjectMessages, userId);
        }

        return assistantResponse;
    }

    /// <summary>
    /// "Szimpla", egyéni beszélgetés külön projekt és memória nélkül
    /// </summary>
    public async Task<string> ProcessStandaloneMessageAsync(ChatSession chat, List<Models.ChatMessage> currentMessages, string userMessage, string userId, bool includeDocumentContext = false, int? maxSentences = null)
    {
        string safeUserMessage = await _textAnalyticsService.RedactPiiAsync(userMessage);
        // 1. User üzenet
        var userMsg = new Models.ChatMessage
        { 
            ChatId = chat.Id,
            ProjectId = chat.ProjectId,
            UserId = userId,
            Role = "user", 
            Content = safeUserMessage
        };
        currentMessages.Add(userMsg);
        await _cosmosDb.UpsertMessageAsync(userMsg, userId);

        var chatKernel = _kernel.Clone();
        var plugin = new UserStrategistPlugin(_cosmosDb, userId);
        chatKernel.Plugins.AddFromObject(plugin, "Strategist");
        chatKernel.Plugins.AddFromObject(new ThemePlugin(_themeService), "Theme");

        var topicFilter = new TopicExtractionFilter();
        chatKernel.FunctionInvocationFilters.Add(topicFilter);

        var executionSettings = new OpenAIPromptExecutionSettings
        {
            ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions
        };

        var chatCompletion = chatKernel.GetRequiredService<Microsoft.SemanticKernel.ChatCompletion.IChatCompletionService>();
        var chatHistory = new Microsoft.SemanticKernel.ChatCompletion.ChatHistory();

        // Külön tárolt dokumentum beemelése szükség esetén
        if (includeDocumentContext && !string.IsNullOrEmpty(chat.ActiveDocumentText))
        {
            var textToInject = chat.ActiveDocumentText;
            if (textToInject.Length > 30000)
            {
                textToInject = textToInject.Substring(0, 30000) + "\n... [TARTALOM LEVÁGVA A HOSSZ MIATT]";
            }
            chatHistory.AddSystemMessage($"Reference Document:\n<document>\n{textToInject}\n</document>");
        }

        if (maxSentences.HasValue && maxSentences.Value > 0)
        {
            chatHistory.AddSystemMessage($"Please answer in at most {maxSentences.Value} sentences.");
        }

        // System prompt, hogy az LLM pontosan tudja, mi a dolga általában
        chatHistory.AddSystemMessage("Te egy hasznos és intelligens AI asszisztens vagy. CSAK AKKOR használj eszközöket (tools), ha a felhasználó KIFEJEZETTEN témát kér, vagy az adatait szeretné elemeztetni, vagy a TÉMÁT/KINÉZETET (theme) szeretné sötétre vagy világosra váltani. Egyéb üzenetekre válaszolj normálisan, eszközhasználat nélkül.");

        var recentMessages = currentMessages.TakeLast(SlidingWindowSize);
        foreach (var msg in recentMessages)
        {
            if (msg.Role == "user")
                chatHistory.AddUserMessage(msg.Content);
            else if (msg.Role == "assistant")
                chatHistory.AddAssistantMessage(msg.Content);
            else
                chatHistory.AddSystemMessage(msg.Content);
        }

        // 3. Valódi automata tool calling (LLM dönti el, hogy meghívja-e az elemző+ajánló ágenseket a pluginból)
        var result = await Microsoft.SemanticKernel.ChatCompletion.ChatCompletionServiceExtensions.GetChatMessageContentAsync(chatCompletion, chatHistory, executionSettings, chatKernel);
        var assistantResponse = result.Content ?? "";

        // Ha meghívta a Téma Generáló toolt, felülírjuk a szöveget
        if (topicFilter.ExtractedTopics != null && topicFilter.ExtractedTopics.Any())
        {
            assistantResponse = "Ezeket a témákat találtam neked:";
        }

        // --- ÚJ: Az AI válaszának maszkolása ---
        assistantResponse = await _textAnalyticsService.RedactPiiAsync(assistantResponse);

        // 2. Assistant üzenet mentése
        var assistantMsg = new Models.ChatMessage
        {
            ChatId = chat.Id,
            ProjectId = chat.ProjectId,
            UserId = userId,
            Role = "assistant",
            Content = assistantResponse,
            Topics = topicFilter.ExtractedTopics
        };
        currentMessages.Add(assistantMsg);
        await _cosmosDb.UpsertMessageAsync(assistantMsg, userId);

        return assistantResponse;
    }

    /// <summary>
    /// Új chat nyitásakor automatikusan legenerálja az üdvözlő üzenetet 3 ajánlott témával.
    /// </summary>
    public async Task<Models.ChatMessage> GenerateInitialRecommendationsAsync(ChatSession chat, string userId)
    {
        var plugin = new UserStrategistPlugin(_cosmosDb, userId);

        // Futtatjuk a teljes autonóm 3-ágenses workflow-t
        string topicsResponse = await plugin.GenerateRecommendationsAsync(_kernel);

        // Ha valamiért az ágensek teljesen besültek
        if (string.IsNullOrWhiteSpace(topicsResponse) || topicsResponse.Length < 10)
        {
            topicsResponse = "Milyen volt a mai napod?\nMilyen projekteken vagy feladatokon dolgozol mostanában?\nMi volt a legérdekesebb dolog, amit mostanában tanultál?";
        }

        var topics = topicsResponse.Split('\n', System.StringSplitOptions.RemoveEmptyEntries).Select(t => t.Trim('-').Trim()).ToList();

        var asstMsg = new Models.ChatMessage
        {
            ChatId = chat.Id,
            ProjectId = chat.ProjectId, // Ha projekt része, a db szolgáltatás itt tölti fel
            UserId = userId,
            Role = "assistant",
            Content = "Szia! Új chatet nyitottál. Ezeket a témákat javaslom kezdésnek:",
            Topics = topics
        };
        
        await _cosmosDb.UpsertMessageAsync(asstMsg, userId);
        return asstMsg;
    }

    /// <summary>
    /// Belső metódus a projekt szintű szummarizációhoz
    /// </summary>
    private async Task UpdateProjectJointSummaryAsync(Project project, List<Models.ChatMessage> currentMessages, string userId)
    {
        var summaryPrompt = new List<OpenAI.Chat.ChatMessage>
        {
            new SystemChatMessage(@"You are an AI assistant tasked with maintaining a project's global memory. 
Your goal is to update the existing project summary by incorporating new key information, facts, and decisions from the recent conversation.
- Do NOT rewrite or drop important previously established facts unless the new conversation explicitly contradicts them.
- Keep the summary clear, well-structured, and concise.
- The summary length MUST be exactly 15 sentences.
- Output ONLY the newly updated, blended summary text and nothing else."),
            new UserChatMessage($"Existing Project Summary:\n{project.JointSummary ?? "None"}\n\nRecent Conversation to summarize:\n" +
                string.Join("\n", currentMessages.TakeLast(SummarizationTrigger * 2).Select(m => $"{m.Role}: {m.Content}")))
        };

        var summaryCompletion = await _chatClient.CompleteChatAsync(summaryPrompt);
        project.JointSummary = summaryCompletion.Value.Content[0].Text;

        // Csak a projektet frissítjük az új összefoglalóval a Cosmosban
        await _cosmosDb.UpsertProjectAsync(project, userId);
    }

    /// <summary>
    /// Chat áthelyezésekor használt, az egész chatet beolvasztó összegzés.
    /// </summary>
    private async Task MergeChatIntoProjectSummaryAsync(Project project, List<Models.ChatMessage> movedMessages, string userId)
    {
        var summaryPrompt = new List<OpenAI.Chat.ChatMessage>
        {
            new SystemChatMessage(@"You are an AI assistant tasked with updating a project's global memory. 
A new chat thread has been moved into this project. 
Your job is to extract the key facts, context, details, and decisions from this entirely new chat thread and integrate them intelligently into the existing project summary.
- Retain previously established facts in the project summary, unless updated by the new thread.
- Ensure the newly integrated context is well-structured and concise.
- The summary length MUST be exactly 15 sentences.
- Output ONLY the newly updated, blended summary text and nothing else."),
            new UserChatMessage($"Existing Project Summary:\n{project.JointSummary ?? "None"}\n\nNew Chat Thread to Merge:\n" +
                string.Join("\n", movedMessages.TakeLast(50).Select(m => $"{m.Role}: {m.Content}")))
        };

        var summaryCompletion = await _chatClient.CompleteChatAsync(summaryPrompt);
        project.JointSummary = summaryCompletion.Value.Content[0].Text;

        await _cosmosDb.UpsertProjectAsync(project, userId);
    }

    /// <summary>
    /// Standalone chat kinevezése projekt chatté.
    /// </summary>
    public async Task MoveChatToProjectAsync(ChatSession chat, Project targetProject, string userId)
    {
        var originalProjectId = chat.ProjectId;

        // 1. Chat frissítése
        chat.ProjectId = targetProject.Id;
        await _cosmosDb.UpsertProjectChatAsync(chat, targetProject.Id, userId);

        // 2. Üzenetek mozgatása (ProjectId frissítése)
        var messages = await _cosmosDb.GetChatMessagesAsync(originalProjectId ?? chat.Id, chat.Id, userId);
        foreach (var msg in messages)
        {
            msg.ProjectId = targetProject.Id;
            await _cosmosDb.UpsertMessageAsync(msg, userId);
        }

        // 3. Projekt joint summary frissítése a chat áthelyezése miatt
        // Itt nem csak egy általános ablakot (SlidingWindow) kérünk le, hanem az imént mozgatott üzeneteket blendeljük bele a projekt memóriájába!
        if (messages.Any(m => m.Role != "system"))
        {
            await MergeChatIntoProjectSummaryAsync(targetProject, messages.Where(m => m.Role != "system").ToList(), userId);
        }
    }

    /// <summary>
    /// Felhasználói profil alapján frissített összefoglaló generálása.
    /// </summary>
    public async Task<string> GenerateUserProfileSummaryAsync(string userId, List<Models.ChatMessage> userRecentMessages)
    {
        var prompt = new List<OpenAI.Chat.ChatMessage>
        {
            new SystemChatMessage(@"Te egy profilozó AI vagy. Szeretném, ha a felhasználó korábbi beszélgetései és kérdései alapján írnál egy pontosan 10 mondatos összefoglalót róla. 
Térj ki arra, hogy mik az érdeklődési körei, valószínűleg mivel foglalkozik, mit tanul, és milyen stílusban kommunikál. A válaszod kizárólag a profilozó szöveg legyen. Ne írj felvezetést, és pontosan 10 mondatot használj."),
            new UserChatMessage("Itt vannak a felhasználó legutóbbi üzenetei:\n\n" +
                string.Join("\n", userRecentMessages.Select(m => $"- {m.Content}")))
        };

        var completion = await _chatClient.CompleteChatAsync(prompt);
        var generatedSummary = completion.Value.Content[0].Text;

        // Szöveg mentése a CosmosDB-be
        var userProfile = await _cosmosDb.GetUserProfileAsync(userId) ?? new UserProfile();
        userProfile.Summary = generatedSummary;
        userProfile.LastUpdated = DateTime.UtcNow;

        await _cosmosDb.UpsertUserProfileAsync(userProfile, userId);

        return generatedSummary;
    }
}
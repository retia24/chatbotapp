using Azure;
using Azure.AI.OpenAI;
using OpenAI.Chat;
using System.Collections.Generic;
using System.Threading.Tasks;

public class ChatService
{
    private readonly ChatClient _chatClient;

    public ChatService(IConfiguration config)
    {
        var endpoint = config["OpenAI:Endpoint"];
        var apiKey = config["OpenAI:ApiKey"]; // Key Vaultból
        var deploymentName = config["OpenAI:DeploymentName"];

        var azureClient = new AzureOpenAIClient(
            new Uri(endpoint!),
            new AzureKeyCredential(apiKey!));

        _chatClient = azureClient.GetChatClient(deploymentName);
    }

    public async Task<string> SendMessageAsync(List<ChatMessage> history)
    {
        // Elküldjük a teljes eddigi beszélgetést (history), hogy emlékezzen a kontextusra
        ChatCompletion completion = await _chatClient.CompleteChatAsync(history);
        return completion.Content[0].Text;
    }
}
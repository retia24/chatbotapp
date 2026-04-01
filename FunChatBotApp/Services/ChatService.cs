using Azure;
using Azure.AI.OpenAI;
using FunChatBotApp.Models;
using FunChatBotApp.Services;
using OpenAI.Chat;
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

    // Beállítások a memóriához
    private const int SlidingWindowSize = 10; // Csak az utolsó N üzenetet küldjük a fő chatnél
    private const int SummarizationTrigger = 6; // Minden N-edik üzenetváltás után frissítjük a közös memóriát

    public ChatService(IConfiguration config, CosmosDbService cosmosDb)
    {
        _cosmosDb = cosmosDb;

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
    public async Task<string> ProcessProjectMessageAsync(Project project, ChatSession chat, string userMessage, bool includeDocumentContext = false)
    {
        // 1. Felhasználói üzenet rögzítése
        chat.Messages.Add(new Models.ChatMessage { Role = "user", Content = userMessage });

        // 2. OpenAI kontextus építése (Sliding window + Joint Summary)
        var openAiHistory = new List<OpenAI.Chat.ChatMessage>();

        if (!string.IsNullOrEmpty(project.JointSummary))
        {
            // A projekt globális memóriáját System promptként adjuk át
            openAiHistory.Add(new SystemChatMessage($"Project Context/Memory: {project.JointSummary}"));
        }

        // Új logika: Külön tárolt dokumentum beemelése szükség esetén
        if (includeDocumentContext && !string.IsNullOrEmpty(chat.ActiveDocumentText))
        {
            var textToInject = chat.ActiveDocumentText;
            if (textToInject.Length > 30000)
            {
                textToInject = textToInject.Substring(0, 30000) + "\n... [TARTALOM LEVÁGVA A HOSSZ MIATT]";
            }
            openAiHistory.Add(new SystemChatMessage($"Reference Document:\n<document>\n{textToInject}\n</document>"));
        }

        // Sliding window
        var recentMessages = chat.Messages.TakeLast(SlidingWindowSize);
        foreach (var msg in recentMessages)
        {
            if (msg.Role == "user")
                openAiHistory.Add(new UserChatMessage(msg.Content));
            else if (msg.Role == "assistant")
                openAiHistory.Add(new AssistantChatMessage(msg.Content));
            else
                openAiHistory.Add(new SystemChatMessage(msg.Content));
        }

        // 3. Lekérjük a választ az OpenAI Complete Chat (Completion) API-tól
        ClientResult<ChatCompletion> completion = await _chatClient.CompleteChatAsync(openAiHistory);
        var assistantResponse = completion.Value.Content[0].Text;

        // 4. Asszisztens válaszának rögzítése
        chat.Messages.Add(new Models.ChatMessage { Role = "assistant", Content = assistantResponse });

        // 5. Szummázás és mentés
        // Ha elértük a limitet, akkor összevonjuk a mostani dolgokat a JointSummary-vel
        if (chat.Messages.Count(m => m.Role == "user") % SummarizationTrigger == 0)
        {
            await UpdateProjectJointSummaryAsync(project, chat);
        }
        else
        {
            // Egyébként csak simán elmentjük a frissített projekt-állapotot a Cosmosba
            // (Mivel a chat a projekt része, így a chat is mentésre kerül a JSON struktúrában)
            await _cosmosDb.UpsertProjectAsync(project);
        }

        return assistantResponse;
    }

    /// <summary>
    /// "Szimpla", egyéni beszélgetés külön projekt és memória nélkül
    /// </summary>
    public async Task<string> ProcessStandaloneMessageAsync(ChatSession chat, string userMessage, bool includeDocumentContext = false)
    {
        chat.Messages.Add(new Models.ChatMessage { Role = "user", Content = userMessage });

        var openAiHistory = new List<OpenAI.Chat.ChatMessage>();

        // Új logika: Külön tárolt dokumentum beemelése szükség esetén
        if (includeDocumentContext && !string.IsNullOrEmpty(chat.ActiveDocumentText))
        {
            var textToInject = chat.ActiveDocumentText;
            if (textToInject.Length > 30000)
            {
                textToInject = textToInject.Substring(0, 30000) + "\n... [TARTALOM LEVÁGVA A HOSSZ MIATT]";
            }
            openAiHistory.Add(new SystemChatMessage($"Reference Document:\n<document>\n{textToInject}\n</document>"));
        }

        var recentMessages = chat.Messages.TakeLast(SlidingWindowSize);
        foreach (var msg in recentMessages)
        {
            if (msg.Role == "user")
                openAiHistory.Add(new UserChatMessage(msg.Content));
            else if (msg.Role == "assistant")
                openAiHistory.Add(new AssistantChatMessage(msg.Content));
            else
                openAiHistory.Add(new SystemChatMessage(msg.Content));
        }

        ClientResult<ChatCompletion> completion = await _chatClient.CompleteChatAsync(openAiHistory);
        var assistantResponse = completion.Value.Content[0].Text;

        chat.Messages.Add(new Models.ChatMessage { Role = "assistant", Content = assistantResponse });

        // Szummarizáció nélkül mentjük egyszerű chatként
        await _cosmosDb.UpsertStandaloneChatAsync(chat);

        return assistantResponse;
    }

    /// <summary>
    /// Belső metódus a projekt szintű szummarizációhoz
    /// </summary>
    private async Task UpdateProjectJointSummaryAsync(Project project, ChatSession triggeringChat)
    {
        var summaryPrompt = new List<OpenAI.Chat.ChatMessage>
        {
            new SystemChatMessage("You are a helpful assistant that summarizes key information, facts, and decisions from a conversation. Blend the recent conversation with the existing project summary. Return only the updated summary. Be concise."),
            new UserChatMessage($"Current Project Summary: {project.JointSummary ?? "None"}\n\nRecent Conversation to summarize:\n" +
                string.Join("\n", triggeringChat.Messages.TakeLast(SummarizationTrigger * 2).Select(m => $"{m.Role}: {m.Content}"))) // *2 mert egy emberi és egy AI válasz = 1 pár
        };

        var summaryCompletion = await _chatClient.CompleteChatAsync(summaryPrompt);
        project.JointSummary = summaryCompletion.Value.Content[0].Text;

        // Ezzel elmentjük a Cosmos DB-be az új összefoglalót + az új üzeneteket is
        await _cosmosDb.UpsertProjectAsync(project);
    }
}
using Newtonsoft.Json;
using System;

namespace FunChatBotApp.Models;

// Közös ősosztály a Cosmos DB konténer gyökérelemeinek
public abstract class WorkspaceDocument
{
    [JsonProperty("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    // Ez lesz a Partition Key a Cosmos DB-ben (/projectId)
    [JsonProperty("projectId")]
    public string ProjectId { get; set; } = string.Empty;

    // Megkülönbözteti, hogy a dokumentum "Project", "StandaloneChat" vagy "Message"
    [JsonProperty("type")]
    public string Type { get; set; } = string.Empty;
}

// 1. Projekt osztály, ami 1 db dokumentum a Cosmos DB-ben
public class Project : WorkspaceDocument
{
    public Project()
    {
        Type = "Project";
    }

    public string Name { get; set; } = string.Empty;

    // A közös memória (Summary) a projekthez tartozó chatek alapján
    public string JointSummary { get; set; } = string.Empty;

    // Nincs List<ChatSession>, a CosmosDB-ből kérdezzük le külön
}

// 2. ChatSession osztály (Ez is 1 db önálló dokumentum)
public class ChatSession : WorkspaceDocument
{
    public ChatSession()
    {
        Type = "StandaloneChat"; 
    }

    public string Title { get; set; } = string.Empty;

    // Az aktuális beszélgetéshez csatolt dokumentum nyers szövege
    public string? ActiveDocumentText { get; set; }

    // Nincs List<ChatMessage>, a CosmosDB-ből kérdezzük le külön
}

// 3. ChatMessage osztály (Minden üzenet 1 új dokumentum lesz)
public class ChatMessage : WorkspaceDocument
{
    public ChatMessage()
    {
        Type = "Message";
    }

    // Melyik chathez tartozik
    [JsonProperty("chatId")]
    public string ChatId { get; set; } = string.Empty;

    [JsonProperty("role")]
    public string Role { get; set; } = string.Empty; // "user", "assistant", vagy "system"

    [JsonProperty("content")]
    public string Content { get; set; } = string.Empty;

    [JsonProperty("timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
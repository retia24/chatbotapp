using Newtonsoft.Json;

namespace FunChatBotApp.Models;

// Közös ősosztály a Cosmos DB konténer gyökérelemeinek
public abstract class WorkspaceDocument
{
    [JsonProperty("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    // Ez lesz a Partition Key a Cosmos DB-ben (/projectId)
    [JsonProperty("projectId")]
    public string ProjectId { get; set; } = string.Empty;

    // Megkülönbözteti, hogy a dokumentum "Project" vagy "StandaloneChat"
    [JsonProperty("type")]
    public string Type { get; set; } = string.Empty;
}

// 1. Projekt osztály, ami tartalmazza a chateket
public class Project : WorkspaceDocument
{
    public Project()
    {
        Type = "Project";
    }

    public string Name { get; set; } = string.Empty;
    
    // A közös memória (Summary) a projekthez tartozó chatek alapján
    public string JointSummary { get; set; } = string.Empty;

    // A projekt tartalmazza a saját chatjeit
    public List<ChatSession> Chats { get; set; } = new();
}

// 2. ChatSession osztály (Lehet egyedülálló, vagy egy Projekt része)
public class ChatSession : WorkspaceDocument
{
    public ChatSession()
    {
        Type = "StandaloneChat"; // Ha projekten belül van, a "Type" mezőt a Cosmos DB nem root szinten fogja értelmezni
    }

    public string Title { get; set; } = string.Empty;
    
    public List<ChatMessage> Messages { get; set; } = new();
}

// 3. ChatMessage osztály
public class ChatMessage
{
    [JsonProperty("role")]
    public string Role { get; set; } = string.Empty; // "user", "assistant", vagy "system"

    [JsonProperty("content")]
    public string Content { get; set; } = string.Empty;

    [JsonProperty("timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
using FunChatBotApp.Models;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Linq;

namespace FunChatBotApp.Services;

public class CosmosDbService
{
    private readonly Container _container;

    public CosmosDbService(IConfiguration config)
    {
        // A Connection String a Key Vaultból jön az előzőleg beállított nevével.
        // Mivel be van kötve az Azure Key Vault a Program.cs-ben, az IConfiguration
        // automatikusan megtalálja a titkot a Vaultban.
        var endpointUri = config["CosmosDb:EndpointUri"];
        var accountKey = config["CosmosDbConnection"];

        var databaseName = config["CosmosDb:DatabaseName"];
        var containerName = config["CosmosDb:ContainerName"];

        // Client példányosítása a teljes connection stringgel
        var client = new CosmosClient(endpointUri, accountKey);
        _container = client.GetContainer(databaseName, containerName);
    }

    // ==========================================
    // Projekt - műveletek
    // ==========================================

    public async Task<List<Project>> GetProjectsAsync(string userId)
    {
        var query = _container.GetItemLinqQueryable<Project>(requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(userId) })
            .Where(x => x.Type == "Project" && x.UserId == userId)
            .ToFeedIterator();

        var results = new List<Project>();
        while (query.HasMoreResults)
        {
            var response = await query.ReadNextAsync();
            results.AddRange(response);
        }
        return results;
    }

    public async Task<Project?> GetProjectAsync(string id, string userId)
    {
        try
        {
            // A projekt ProjectId-ja mindig a saját Id-ja a struktúránk alapján
            ItemResponse<Project> response = await _container.ReadItemAsync<Project>(id, new PartitionKey(userId));
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task UpsertProjectAsync(Project project, string userId)
    {
        project.Type = "Project";
        project.UserId = userId;
        if (string.IsNullOrEmpty(project.ProjectId))
        {
            project.ProjectId = project.Id; // A partíció kulcs a projekt ID-ja lesz
        }

        await _container.UpsertItemAsync(project, new PartitionKey(userId));
    }

    public async Task DeleteProjectAsync(string id, string userId)
    {
        await _container.DeleteItemAsync<Project>(id, new PartitionKey(userId));
    }


    // ==========================================
    // Standalone (egyszerű) Chat - műveletek
    // ==========================================

    public async Task<List<ChatSession>> GetStandaloneChatsAsync(string userId)
    {
        // A Type alapján listázzuk azokat a chateket, amiknek a ProjectId megegyezik a saját Id-val
        var query = new QueryDefinition("SELECT * FROM c WHERE c.type = 'StandaloneChat' AND c.projectId = c.id");

        var iterator = _container.GetItemQueryIterator<ChatSession>(query, requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(userId) });
        var results = new List<ChatSession>();

        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync();
            results.AddRange(response);
        }
        return results;
    }

    public async Task<ChatSession?> GetStandaloneChatAsync(string id, string userId)
    {
        try
        {
            // Az egyszerű chateknél megegyeztünk egy közös "Standalone" partícióban
            ItemResponse<ChatSession> response = await _container.ReadItemAsync<ChatSession>(id, new PartitionKey(userId));
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task UpsertStandaloneChatAsync(ChatSession chat, string userId)
    {
        chat.Type = "StandaloneChat";
        chat.UserId = userId;
        if (string.IsNullOrEmpty(chat.ProjectId))
        {
            chat.ProjectId = chat.Id; // Közös partíció kulcs az egyedülálló chateknek
        }

        await _container.UpsertItemAsync(chat, new PartitionKey(userId));
    }

    public async Task DeleteStandaloneChatAsync(string id, string userId)
    {
        await _container.DeleteItemAsync<ChatSession>(id, new PartitionKey(userId));
    }

    // ==========================================
    // Projekt Chatek - műveletek
    // ==========================================

    public async Task<List<ChatSession>> GetProjectChatsAsync(string projectId, string userId)
    {
        var query = new QueryDefinition("SELECT * FROM c WHERE c.projectId = @projectId AND c.type = 'StandaloneChat'")
            .WithParameter("@projectId", projectId);

        var iterator = _container.GetItemQueryIterator<ChatSession>(
            query, 
            requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(userId) });

        var results = new List<ChatSession>();
        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync();
            results.AddRange(response);
        }
        return results;
    }

    public async Task UpsertProjectChatAsync(ChatSession chat, string projectId, string userId)
    {
        chat.Type = "StandaloneChat";
        chat.ProjectId = projectId;
        chat.UserId = userId;

        await _container.UpsertItemAsync(chat, new PartitionKey(userId));
    }

    // ==========================================
    // Üzenetek - műveletek
    // ==========================================

    public async Task<List<ChatMessage>> GetChatMessagesAsync(string projectId, string chatId, string userId)
    {
        // Az üzeneteket időrendben kérjük le az adott partícióból és chatből
        var query = new QueryDefinition("SELECT * FROM c WHERE c.projectId = @projectId AND c.chatId = @chatId AND c.type = 'Message' ORDER BY c.timestamp ASC")
            .WithParameter("@projectId", projectId)
            .WithParameter("@chatId", chatId);

        var iterator = _container.GetItemQueryIterator<ChatMessage>(
            query,
            requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(userId) });

        var results = new List<ChatMessage>();
        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync();
            results.AddRange(response);
        }
        return results;
    }

    public async Task<List<ChatMessage>> GetProjectMessagesAsync(string projectId, string userId, int? slidingWindowSize = null)
    {
        var query = new QueryDefinition("SELECT * FROM c WHERE c.projectId = @projectId AND c.type = 'Message' ORDER BY c.timestamp ASC")
            .WithParameter("@projectId", projectId);

        var iterator = _container.GetItemQueryIterator<ChatMessage>(
            query,
            requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(userId) });

        var results = new List<ChatMessage>();
        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync();
            results.AddRange(response);
        }

        if (slidingWindowSize.HasValue && slidingWindowSize.Value > 0)
        {
            results = results
                .GroupBy(m => m.ChatId)
                .SelectMany(g => g.TakeLast(slidingWindowSize.Value))
                .OrderBy(m => m.Timestamp)
                .ToList();
        }

        return results;
    }

    public async Task UpsertMessageAsync(ChatMessage message, string userId)
    {
        message.Type = "Message";
        message.UserId = userId;
        await _container.UpsertItemAsync(message, new PartitionKey(userId));
    }
}

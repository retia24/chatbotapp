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

    public async Task<List<Project>> GetProjectsAsync()
    {
        var query = _container.GetItemLinqQueryable<Project>()
            .Where(x => x.Type == "Project")
            .ToFeedIterator();

        var results = new List<Project>();
        while (query.HasMoreResults)
        {
            var response = await query.ReadNextAsync();
            results.AddRange(response);
        }
        return results;
    }

    public async Task<Project?> GetProjectAsync(string id)
    {
        try
        {
            // A projekt ProjectId-ja mindig a saját Id-ja a struktúránk alapján
            ItemResponse<Project> response = await _container.ReadItemAsync<Project>(id, new PartitionKey(id));
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task UpsertProjectAsync(Project project)
    {
        project.Type = "Project";
        if (string.IsNullOrEmpty(project.ProjectId))
        {
            project.ProjectId = project.Id; // A partíció kulcs a projekt ID-ja lesz
        }

        await _container.UpsertItemAsync(project, new PartitionKey(project.ProjectId));
    }

    public async Task DeleteProjectAsync(string id)
    {
        await _container.DeleteItemAsync<Project>(id, new PartitionKey(id));
    }


    // ==========================================
    // Standalone (egyszerű) Chat - műveletek
    // ==========================================

    public async Task<List<ChatSession>> GetStandaloneChatsAsync()
    {
        var query = _container.GetItemLinqQueryable<ChatSession>()
            .Where(x => x.Type == "StandaloneChat")
            .ToFeedIterator();

        var results = new List<ChatSession>();
        while (query.HasMoreResults)
        {
            var response = await query.ReadNextAsync();
            results.AddRange(response);
        }
        return results;
    }

    public async Task<ChatSession?> GetStandaloneChatAsync(string id)
    {
        try
        {
            // Az egyszerű chateknél megegyeztünk egy közös "Standalone" partícióban
            ItemResponse<ChatSession> response = await _container.ReadItemAsync<ChatSession>(id, new PartitionKey(id));
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task UpsertStandaloneChatAsync(ChatSession chat)
    {
        chat.Type = "StandaloneChat";
        if (string.IsNullOrEmpty(chat.ProjectId))
        {
            chat.ProjectId = chat.Id; // Közös partíció kulcs az egyedülálló chateknek
        }

        await _container.UpsertItemAsync(chat, new PartitionKey(chat.ProjectId));
    }

    public async Task DeleteStandaloneChatAsync(string id)
    {
        await _container.DeleteItemAsync<ChatSession>(id, new PartitionKey(id));
    }
}

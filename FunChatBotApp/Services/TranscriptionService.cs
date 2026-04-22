using Azure.Storage.Blobs;
using Azure.Storage.Sas;
using FunChatBotApp.Models;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text;

namespace FunChatBotApp.Services;

public class TranscriptionService
{
    private readonly CosmosDbService _cosmosDb;
    private readonly HttpClient _httpClient;
    private readonly string _blobConnectionString;
    private readonly string _blobContainer;
    private readonly string _speechEndpoint;
    private readonly string _speechKey;

    public TranscriptionService(CosmosDbService cosmosDb, IConfiguration config, HttpClient httpClient)
    {
        _cosmosDb = cosmosDb;
        _httpClient = httpClient;
        _blobConnectionString = config["BlobStorage:ConnectionString"];
        _blobContainer = config["BlobStorage:ContainerName"];
        _speechEndpoint = config["SpeechService:Endpoint"];
        _speechKey = config["SpeechService:ApiKey"];
    }

    public async Task<AudioTranscriptionDocument> StartTranscriptionAsync(Stream fileStream, string fileName, string userId)
    {
        // 1. Feltöltés a Blob Storage-ba
        var blobServiceClient = new BlobServiceClient(_blobConnectionString);
        var containerClient = blobServiceClient.GetBlobContainerClient(_blobContainer);
        await containerClient.CreateIfNotExistsAsync();

        string blobName = $"{Guid.NewGuid()}_{fileName}";
        var blobClient = containerClient.GetBlobClient(blobName);

        await blobClient.UploadAsync(fileStream, overwrite: true);

        // SAS token generálás a Speech API számára (privát blobhoz)
        var sasBuilder = new BlobSasBuilder
        {
            BlobContainerName = _blobContainer,
            BlobName = blobName,
            Resource = "b",
            StartsOn = DateTimeOffset.UtcNow,
            ExpiresOn = DateTimeOffset.UtcNow.AddDays(1)
        };
        sasBuilder.SetPermissions(BlobSasPermissions.Read);

        Uri sasUri = blobClient.GenerateSasUri(sasBuilder);

        // 2. Dokumentum mentése Cosmos DB-be Uploading státusszal
        var doc = new AudioTranscriptionDocument
        {
            OriginalFileName = fileName,
            BlobUrl = blobClient.Uri.ToString(),
            Status = TranscriptionState.Uploading
        };
        await _cosmosDb.UpsertTranscriptionAsync(doc, userId);

        // 3. Hívás a Speech Service Batch API-hoz
        var requestPayload = new
        {
            contentUrls = new[] { sasUri.ToString() },
            properties = new
            {
                diarizationEnabled = false,
                wordLevelTimestampsEnabled = false,
                punctuationMode = "DictatedAndAutomatic",
                profanityFilterMode = "Masked"
            },
            locale = "hu-HU",

            displayName = $"Transcription_{fileName}"
        };

        var requestContent = new StringContent(JsonSerializer.Serialize(requestPayload), Encoding.UTF8, "application/json");

        var requestMessage = new HttpRequestMessage(HttpMethod.Post, $"{_speechEndpoint}/transcriptions")
        {
            Content = requestContent
        };
        requestMessage.Headers.Add("Ocp-Apim-Subscription-Key", _speechKey);

        var response = await _httpClient.SendAsync(requestMessage);

        if (response.IsSuccessStatusCode)
        {
            var responseContent = await response.Content.ReadAsStringAsync();
            using var jsonDocument = JsonDocument.Parse(responseContent);
            var selfUri = jsonDocument.RootElement.GetProperty("self").GetString();

            doc.ApiTranscriptionUri = selfUri ?? string.Empty;
            doc.Status = TranscriptionState.Processing;
            await _cosmosDb.UpsertTranscriptionAsync(doc, userId);
        }
        else
        {
            doc.Status = TranscriptionState.Failed;
            doc.ErrorMessage = await response.Content.ReadAsStringAsync();
            await _cosmosDb.UpsertTranscriptionAsync(doc, userId);
        }

        return doc;
    }

    public async Task<AudioTranscriptionDocument> CheckStatusAsync(AudioTranscriptionDocument doc, string userId)
    {
        if (doc.Status == TranscriptionState.Completed || doc.Status == TranscriptionState.Failed)
            return doc;

        if (string.IsNullOrEmpty(doc.ApiTranscriptionUri))
            return doc;

        var requestMessage = new HttpRequestMessage(HttpMethod.Get, doc.ApiTranscriptionUri);
        requestMessage.Headers.Add("Ocp-Apim-Subscription-Key", _speechKey);

        var response = await _httpClient.SendAsync(requestMessage);
        if (response.IsSuccessStatusCode)
        {
            var responseContent = await response.Content.ReadAsStringAsync();
            using var jsonDocument = JsonDocument.Parse(responseContent);

            var statusStr = jsonDocument.RootElement.GetProperty("status").GetString();

            if (statusStr == "Succeeded")
            {
                // Letöltjük az eredmény linkjét a files endpointról
                var filesUri = jsonDocument.RootElement.GetProperty("links").GetProperty("files").GetString();
                await DownloadTranscriptResultAsync(doc, filesUri!, userId);
            }
            else if (statusStr == "Failed")
            {
                doc.Status = TranscriptionState.Failed;
                await _cosmosDb.UpsertTranscriptionAsync(doc, userId);
            }
        }
        return doc;
    }

    private async Task DownloadTranscriptResultAsync(AudioTranscriptionDocument doc, string filesUri, string userId)
    {
        var requestMessage = new HttpRequestMessage(HttpMethod.Get, filesUri);
        requestMessage.Headers.Add("Ocp-Apim-Subscription-Key", _speechKey);

        var response = await _httpClient.SendAsync(requestMessage);
        if (response.IsSuccessStatusCode)
        {
            var responseContent = await response.Content.ReadAsStringAsync();
            using var filesDoc = JsonDocument.Parse(responseContent);
            var values = filesDoc.RootElement.GetProperty("values");

            string? contentUrl = null;
            foreach (var val in values.EnumerateArray())
            {
                var kind = val.GetProperty("kind").GetString();
                if (kind == "Transcription")
                {
                    contentUrl = val.GetProperty("links").GetProperty("contentUrl").GetString();
                    break;
                }
            }

            if (!string.IsNullOrEmpty(contentUrl))
            {
                // A tényleges leirat fájl letöltése
                var resultResponse = await _httpClient.GetAsync(contentUrl);
                if (resultResponse.IsSuccessStatusCode)
                {
                    var resultStr = await resultResponse.Content.ReadAsStringAsync();
                    using var resultDoc = JsonDocument.Parse(resultStr);

                    // A v3.2 API felépítése általában tartalmaz egy combinedRecognizedPhrases listát
                    var combinedPhrases = resultDoc.RootElement.GetProperty("combinedRecognizedPhrases");
                    if (combinedPhrases.GetArrayLength() > 0)
                    {
                        var display = combinedPhrases[0].GetProperty("display").GetString();
                        doc.TranscriptText = display ?? string.Empty;
                        doc.Status = TranscriptionState.Completed;
                        await _cosmosDb.UpsertTranscriptionAsync(doc, userId);
                        return;
                    }
                }
            }
        }

        doc.Status = TranscriptionState.Failed;
        doc.ErrorMessage = "Sikeres állapot, de hiba történt a leirat fájl letöltésekor vagy feldolgozásakor.";
        await _cosmosDb.UpsertTranscriptionAsync(doc, userId);
    }
}

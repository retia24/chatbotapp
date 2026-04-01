using Azure;
using Azure.AI.DocumentIntelligence;
using System.IO;
using System.Threading.Tasks;

public class DocumentService
{
    private readonly DocumentIntelligenceClient _client;

    public DocumentService(IConfiguration config)
    {
        var endpoint = config["DocIntel:Endpoint"];
        var apiKey = config["DocIntel:ApiKey"]; // Key Vaultból jön

        _client = new DocumentIntelligenceClient(
            new Uri(endpoint!),
            new AzureKeyCredential(apiKey!));
    }

    public async Task<string> ExtractTextFromStreamAsync(Stream fileStream)
    {
        // A "prebuilt-read" modell tökéletes általános szövegkinyerésre docx, pdf, pptx, txt fájlokból
        var content = await BinaryData.FromStreamAsync(fileStream);

        Operation<AnalyzeResult> operation = await _client.AnalyzeDocumentAsync(
            WaitUntil.Completed,
            "prebuilt-read",
            content);

        return operation.Value.Content; // Visszaadja a nyers szöveget
    }
}
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using System.Net.Http.Headers;
using System.IO;
using System.Text;
using System.Text.Json;

var confbuilder = new ConfigurationBuilder();

// Add the appsettings.json from the main project
string appSettingsPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, @"..\..\..\..\FunChatBotApp\appsettings.json"));
confbuilder.AddJsonFile(appSettingsPath, optional: true);

string keyVaultUrl = "https://funchatbot-akv.vault.azure.net/";
confbuilder.AddAzureKeyVault(new Uri(keyVaultUrl), new DefaultAzureCredential());

var config = confbuilder.Build();
string aiEndpoint = config["OpenAI:Endpoint"]!;
string aiKey = config["OpenAI:ApiKey"]!;

// 1. Saját HttpClient létrehozása 5 perces időtúllépéssel
var customHttpClient = new HttpClient
{
    Timeout = TimeSpan.FromMinutes(5)
};

// 2. Azure OpenAI Responses API inicializálása (gpt-5.3-codex deployment/model)
customHttpClient.DefaultRequestHeaders.Clear();
customHttpClient.DefaultRequestHeaders.Add("api-key", aiKey);
customHttpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

// Mappák és könyvtárak feloldása biztonságosan
string baseDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, @"..\..\..\..\FunChatBotApp"));
string sourceProjectDir = Path.Combine(baseDir, "Services");
string testProjectDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, @"..\..\..\..\FunChatBotApp.Tests\GeneratedTests"));

if (!Directory.Exists(testProjectDir))
{
    Directory.CreateDirectory(testProjectDir);
}

// 1. LÉPÉS: A Tudásbázis (Kontextus) felépítése
string[] contextFolders = { "Models", "Data", "Plugins" };
StringBuilder contextBuilder = new StringBuilder();

foreach (var folder in contextFolders)
{
    string fullPath = Path.Combine(baseDir, folder);
    if (Directory.Exists(fullPath))
    {
        var files = Directory.GetFiles(fullPath, "*.cs", SearchOption.AllDirectories);
        foreach (var file in files)
        {
            contextBuilder.AppendLine($"// --- FÁJL: {folder}/{Path.GetFileName(file)} ---");
            contextBuilder.AppendLine(await File.ReadAllTextAsync(file));
            contextBuilder.AppendLine();
        }
    }
}
string knowledgeBase = contextBuilder.ToString();
Console.WriteLine("Tudásbázis beolvasva a Modellekből, Adatbázisból és a Pluginokból.");

string ExtractOutputText(JsonElement root)
{
    if (root.TryGetProperty("output_text", out var outputText) && outputText.ValueKind == JsonValueKind.String)
    {
        return outputText.GetString() ?? string.Empty;
    }

    if (root.TryGetProperty("output", out var output) && output.ValueKind == JsonValueKind.Array)
    {
        var textBuilder = new StringBuilder();

        foreach (var outputItem in output.EnumerateArray())
        {
            if (!outputItem.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var contentItem in content.EnumerateArray())
            {
                if (contentItem.TryGetProperty("type", out var type)
                    && type.ValueKind == JsonValueKind.String
                    && type.GetString() == "output_text"
                    && contentItem.TryGetProperty("text", out var text)
                    && text.ValueKind == JsonValueKind.String)
                {
                    textBuilder.Append(text.GetString());
                }
            }
        }

        return textBuilder.ToString();
    }

    return string.Empty;
}

// 2. LÉPÉS: Végigmegyünk a Service fájlokon (A tesztelendő fájlok)
var csFiles = Directory.GetFiles(sourceProjectDir, "*.cs");

foreach (var file in csFiles)
{
    string fileName = Path.GetFileNameWithoutExtension(file);
    Console.WriteLine($"Generálás ehhez: {fileName}...");

    try
    {
        string sourceCode = await File.ReadAllTextAsync(file);

        // 3. LÉPÉS: prompt összeállítása a Tudásbázis és a Tesztelendő Forráskód alapján
        string prompt = $@"
Te egy Senior C# .NET tesztelő mérnök vagy.
A feladatod, hogy xUnit teszteket írj a megadott TESZTELENDŐ C# osztályhoz.

KÖVETELMÉNYEK:
1. Használj xUnit keretrendszert és Moq-ot (a függőségek mockolásához).
2. Kövesd az Arrange-Act-Assert (AAA) mintát.
3. Kérlek, használd a megfelelő névtereket.
4. PONTOSAN a megadott REFERENCIA KÓDOK alapján mockold az entitásokat. Ne találj ki nem létező property-ket!
5. A válaszod KIZÁRÓLAG a nyers C# kód legyen, markdown formázás (```csharp) és magyarázatok NÉLKÜL! Csak maga a namespace és az osztály.

REFERENCIA KÓDOK (Modellek, Adatbázis, Pluginok - EZEKHEZ NE ÍRJ TESZTET, csak olvasd el őket a kontextushoz!):
{knowledgeBase}

TESZTELENDŐ FORRÁSKÓD (Ehhez írd a tesztet):
{sourceCode}
";

        string responsesUrl = $"{aiEndpoint.TrimEnd('/')}/openai/v1/responses";
        var payload = new
        {
            model = "gpt-5.3-codex",
            input = prompt
        };

        string payloadJson = JsonSerializer.Serialize(payload);
        using var requestContent = new StringContent(payloadJson, Encoding.UTF8, "application/json");
        using var response = await customHttpClient.PostAsync(responsesUrl, requestContent);
        response.EnsureSuccessStatusCode();

        string responseJson = await response.Content.ReadAsStringAsync();
        using var responseDoc = JsonDocument.Parse(responseJson);
        string generatedTestCode = ExtractOutputText(responseDoc.RootElement);

        generatedTestCode = generatedTestCode.Replace("```csharp", "").Replace("```", "").Trim();

        // 6. Tesztfájl mentése
        string outputFilePath = Path.Combine(testProjectDir, $"{fileName}Tests.cs");
        await File.WriteAllTextAsync(outputFilePath, generatedTestCode);

        Console.WriteLine($"[KÉSZ] Mentve: {outputFilePath}");

        // 7. újabb kérés előtt várakozás a Rate Limit miatt
        Console.WriteLine("Várakozás 5 másodpercet a Rate Limit miatt...");
        await Task.Delay(5000);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[ERROR] Failed to generate tests for {fileName}: {ex.Message}");
    }
}
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.SemanticKernel;
using System.IO;

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

// 2. Semantic Kernel inicializálása a biztonságos változókkal és a custom klienssel
var builder = Kernel.CreateBuilder();
builder.AddAzureOpenAIChatCompletion(
    deploymentName: "gpt-4o-mini",
    endpoint: aiEndpoint,
    apiKey: aiKey,
    httpClient: customHttpClient);

var kernel = builder.Build();

string sourceProjectDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, @"..\..\..\..\FunChatBotApp\Services"));
string testProjectDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, @"..\..\..\..\FunChatBotApp.Tests\GeneratedTests"));

if (!Directory.Exists(testProjectDir))
{
    Directory.CreateDirectory(testProjectDir);
}

var csFiles = Directory.GetFiles(sourceProjectDir, "*.cs");

foreach (var file in csFiles)
{
    string fileName = Path.GetFileNameWithoutExtension(file);
    Console.WriteLine($"Generating tests for: {fileName}...");

    try
    {
        string sourceCode = await File.ReadAllTextAsync(file);

        string prompt = $@"
Te egy Senior C# .NET tesztelő mérnök vagy.
A feladatod, hogy xUnit teszteket írj a megadott C# osztályhoz.

KÖVETELMÉNYEK:
1. Használj xUnit keretrendszert és Moq-ot (a függőségek mockolásához).
2. Kövesd az Arrange-Act-Assert (AAA) mintát.
3. Kérlek, használd a megfelelő névtereket: `using FunChatBotApp.Models;`, `using FunChatBotApp.Services;`
4. HA NEM ISMERED egy objektum pontos tulajdonságait (mert nincsenek benne a forráskódban), NE találj ki konkrét property-ket a mockolásnál, inkább használj `It.IsAny<T>()` megoldást, vagy hozz létre alapértelmezett példányokat!
5. A válaszod KIZÁRÓLAG a nyers C# kód legyen, markdown formázás (```csharp) és magyarázatok NÉLKÜL!

FORRÁSKÓD:
{sourceCode}
";

        var result = await kernel.InvokePromptAsync(prompt);
        string generatedTestCode = result.ToString();

        generatedTestCode = generatedTestCode.Replace("```csharp", "").Replace("```", "").Trim();

        // 6. Tesztfájl mentése
        string outputFilePath = Path.Combine(testProjectDir, $"{fileName}Tests.cs");
        await File.WriteAllTextAsync(outputFilePath, generatedTestCode);

        Console.WriteLine($"[KÉSZ] Mentve: {outputFilePath}");

        // VÉDŐHÁLÓ: Várunk 5 másodpercet a következő fájl előtt, hogy az Azure OpenAI "kifújhassa" magát
        Console.WriteLine("Várakozás 5 másodpercet a Rate Limit miatt...");
        await Task.Delay(5000);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[ERROR] Failed to generate tests for {fileName}: {ex.Message}");
    }
}
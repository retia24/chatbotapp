using Azure;
using Azure.AI.TextAnalytics;
using Microsoft.Extensions.Configuration;
using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace FunChatBotApp.Services;

public partial class TextAnalyticsService
{
    private readonly TextAnalyticsClient? _client;

    public TextAnalyticsService(IConfiguration config)
    {
        var languageEndpoint = config["AzureAILanguage:Endpoint"];
        var languageKey = config["AzureAILanguage:ApiKey"];

        if (!string.IsNullOrEmpty(languageEndpoint) && !string.IsNullOrEmpty(languageKey))
        {
            _client = new TextAnalyticsClient(
                new Uri(languageEndpoint),
                new AzureKeyCredential(languageKey));
        }
    }

    /// <summary>
    /// Kiszűri és maszkolja (pl. ***) a szenzitív adatokat a szövegben.
    /// Azure AI Language és lokális RegEx (magyar azonosítókhoz) kombinációjával.
    /// </summary>
    public async Task<string> RedactPiiAsync(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        string redactedText = text;

        // 1. Felhős NLP hívás az Azure AI Language segítségével
        if (_client != null)
        {
            try
            {
                PiiEntityCollection entities = await _client.RecognizePiiEntitiesAsync(redactedText, "hu");
                redactedText = entities.RedactedText;
            }
            catch
            {
                // Ha Azure error van, megyünk tovább a RegEx maszkolásra
            }
        }

        // 2. Lokális "Fine-Tuning" magyar azonosítókra RegEx-szel
        // Magyar személyi igazolvány: Hat szám + Két Betű (pl. 594239CX)
        redactedText = SzemelyiIgazolvanyRegex().Replace(redactedText, "********");

        // TAJ szám: Kilenc szám formázva vagy egyben (pl. 123 456 789 vagy 123-456-789 vagy 123456789)
        redactedText = TajSzamRegex().Replace(redactedText, "*********");

        // Adóazonosító jel: 10 szám, 8-assal kezdődik
        redactedText = AdoazonositoRegex().Replace(redactedText, "**********");

        return redactedText;
    }

    // RegEx minták kigenerálása
    
    [GeneratedRegex(@"\b\d{6}[A-Za-z]{2}\b")]
    private static partial Regex SzemelyiIgazolvanyRegex();

    [GeneratedRegex(@"\b\d{3}[-\s]?\d{3}[-\s]?\d{3}\b")]
    private static partial Regex TajSzamRegex();

    [GeneratedRegex(@"\b8\d{9}\b")]
    private static partial Regex AdoazonositoRegex();
}

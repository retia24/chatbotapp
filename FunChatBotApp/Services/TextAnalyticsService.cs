using Azure;
using Azure.AI.TextAnalytics;
using Microsoft.Extensions.Configuration;
using System;
using System.Threading.Tasks;

namespace FunChatBotApp.Services;

public class TextAnalyticsService
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
    /// </summary>
    public async Task<string> RedactPiiAsync(string text)
    {
        if (_client == null || string.IsNullOrWhiteSpace(text))
            return text;

        try
        {
            // "hu" nyelv megadása a pontosabb felismeréshez (angol esetén "en")
            PiiEntityCollection entities = await _client.RecognizePiiEntitiesAsync(text, "hu");

            return entities.RedactedText;
        }
        catch
        {
            // Fallback: hiba esetén ne akadjon meg a chat, adja vissza az eredetit
            return text;
        }
    }
}

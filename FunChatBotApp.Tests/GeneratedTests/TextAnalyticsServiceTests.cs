using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Xunit;
using FunChatBotApp.Services;

namespace FunChatBotApp.Tests.Services;

public class TextAnalyticsServiceTests
{
    [Fact]
    public async Task RedactPiiAsync_WhenTextIsNull_ReturnsNull()
    {
        var config = new ConfigurationBuilder().Build();
        var service = new TextAnalyticsService(config);

        var result = await service.RedactPiiAsync(null!);

        Assert.Null(result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RedactPiiAsync_WhenTextIsEmptyOrWhitespace_ReturnsOriginal(string input)
    {
        var config = new ConfigurationBuilder().Build();
        var service = new TextAnalyticsService(config);

        var result = await service.RedactPiiAsync(input);

        Assert.Equal(input, result);
    }

    [Fact]
    public async Task RedactPiiAsync_WhenNoAzureConfig_RedactsHungarianIdentifiersWithRegex()
    {
        var config = new ConfigurationBuilder().Build();
        var service = new TextAnalyticsService(config);
        var input = "Személyi: 594239CX, TAJ: 123 456 789, TAJ2: 123-456-789, TAJ3: 123456789, Adó: 8123456789";

        var result = await service.RedactPiiAsync(input);

        Assert.Contains("Személyi: ********", result);
        Assert.Contains("TAJ: *********", result);
        Assert.Contains("TAJ2: *********", result);
        Assert.Contains("TAJ3: *********", result);
        Assert.Contains("Adó: **********", result);
        Assert.DoesNotContain("594239CX", result);
        Assert.DoesNotContain("123 456 789", result);
        Assert.DoesNotContain("123-456-789", result);
        Assert.DoesNotContain("123456789", result);
        Assert.DoesNotContain("8123456789", result);
    }

    [Fact]
    public async Task RedactPiiAsync_WhenTextContainsNonMatchingPatterns_DoesNotRedact()
    {
        var config = new ConfigurationBuilder().Build();
        var service = new TextAnalyticsService(config);
        var input = "Nem érvényes: 59423CX, 7123456789, 12-3456-789";

        var result = await service.RedactPiiAsync(input);

        Assert.Equal(input, result);
    }

    [Fact]
    public async Task RedactPiiAsync_WhenMixedText_RedactsOnlyMatchingParts()
    {
        var config = new ConfigurationBuilder().Build();
        var service = new TextAnalyticsService(config);
        var input = "Név: Béla, személyi 111111AA, random 999, adó 8123456789.";

        var result = await service.RedactPiiAsync(input);

        Assert.Contains("Név: Béla", result);
        Assert.Contains("személyi ********", result);
        Assert.Contains("random 999", result);
        Assert.Contains("adó **********", result);
    }
}
using System;
using System.Threading.Tasks;
using Azure;
using Azure.AI.TextAnalytics;
using FunChatBotApp.Services;
using Microsoft.Extensions.Configuration;
using Moq;
using Xunit;

namespace FunChatBotApp.Tests.Services
{
    public class TextAnalyticsServiceTests
    {
        private class TestableTextAnalyticsService : TextAnalyticsService
        {
            private readonly TextAnalyticsClient? _mockClient;

            public TestableTextAnalyticsService(TextAnalyticsClient? client)
                : base(Mock.Of<IConfiguration>())
            {
                _mockClient = client;
                typeof(TextAnalyticsService)
                    .GetField("_client", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                    .SetValue(this, _mockClient);
            }
        }

        [Fact]
        public async Task RedactPiiAsync_NullOrWhiteSpaceInput_ReturnsSameText()
        {
            // Arrange
            var service = new TestableTextAnalyticsService(null);
            string input1 = null!;
            string input2 = "";
            string input3 = "   ";

            // Act
            var result1 = await service.RedactPiiAsync(input1);
            var result2 = await service.RedactPiiAsync(input2);
            var result3 = await service.RedactPiiAsync(input3);

            // Assert
            Assert.Null(result1);
            Assert.Equal("", result2);
            Assert.Equal("   ", result3);
        }

        [Fact]
        public async Task RedactPiiAsync_WithClient_MasksUsingAzureAndLocalRegex()
        {
            // Arrange
            var input = "594239CX 123 456 789 8123456789";
            var redactedByAzure = "REDACTED_BY_AZURE";

            var mockClient = new Mock<TextAnalyticsClient>();
            var piiEntities = new PiiEntityCollection(new PiiEntity[0], redactedByAzure, default);

            mockClient
                .Setup(c => c.RecognizePiiEntitiesAsync(It.IsAny<string>(), "hu", default, default))
                .ReturnsAsync(Response.FromValue(piiEntities, Mock.Of<Response>()));

            var service = new TestableTextAnalyticsService(mockClient.Object);

            // Act
            var result = await service.RedactPiiAsync(input);

            // Assert
            Assert.DoesNotContain("594239CX", result);
            Assert.DoesNotContain("123 456 789", result);
            Assert.DoesNotContain("8123456789", result);
            Assert.Contains("REDACTED_BY_AZURE", result);
            Assert.Contains("********", result);   // személyi igazolvány masking
            Assert.Contains("*********", result);  // TAJ szám masking
            Assert.Contains("**********", result); // adóazonosító masking
        }

        [Fact]
        public async Task RedactPiiAsync_WithClientThrows_ExceptionHandledAndLocalMaskingApplied()
        {
            // Arrange
            var input = "594239CX 123456789 8123456789";

            var mockClient = new Mock<TextAnalyticsClient>();
            mockClient
                .Setup(c => c.RecognizePiiEntitiesAsync(It.IsAny<string>(), "hu", default, default))
                .ThrowsAsync(new RequestFailedException("Azure error"));

            var service = new TestableTextAnalyticsService(mockClient.Object);

            // Act
            var result = await service.RedactPiiAsync(input);

            // Assert
            Assert.DoesNotContain("594239CX", result);
            Assert.DoesNotContain("123456789", result);
            Assert.DoesNotContain("8123456789", result);
            Assert.Contains("********", result);   // személyi igazolvány masking
            Assert.Contains("*********", result);  // TAJ szám masking
            Assert.Contains("**********", result); // adóazonosító masking
        }

        [Theory]
        [InlineData("594239CX", "********")]
        [InlineData("123 456 789", "*********")]
        [InlineData("123-456-789", "*********")]
        [InlineData("123456789", "*********")]
        [InlineData("8123456789", "**********")]
        [InlineData("8712345678", "**********")]
        public async Task RedactPiiAsync_LocalRegexMasksCorrectly(string input, string expectedMask)
        {
            // Arrange
            var service = new TestableTextAnalyticsService(null);

            // Act
            var result = await service.RedactPiiAsync(input);

            // Assert
            Assert.Equal(expectedMask, result);
        }

        [Fact]
        public async Task Constructor_WithNullOrEmptyConfig_NoClientCreated()
        {
            // Arrange
            var mockConfig = new Mock<IConfiguration>();
            mockConfig.Setup(c => c["AzureAILanguage:Endpoint"]).Returns<string?>(null);
            mockConfig.Setup(c => c["AzureAILanguage:ApiKey"]).Returns<string?>(null);

            // Act
            var service = new TextAnalyticsService(mockConfig.Object);

            // Act & Assert: no exception thrown and _client is null
            var field = typeof(TextAnalyticsService).GetField("_client", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var clientValue = field!.GetValue(service);
            Assert.Null(clientValue);

            var text = "Some string";
            var result = await service.RedactPiiAsync(text);
            Assert.Equal(text, result);
        }
    }
}
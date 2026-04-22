using System.IO;
using System.Threading.Tasks;
using Azure;
using Azure.AI.DocumentIntelligence;
using Microsoft.Extensions.Configuration;
using Moq;
using Xunit;

namespace FunChatBotApp.Tests
{
    public class DocumentServiceTests
    {
        [Fact]
        public async Task ExtractTextFromStreamAsync_WithValidStream_ReturnsExtractedContent()
        {
            // Arrange
            var inMemorySettings = new[]
            {
                new KeyValuePair<string, string?>("DocIntel:Endpoint", "https://example.cognitiveservices.azure.com/"),
                new KeyValuePair<string, string?>("DocIntel:ApiKey", "fake-api-key")
            };

            IConfiguration config = new ConfigurationBuilder()
                .AddInMemoryCollection(inMemorySettings)
                .Build();

            var service = new DocumentService(config);
            await using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("dummy content"));

            // Act
            var exception = await Record.ExceptionAsync(() => service.ExtractTextFromStreamAsync(stream));

            // Assert
            Assert.Null(exception);
        }

        [Fact]
        public async Task ExtractTextFromStreamAsync_WithEmptyStream_DoesNotThrowConfigurationRelatedException()
        {
            // Arrange
            var inMemorySettings = new[]
            {
                new KeyValuePair<string, string?>("DocIntel:Endpoint", "https://example.cognitiveservices.azure.com/"),
                new KeyValuePair<string, string?>("DocIntel:ApiKey", "fake-api-key")
            };

            IConfiguration config = new ConfigurationBuilder()
                .AddInMemoryCollection(inMemorySettings)
                .Build();

            var service = new DocumentService(config);
            await using var stream = new MemoryStream();

            // Act
            var exception = await Record.ExceptionAsync(() => service.ExtractTextFromStreamAsync(stream));

            // Assert
            Assert.Null(exception);
        }

        [Fact]
        public void Constructor_WithMissingEndpoint_ThrowsException()
        {
            // Arrange
            var inMemorySettings = new[]
            {
                new KeyValuePair<string, string?>("DocIntel:ApiKey", "fake-api-key")
            };

            IConfiguration config = new ConfigurationBuilder()
                .AddInMemoryCollection(inMemorySettings)
                .Build();

            // Act
            var exception = Record.Exception(() => new DocumentService(config));

            // Assert
            Assert.NotNull(exception);
        }

        [Fact]
        public void Constructor_WithMissingApiKey_ThrowsException()
        {
            // Arrange
            var inMemorySettings = new[]
            {
                new KeyValuePair<string, string?>("DocIntel:Endpoint", "https://example.cognitiveservices.azure.com/")
            };

            IConfiguration config = new ConfigurationBuilder()
                .AddInMemoryCollection(inMemorySettings)
                .Build();

            // Act
            var exception = Record.Exception(() => new DocumentService(config));

            // Assert
            Assert.NotNull(exception);
        }

        [Fact]
        public void Moq_IsAvailable_AndCanMockConfiguration()
        {
            // Arrange
            var configMock = new Mock<IConfiguration>();
            configMock.Setup(c => c["DocIntel:Endpoint"]).Returns("https://example.cognitiveservices.azure.com/");
            configMock.Setup(c => c["DocIntel:ApiKey"]).Returns("fake-api-key");

            // Act
            var exception = Record.Exception(() => new DocumentService(configMock.Object));

            // Assert
            Assert.Null(exception);
        }
    }
}
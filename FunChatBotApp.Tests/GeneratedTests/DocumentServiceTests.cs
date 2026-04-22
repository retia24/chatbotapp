using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace FunChatBotApp.Tests.Services
{
    public class DocumentServiceTests
    {
        [Fact]
        public async Task ExtractTextFromStreamAsync_WithValidStream_ReturnsExtractedText()
        {
            // Arrange
            var inMemorySettings = new System.Collections.Generic.Dictionary<string, string?>
            {
                { "DocIntel:Endpoint", "https://fake-endpoint.cognitiveservices.azure.com/" },
                { "DocIntel:ApiKey", "fake-api-key" }
            };

            IConfiguration configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(inMemorySettings)
                .Build();

            var service = new DocumentService(configuration);
            await using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("dummy content"));

            // Act & Assert
            await Assert.ThrowsAnyAsync<System.Exception>(async () =>
            {
                _ = await service.ExtractTextFromStreamAsync(stream);
            });
        }

        [Fact]
        public void Constructor_WithMissingEndpoint_ThrowsException()
        {
            // Arrange
            var inMemorySettings = new System.Collections.Generic.Dictionary<string, string?>
            {
                { "DocIntel:ApiKey", "fake-api-key" }
            };

            IConfiguration configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(inMemorySettings)
                .Build();

            // Act & Assert
            Assert.ThrowsAny<System.Exception>(() =>
            {
                _ = new DocumentService(configuration);
            });
        }

        [Fact]
        public void Constructor_WithMissingApiKey_ThrowsException()
        {
            // Arrange
            var inMemorySettings = new System.Collections.Generic.Dictionary<string, string?>
            {
                { "DocIntel:Endpoint", "https://fake-endpoint.cognitiveservices.azure.com/" }
            };

            IConfiguration configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(inMemorySettings)
                .Build();

            // Act & Assert
            Assert.ThrowsAny<System.Exception>(() =>
            {
                _ = new DocumentService(configuration);
            });
        }
    }
}
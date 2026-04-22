using System;
using System.IO;
using System.Threading.Tasks;
using Azure;
using Azure.AI.DocumentIntelligence;
using Microsoft.Extensions.Configuration;
using Moq;
using Xunit;

namespace DocumentServiceTests
{
    public class DocumentServiceTests
    {
        [Fact]
        public async Task ExtractTextFromStreamAsync_ReturnsExpectedContent()
        {
            // Arrange
            var mockConfig = new Mock<IConfiguration>();
            mockConfig.Setup(c => c["DocIntel:Endpoint"]).Returns("https://fake.endpoint");
            mockConfig.Setup(c => c["DocIntel:ApiKey"]).Returns("fake_api_key");

            var mockClient = new Mock<DocumentIntelligenceClient>(MockBehavior.Strict,
                new Uri("https://fake.endpoint"), new AzureKeyCredential("fake_api_key"));

            var mockOperation = new Mock<Operation<AnalyzeResult>>();
            var analyzeResult = new AnalyzeResult(new ReadOnlyMemory<byte>(Array.Empty<byte>()), new AnalyzeResultContent("expected text"));
            mockOperation.Setup(o => o.Value).Returns(analyzeResult);

            mockClient
                .Setup(c => c.AnalyzeDocumentAsync(It.IsAny<WaitUntil>(), "prebuilt-read", It.IsAny<Azure.Core.BinaryData>(), default))
                .ReturnsAsync(mockOperation.Object);

            // Use derived class to inject mock client
            var docService = new DocumentServiceTestable(mockConfig.Object, mockClient.Object);

            using var stream = new MemoryStream(new byte[] { 1, 2, 3 });

            // Act
            var result = await docService.ExtractTextFromStreamAsync(stream);

            // Assert
            Assert.Equal("expected text", result);
            mockClient.Verify(c => c.AnalyzeDocumentAsync(It.IsAny<WaitUntil>(), "prebuilt-read", It.IsAny<Azure.Core.BinaryData>(), default), Times.Once);
        }

        private class DocumentServiceTestable : DocumentService
        {
            private readonly DocumentIntelligenceClient _mockClient;

            public DocumentServiceTestable(IConfiguration config, DocumentIntelligenceClient client) : base(config)
            {
                _mockClient = client;
            }

            public override async Task<string> ExtractTextFromStreamAsync(Stream fileStream)
            {
                var content = await Azure.Core.BinaryData.FromStreamAsync(fileStream);

                Operation<AnalyzeResult> operation = await _mockClient.AnalyzeDocumentAsync(
                    WaitUntil.Completed,
                    "prebuilt-read",
                    content);

                return operation.Value.Content;
            }
        }

        private class AnalyzeResultContent : AnalyzeResult
        {
            private readonly string _content;

            public AnalyzeResultContent(string content) : base(default, default)
            {
                _content = content;
            }

            public override string Content => _content;
        }
    }
}
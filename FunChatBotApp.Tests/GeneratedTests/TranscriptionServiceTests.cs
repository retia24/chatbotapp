namespace FunChatBotApp.Tests.Services
{
    using FunChatBotApp.Models;
    using FunChatBotApp.Services;
    using Microsoft.Extensions.Configuration;
    using Moq;
    using Moq.Protected;
    using System.Net;
    using System.Text;
    using Xunit;

    public class TranscriptionServiceTests
    {
        [Fact]
        public async Task CheckStatusAsync_WhenDocAlreadyCompleted_ReturnsSameDoc_AndDoesNotCallHttpOrDb()
        {
            var cosmosMock = new Mock<CosmosDbService>();
            var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
            var httpClient = new HttpClient(handlerMock.Object);

            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["BlobStorage:ConnectionString"] = "UseDevelopmentStorage=true",
                    ["BlobStorage:ContainerName"] = "audio",
                    ["SpeechService:Endpoint"] = "https://speech.test",
                    ["SpeechService:ApiKey"] = "api-key"
                })
                .Build();

            var service = new TranscriptionService(cosmosMock.Object, config, httpClient);
            var doc = new AudioTranscriptionDocument
            {
                Status = TranscriptionState.Completed,
                ApiTranscriptionUri = "https://speech.test/transcriptions/1"
            };

            var result = await service.CheckStatusAsync(doc, "user-1");

            Assert.Same(doc, result);
            cosmosMock.Verify(x => x.UpsertTranscriptionAsync(It.IsAny<AudioTranscriptionDocument>(), It.IsAny<string>()), Times.Never);

            handlerMock.Protected().Verify(
                "SendAsync",
                Times.Never(),
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>());
        }

        [Fact]
        public async Task CheckStatusAsync_WhenDocFailed_ReturnsSameDoc_AndDoesNotCallHttpOrDb()
        {
            var cosmosMock = new Mock<CosmosDbService>();
            var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
            var httpClient = new HttpClient(handlerMock.Object);

            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["BlobStorage:ConnectionString"] = "UseDevelopmentStorage=true",
                    ["BlobStorage:ContainerName"] = "audio",
                    ["SpeechService:Endpoint"] = "https://speech.test",
                    ["SpeechService:ApiKey"] = "api-key"
                })
                .Build();

            var service = new TranscriptionService(cosmosMock.Object, config, httpClient);
            var doc = new AudioTranscriptionDocument
            {
                Status = TranscriptionState.Failed,
                ApiTranscriptionUri = "https://speech.test/transcriptions/1"
            };

            var result = await service.CheckStatusAsync(doc, "user-1");

            Assert.Same(doc, result);
            cosmosMock.Verify(x => x.UpsertTranscriptionAsync(It.IsAny<AudioTranscriptionDocument>(), It.IsAny<string>()), Times.Never);

            handlerMock.Protected().Verify(
                "SendAsync",
                Times.Never(),
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>());
        }

        [Fact]
        public async Task CheckStatusAsync_WhenApiUriMissing_ReturnsSameDoc_AndDoesNotCallHttpOrDb()
        {
            var cosmosMock = new Mock<CosmosDbService>();
            var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
            var httpClient = new HttpClient(handlerMock.Object);

            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["BlobStorage:ConnectionString"] = "UseDevelopmentStorage=true",
                    ["BlobStorage:ContainerName"] = "audio",
                    ["SpeechService:Endpoint"] = "https://speech.test",
                    ["SpeechService:ApiKey"] = "api-key"
                })
                .Build();

            var service = new TranscriptionService(cosmosMock.Object, config, httpClient);
            var doc = new AudioTranscriptionDocument
            {
                Status = TranscriptionState.Processing,
                ApiTranscriptionUri = ""
            };

            var result = await service.CheckStatusAsync(doc, "user-1");

            Assert.Same(doc, result);
            cosmosMock.Verify(x => x.UpsertTranscriptionAsync(It.IsAny<AudioTranscriptionDocument>(), It.IsAny<string>()), Times.Never);

            handlerMock.Protected().Verify(
                "SendAsync",
                Times.Never(),
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>());
        }

        [Fact]
        public async Task CheckStatusAsync_WhenStatusFailedFromApi_SetsDocFailed_AndUpserts()
        {
            var cosmosMock = new Mock<CosmosDbService>();

            var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
            handlerMock.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.Is<HttpRequestMessage>(m => m.Method == HttpMethod.Get && m.RequestUri!.ToString() == "https://speech.test/transcriptions/1"),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"status\":\"Failed\"}", Encoding.UTF8, "application/json")
                });

            var httpClient = new HttpClient(handlerMock.Object);

            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["BlobStorage:ConnectionString"] = "UseDevelopmentStorage=true",
                    ["BlobStorage:ContainerName"] = "audio",
                    ["SpeechService:Endpoint"] = "https://speech.test",
                    ["SpeechService:ApiKey"] = "api-key"
                })
                .Build();

            var service = new TranscriptionService(cosmosMock.Object, config, httpClient);
            var doc = new AudioTranscriptionDocument
            {
                Status = TranscriptionState.Processing,
                ApiTranscriptionUri = "https://speech.test/transcriptions/1"
            };

            var result = await service.CheckStatusAsync(doc, "user-1");

            Assert.Same(doc, result);
            Assert.Equal(TranscriptionState.Failed, doc.Status);
            cosmosMock.Verify(x => x.UpsertTranscriptionAsync(doc, "user-1"), Times.Once);

            handlerMock.Protected().Verify(
                "SendAsync",
                Times.Once(),
                ItExpr.Is<HttpRequestMessage>(m =>
                    m.Method == HttpMethod.Get &&
                    m.RequestUri!.ToString() == "https://speech.test/transcriptions/1" &&
                    m.Headers.Contains("Ocp-Apim-Subscription-Key")),
                ItExpr.IsAny<CancellationToken>());
        }
    }
}
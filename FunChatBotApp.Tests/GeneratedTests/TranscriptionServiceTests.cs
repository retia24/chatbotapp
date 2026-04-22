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
        public async Task CheckStatusAsync_WhenDocumentAlreadyCompleted_ReturnsSameDocumentWithoutHttpCall()
        {
            var cosmosMock = new Mock<CosmosDbService>();
            var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
            var httpClient = new HttpClient(handlerMock.Object);
            var config = CreateConfiguration();

            var service = new TranscriptionService(cosmosMock.Object, config, httpClient);

            var doc = new AudioTranscriptionDocument
            {
                Status = TranscriptionState.Completed,
                ApiTranscriptionUri = "https://example.com/transcriptions/1"
            };

            var result = await service.CheckStatusAsync(doc, "user1");

            Assert.Same(doc, result);
            Assert.Equal(TranscriptionState.Completed, result.Status);
            handlerMock.Protected().Verify(
                "SendAsync",
                Times.Never(),
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>());

            cosmosMock.Verify(x => x.UpsertTranscriptionAsync(It.IsAny<AudioTranscriptionDocument>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task CheckStatusAsync_WhenApiUriMissing_ReturnsSameDocumentWithoutHttpCall()
        {
            var cosmosMock = new Mock<CosmosDbService>();
            var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
            var httpClient = new HttpClient(handlerMock.Object);
            var config = CreateConfiguration();

            var service = new TranscriptionService(cosmosMock.Object, config, httpClient);

            var doc = new AudioTranscriptionDocument
            {
                Status = TranscriptionState.Processing,
                ApiTranscriptionUri = string.Empty
            };

            var result = await service.CheckStatusAsync(doc, "user1");

            Assert.Same(doc, result);
            Assert.Equal(TranscriptionState.Processing, result.Status);
            handlerMock.Protected().Verify(
                "SendAsync",
                Times.Never(),
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>());

            cosmosMock.Verify(x => x.UpsertTranscriptionAsync(It.IsAny<AudioTranscriptionDocument>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task CheckStatusAsync_WhenSpeechStatusFailed_SetsFailedAndUpserts()
        {
            var cosmosMock = new Mock<CosmosDbService>();

            var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
            handlerMock.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.Is<HttpRequestMessage>(m => m.Method == HttpMethod.Get && m.RequestUri!.ToString() == "https://speech/status/1"),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"status\":\"Failed\"}", Encoding.UTF8, "application/json")
                });

            var httpClient = new HttpClient(handlerMock.Object);
            var config = CreateConfiguration();

            var service = new TranscriptionService(cosmosMock.Object, config, httpClient);

            var doc = new AudioTranscriptionDocument
            {
                Status = TranscriptionState.Processing,
                ApiTranscriptionUri = "https://speech/status/1"
            };

            var result = await service.CheckStatusAsync(doc, "user1");

            Assert.Equal(TranscriptionState.Failed, result.Status);
            cosmosMock.Verify(x => x.UpsertTranscriptionAsync(doc, "user1"), Times.Once);
        }

        [Fact]
        public async Task CheckStatusAsync_WhenSucceededAndTranscriptDownloaded_SetsCompletedAndTranscriptText()
        {
            var cosmosMock = new Mock<CosmosDbService>();

            var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);

            handlerMock.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.Is<HttpRequestMessage>(m => m.Method == HttpMethod.Get && m.RequestUri!.ToString() == "https://speech/status/2"),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"status\":\"Succeeded\",\"links\":{\"files\":\"https://speech/files/2\"}}", Encoding.UTF8, "application/json")
                });

            handlerMock.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.Is<HttpRequestMessage>(m => m.Method == HttpMethod.Get && m.RequestUri!.ToString() == "https://speech/files/2"),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"values\":[{\"kind\":\"Transcription\",\"links\":{\"contentUrl\":\"https://speech/content/2\"}}]}", Encoding.UTF8, "application/json")
                });

            handlerMock.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.Is<HttpRequestMessage>(m => m.Method == HttpMethod.Get && m.RequestUri!.ToString() == "https://speech/content/2"),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"combinedRecognizedPhrases\":[{\"display\":\"Ez egy teszt leirat\"}]}", Encoding.UTF8, "application/json")
                });

            var httpClient = new HttpClient(handlerMock.Object);
            var config = CreateConfiguration();

            var service = new TranscriptionService(cosmosMock.Object, config, httpClient);

            var doc = new AudioTranscriptionDocument
            {
                Status = TranscriptionState.Processing,
                ApiTranscriptionUri = "https://speech/status/2"
            };

            var result = await service.CheckStatusAsync(doc, "user1");

            Assert.Equal(TranscriptionState.Completed, result.Status);
            Assert.Equal("Ez egy teszt leirat", result.TranscriptText);
            cosmosMock.Verify(x => x.UpsertTranscriptionAsync(doc, "user1"), Times.Once);
        }

        [Fact]
        public async Task CheckStatusAsync_WhenSucceededButResultInvalid_SetsFailedWithExpectedErrorMessage()
        {
            var cosmosMock = new Mock<CosmosDbService>();

            var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);

            handlerMock.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.Is<HttpRequestMessage>(m => m.Method == HttpMethod.Get && m.RequestUri!.ToString() == "https://speech/status/3"),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"status\":\"Succeeded\",\"links\":{\"files\":\"https://speech/files/3\"}}", Encoding.UTF8, "application/json")
                });

            handlerMock.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.Is<HttpRequestMessage>(m => m.Method == HttpMethod.Get && m.RequestUri!.ToString() == "https://speech/files/3"),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"values\":[{\"kind\":\"Other\",\"links\":{\"contentUrl\":\"https://speech/content/3\"}}]}", Encoding.UTF8, "application/json")
                });

            var httpClient = new HttpClient(handlerMock.Object);
            var config = CreateConfiguration();

            var service = new TranscriptionService(cosmosMock.Object, config, httpClient);

            var doc = new AudioTranscriptionDocument
            {
                Status = TranscriptionState.Processing,
                ApiTranscriptionUri = "https://speech/status/3"
            };

            var result = await service.CheckStatusAsync(doc, "user1");

            Assert.Equal(TranscriptionState.Failed, result.Status);
            Assert.Equal("Sikeres állapot, de hiba történt a leirat fájl letöltésekor vagy feldolgozásakor.", result.ErrorMessage);
            cosmosMock.Verify(x => x.UpsertTranscriptionAsync(doc, "user1"), Times.Once);
        }

        private static IConfiguration CreateConfiguration()
        {
            var values = new Dictionary<string, string?>
            {
                ["BlobStorage:ConnectionString"] = "UseDevelopmentStorage=true",
                ["BlobStorage:ContainerName"] = "test",
                ["SpeechService:Endpoint"] = "https://speech",
                ["SpeechService:ApiKey"] = "key"
            };

            return new ConfigurationBuilder()
                .AddInMemoryCollection(values)
                .Build();
        }
    }
}
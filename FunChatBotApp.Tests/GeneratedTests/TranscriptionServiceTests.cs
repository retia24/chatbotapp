using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FunChatBotApp.Models;
using FunChatBotApp.Services;
using Microsoft.Extensions.Configuration;
using Moq;
using Moq.Protected;
using Xunit;

namespace FunChatBotApp.Tests
{
    public class TranscriptionServiceTests
    {
        private readonly Mock<CosmosDbService> _cosmosDbMock;
        private readonly Mock<IConfiguration> _configMock;
        private readonly Mock<HttpMessageHandler> _httpHandlerMock;
        private readonly HttpClient _httpClient;
        private readonly TranscriptionService _service;

        private const string BlobConnectionString = "UseDevelopmentStorage=true";
        private const string BlobContainer = "test-container";
        private const string SpeechEndpoint = "https://fake.speech.endpoint";
        private const string SpeechKey = "fake-key";

        public TranscriptionServiceTests()
        {
            _cosmosDbMock = new Mock<CosmosDbService>();
            _configMock = new Mock<IConfiguration>();
            _configMock.Setup(c => c["BlobStorage:ConnectionString"]).Returns(BlobConnectionString);
            _configMock.Setup(c => c["BlobStorage:ContainerName"]).Returns(BlobContainer);
            _configMock.Setup(c => c["SpeechService:Endpoint"]).Returns(SpeechEndpoint);
            _configMock.Setup(c => c["SpeechService:ApiKey"]).Returns(SpeechKey);

            _httpHandlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
            _httpClient = new HttpClient(_httpHandlerMock.Object);

            _service = new TranscriptionService(_cosmosDbMock.Object, _configMock.Object, _httpClient);
        }

        [Fact]
        public async Task StartTranscriptionAsync_SuccessfulFlow_UpdatesStatusToProcessing()
        {
            // Arrange
            var fileStream = new MemoryStream(new byte[] { 1, 2, 3 });
            string fileName = "audio.wav";
            string userId = "user123";

            _httpHandlerMock.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.Is<HttpRequestMessage>(req =>
                        req.Method == HttpMethod.Post &&
                        req.RequestUri?.AbsoluteUri == $"{SpeechEndpoint}/transcriptions"),
                    ItExpr.IsAny<CancellationToken>()
                )
                .ReturnsAsync(new HttpResponseMessage(
                    HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"self\":\"https://fake.api.transcriptions/1\"}")
                })
                .Verifiable();

            _cosmosDbMock.Setup(db => db.UpsertTranscriptionAsync(It.IsAny<AudioTranscriptionDocument>(), userId))
                .Returns(Task.CompletedTask)
                .Verifiable();

            // Act
            var result = await _service.StartTranscriptionAsync(fileStream, fileName, userId);

            // Assert
            _httpHandlerMock.Protected().Verify(
                "SendAsync",
                Times.Once(),
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.Method == HttpMethod.Post &&
                    req.RequestUri?.AbsoluteUri == $"{SpeechEndpoint}/transcriptions"),
                ItExpr.IsAny<CancellationToken>());

            _cosmosDbMock.Verify(db => db.UpsertTranscriptionAsync(It.Is<AudioTranscriptionDocument>(d =>
                d.Status == TranscriptionState.Uploading ||
                d.Status == TranscriptionState.Processing), userId), Times.AtLeast(2));

            Assert.NotNull(result);
            Assert.Equal(fileName, result.OriginalFileName);
            Assert.NotNull(result.BlobUrl);
            Assert.Equal(TranscriptionState.Processing, result.Status);
            Assert.Equal("https://fake.api.transcriptions/1", result.ApiTranscriptionUri);
        }

        [Fact]
        public async Task StartTranscriptionAsync_FailedResponse_UpdatesStatusToFailed()
        {
            // Arrange
            var fileStream = new MemoryStream(new byte[] { 1, 2, 3 });
            string fileName = "audio.wav";
            string userId = "user123";

            var errorMessage = "Error from speech API";

            _httpHandlerMock.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>()
                )
                .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.BadRequest)
                {
                    Content = new StringContent(errorMessage)
                })
                .Verifiable();

            _cosmosDbMock.Setup(db => db.UpsertTranscriptionAsync(It.IsAny<AudioTranscriptionDocument>(), userId))
                .Returns(Task.CompletedTask)
                .Verifiable();

            // Act
            var result = await _service.StartTranscriptionAsync(fileStream, fileName, userId);

            // Assert
            _cosmosDbMock.Verify(db => db.UpsertTranscriptionAsync(It.Is<AudioTranscriptionDocument>(d =>
                d.Status == TranscriptionState.Failed && d.ErrorMessage == errorMessage), userId), Times.AtLeast(2));

            Assert.Equal(TranscriptionState.Failed, result.Status);
            Assert.Equal(errorMessage, result.ErrorMessage);
        }

        [Fact]
        public async Task CheckStatusAsync_CompletedOrFailedStatus_ReturnsDocumentWithoutHttpCall()
        {
            // Arrange
            var docCompleted = new AudioTranscriptionDocument { Status = TranscriptionState.Completed };
            var docFailed = new AudioTranscriptionDocument { Status = TranscriptionState.Failed };

            // Act
            var resultCompleted = await _service.CheckStatusAsync(docCompleted, "user1");
            var resultFailed = await _service.CheckStatusAsync(docFailed, "user1");

            // Assert
            _httpHandlerMock.Protected().Verify("SendAsync", Times.Never(), ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>());
            Assert.Same(docCompleted, resultCompleted);
            Assert.Same(docFailed, resultFailed);
        }

        [Fact]
        public async Task CheckStatusAsync_EmptyApiUri_ReturnsDocumentWithoutHttpCall()
        {
            // Arrange
            var doc = new AudioTranscriptionDocument { Status = TranscriptionState.Processing, ApiTranscriptionUri = "" };

            // Act
            var result = await _service.CheckStatusAsync(doc, "user1");

            // Assert
            _httpHandlerMock.Protected().Verify("SendAsync", Times.Never(), ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>());
            Assert.Same(doc, result);
        }

        [Fact]
        public async Task CheckStatusAsync_SucceededStatus_DownloadsTranscriptAndUpdatesDocument()
        {
            // Arrange
            var doc = new AudioTranscriptionDocument
            {
                Status = TranscriptionState.Processing,
                ApiTranscriptionUri = "https://fake.api/transcription/1"
            };
            string userId = "user1";

            var transcriptionStatusResponse = "{\"status\":\"Succeeded\",\"links\":{\"files\":\"https://fake.api/files/1\"}}";
            var filesResponse = "{\"values\":[{\"kind\":\"Transcription\",\"links\":{\"contentUrl\":\"https://fake.api/content/1\"}}]}";
            var contentResponse = "{\"combinedRecognizedPhrases\":[{\"display\":\"This is the transcript text.\"}]}";

            var sequence = new MockSequence();

            _httpHandlerMock.Protected()
                .InSequence(sequence)
                .Setup<Task<HttpResponseMessage>>("SendAsync",
                    ItExpr.Is<HttpRequestMessage>(req => req.Method == HttpMethod.Get && req.RequestUri?.AbsoluteUri == doc.ApiTranscriptionUri),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(transcriptionStatusResponse)
                });

            _httpHandlerMock.Protected()
                .InSequence(sequence)
                .Setup<Task<HttpResponseMessage>>("SendAsync",
                    ItExpr.Is<HttpRequestMessage>(req => req.Method == HttpMethod.Get && req.RequestUri?.AbsoluteUri == "https://fake.api/files/1"),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(filesResponse)
                });

            _httpHandlerMock.Protected()
                .InSequence(sequence)
                .Setup<Task<HttpResponseMessage>>("SendAsync",
                    ItExpr.Is<HttpRequestMessage>(req => req.Method == HttpMethod.Get && req.RequestUri?.AbsoluteUri == "https://fake.api/content/1"),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(contentResponse)
                });

            _cosmosDbMock.Setup(db => db.UpsertTranscriptionAsync(It.IsAny<AudioTranscriptionDocument>(), userId))
                .Returns(Task.CompletedTask)
                .Verifiable();

            // Act
            var result = await _service.CheckStatusAsync(doc, userId);

            // Assert
            _cosmosDbMock.Verify(db => db.UpsertTranscriptionAsync(It.Is<AudioTranscriptionDocument>(d =>
                d.Status == TranscriptionState.Completed &&
                d.TranscriptText == "This is the transcript text."), userId), Times.Once);

            Assert.Equal(TranscriptionState.Completed, result.Status);
            Assert.Equal("This is the transcript text.", result.TranscriptText);
        }

        [Fact]
        public async Task CheckStatusAsync_FailedStatus_UpdatesStatusToFailed()
        {
            // Arrange
            var doc = new AudioTranscriptionDocument
            {
                Status = TranscriptionState.Processing,
                ApiTranscriptionUri = "https://fake.api/transcription/1"
            };
            string userId = "user1";

            var transcriptionStatusResponse = "{\"status\":\"Failed\"}";

            _httpHandlerMock.Protected()
                .Setup<Task<HttpResponseMessage>>("SendAsync",
                    ItExpr.Is<HttpRequestMessage>(req => req.Method == HttpMethod.Get && req.RequestUri?.AbsoluteUri == doc.ApiTranscriptionUri),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(transcriptionStatusResponse)
                })
                .Verifiable();

            _cosmosDbMock.Setup(db => db.UpsertTranscriptionAsync(It.IsAny<AudioTranscriptionDocument>(), userId))
                .Returns(Task.CompletedTask)
                .Verifiable();

            // Act
            var result = await _service.CheckStatusAsync(doc, userId);

            // Assert
            _cosmosDbMock.Verify(db => db.UpsertTranscriptionAsync(It.Is<AudioTranscriptionDocument>(d =>
                d.Status == TranscriptionState.Failed), userId), Times.Once);

            Assert.Equal(TranscriptionState.Failed, result.Status);
        }
    }
}
namespace FunChatBotApp.Tests.Services
{
    using System;
    using System.Reflection;
    using System.Threading;
    using System.Threading.Tasks;
    using FunChatBotApp.Models;
    using FunChatBotApp.Services;
    using Microsoft.Azure.Cosmos;
    using Moq;
    using Xunit;

    public class CosmosDbServiceTests
    {
        private static CosmosDbService CreateServiceWithMockedContainer(Mock<Container> containerMock)
        {
            var configMock = new Mock<Microsoft.Extensions.Configuration.IConfiguration>();
            configMock.Setup(c => c["CosmosDb:EndpointUri"]).Returns("https://localhost:8081");
            configMock.Setup(c => c["CosmosDbConnection"]).Returns("fake-key");
            configMock.Setup(c => c["CosmosDb:DatabaseName"]).Returns("db");
            configMock.Setup(c => c["CosmosDb:ContainerName"]).Returns("container");

            var service = new CosmosDbService(configMock.Object);

            var field = typeof(CosmosDbService).GetField("_container", BindingFlags.Instance | BindingFlags.NonPublic);
            field!.SetValue(service, containerMock.Object);

            return service;
        }

        [Fact]
        public async Task UpsertProjectAsync_SetsRequiredFields_AndCallsUpsert()
        {
            var containerMock = new Mock<Container>();
            containerMock
                .Setup(c => c.UpsertItemAsync(
                    It.IsAny<Project>(),
                    It.IsAny<PartitionKey?>(),
                    It.IsAny<ItemRequestOptions>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((ItemResponse<Project>)null!);

            var sut = CreateServiceWithMockedContainer(containerMock);
            var project = new Project { Id = "p1", ProjectId = "" };
            var userId = "user-1";

            await sut.UpsertProjectAsync(project, userId);

            Assert.Equal("Project", project.Type);
            Assert.Equal(userId, project.UserId);
            Assert.Equal("p1", project.ProjectId);

            containerMock.Verify(c => c.UpsertItemAsync(
                project,
                It.Is<PartitionKey?>(pk => pk.HasValue),
                null,
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task UpsertUserProfileAsync_SetsIdTypeAndUserId_AndCallsUpsert()
        {
            var containerMock = new Mock<Container>();
            containerMock
                .Setup(c => c.UpsertItemAsync(
                    It.IsAny<UserProfile>(),
                    It.IsAny<PartitionKey?>(),
                    It.IsAny<ItemRequestOptions>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((ItemResponse<UserProfile>)null!);

            var sut = CreateServiceWithMockedContainer(containerMock);
            var profile = new UserProfile { Id = "old-id", UserId = "old-user", Summary = "sum" };
            var userId = "user-42";

            await sut.UpsertUserProfileAsync(profile, userId);

            Assert.Equal("UserProfile", profile.Type);
            Assert.Equal(userId, profile.UserId);
            Assert.Equal(userId, profile.Id);

            containerMock.Verify(c => c.UpsertItemAsync(
                profile,
                It.Is<PartitionKey?>(pk => pk.HasValue),
                null,
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task UpsertMessageAsync_SetsTypeAndUserId_AndCallsUpsert()
        {
            var containerMock = new Mock<Container>();
            containerMock
                .Setup(c => c.UpsertItemAsync(
                    It.IsAny<ChatMessage>(),
                    It.IsAny<PartitionKey?>(),
                    It.IsAny<ItemRequestOptions>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((ItemResponse<ChatMessage>)null!);

            var sut = CreateServiceWithMockedContainer(containerMock);
            var message = new ChatMessage { Content = "hello", Role = "user", ChatId = "chat-1", ProjectId = "proj-1" };
            var userId = "user-a";

            await sut.UpsertMessageAsync(message, userId);

            Assert.Equal("Message", message.Type);
            Assert.Equal(userId, message.UserId);

            containerMock.Verify(c => c.UpsertItemAsync(
                message,
                It.Is<PartitionKey?>(pk => pk.HasValue),
                null,
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task UpsertTranscriptionAsync_SetsTypeUserIdAndProjectId_WhenProjectIdEmpty()
        {
            var containerMock = new Mock<Container>();
            containerMock
                .Setup(c => c.UpsertItemAsync(
                    It.IsAny<AudioTranscriptionDocument>(),
                    It.IsAny<PartitionKey?>(),
                    It.IsAny<ItemRequestOptions>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((ItemResponse<AudioTranscriptionDocument>)null!);

            var sut = CreateServiceWithMockedContainer(containerMock);
            var doc = new AudioTranscriptionDocument { Id = "tr-1", ProjectId = "", OriginalFileName = "a.wav" };
            var userId = "user-z";

            await sut.UpsertTranscriptionAsync(doc, userId);

            Assert.Equal("AudioTranscription", doc.Type);
            Assert.Equal(userId, doc.UserId);
            Assert.Equal("tr-1", doc.ProjectId);

            containerMock.Verify(c => c.UpsertItemAsync(
                doc,
                It.Is<PartitionKey?>(pk => pk.HasValue),
                null,
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task DeleteProjectAsync_CallsDeleteWithCorrectId()
        {
            var containerMock = new Mock<Container>();
            containerMock
                .Setup(c => c.DeleteItemAsync<Project>(
                    It.IsAny<string>(),
                    It.IsAny<PartitionKey>(),
                    It.IsAny<ItemRequestOptions>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((ItemResponse<Project>)null!);

            var sut = CreateServiceWithMockedContainer(containerMock);

            await sut.DeleteProjectAsync("project-123", "user-123");

            containerMock.Verify(c => c.DeleteItemAsync<Project>(
                "project-123",
                It.IsAny<PartitionKey>(),
                null,
                It.IsAny<CancellationToken>()), Times.Once);
        }
    }
}
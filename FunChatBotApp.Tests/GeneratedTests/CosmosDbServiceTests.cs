namespace FunChatBotApp.Tests.Services
{
    using System;
    using System.Linq;
    using System.Reflection;
    using System.Threading;
    using System.Threading.Tasks;
    using FunChatBotApp.Models;
    using FunChatBotApp.Services;
    using Microsoft.Azure.Cosmos;
    using Microsoft.Extensions.Configuration;
    using Moq;
    using Xunit;

    public class CosmosDbServiceTests
    {
        private static CosmosDbService CreateServiceWithContainer(Mock<Container> containerMock)
        {
            var configMock = new Mock<IConfiguration>();
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
                .ReturnsAsync(Mock.Of<ItemResponse<Project>>());

            var service = CreateServiceWithContainer(containerMock);
            var project = new Project { Id = "p1", ProjectId = "" };
            var userId = "u1";

            await service.UpsertProjectAsync(project, userId);

            Assert.Equal("Project", project.Type);
            Assert.Equal(userId, project.UserId);
            Assert.Equal("p1", project.ProjectId);

            containerMock.Verify(c => c.UpsertItemAsync(
                It.Is<Project>(p => p == project),
                It.Is<PartitionKey?>(pk => pk.HasValue),
                It.IsAny<ItemRequestOptions>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task GetProjectAsync_WhenNotFound_ReturnsNull()
        {
            var containerMock = new Mock<Container>();
            containerMock
                .Setup(c => c.ReadItemAsync<Project>(
                    It.IsAny<string>(),
                    It.IsAny<PartitionKey>(),
                    It.IsAny<ItemRequestOptions>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new CosmosException("not found", System.Net.HttpStatusCode.NotFound, 0, string.Empty, 0));

            var service = CreateServiceWithContainer(containerMock);

            var result = await service.GetProjectAsync("missing", "u1");

            Assert.Null(result);
        }

        [Fact]
        public async Task UpsertStandaloneChatAsync_SetsTypeUserAndProjectId_WhenProjectIdEmpty()
        {
            var containerMock = new Mock<Container>();
            containerMock
                .Setup(c => c.UpsertItemAsync(
                    It.IsAny<ChatSession>(),
                    It.IsAny<PartitionKey?>(),
                    It.IsAny<ItemRequestOptions>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(Mock.Of<ItemResponse<ChatSession>>());

            var service = CreateServiceWithContainer(containerMock);
            var chat = new ChatSession { Id = "c1", ProjectId = "" };

            await service.UpsertStandaloneChatAsync(chat, "u1");

            Assert.Equal("StandaloneChat", chat.Type);
            Assert.Equal("u1", chat.UserId);
            Assert.Equal("c1", chat.ProjectId);
        }

        [Fact]
        public async Task UpsertMessageAsync_SetsTypeAndUserId()
        {
            var containerMock = new Mock<Container>();
            containerMock
                .Setup(c => c.UpsertItemAsync(
                    It.IsAny<ChatMessage>(),
                    It.IsAny<PartitionKey?>(),
                    It.IsAny<ItemRequestOptions>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(Mock.Of<ItemResponse<ChatMessage>>());

            var service = CreateServiceWithContainer(containerMock);
            var message = new ChatMessage { Content = "hello" };

            await service.UpsertMessageAsync(message, "u1");

            Assert.Equal("Message", message.Type);
            Assert.Equal("u1", message.UserId);
        }

        [Fact]
        public async Task UpsertUserProfileAsync_SetsTypeUserAndIdToUserId()
        {
            var containerMock = new Mock<Container>();
            containerMock
                .Setup(c => c.UpsertItemAsync(
                    It.IsAny<UserProfile>(),
                    It.IsAny<PartitionKey?>(),
                    It.IsAny<ItemRequestOptions>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(Mock.Of<ItemResponse<UserProfile>>());

            var service = CreateServiceWithContainer(containerMock);
            var profile = new UserProfile { Id = "old-id" };

            await service.UpsertUserProfileAsync(profile, "u1");

            Assert.Equal("UserProfile", profile.Type);
            Assert.Equal("u1", profile.UserId);
            Assert.Equal("u1", profile.Id);
        }

        [Fact]
        public async Task UpsertTranscriptionAsync_SetsTypeUserAndProjectId_WhenMissing()
        {
            var containerMock = new Mock<Container>();
            containerMock
                .Setup(c => c.UpsertItemAsync(
                    It.IsAny<AudioTranscriptionDocument>(),
                    It.IsAny<PartitionKey?>(),
                    It.IsAny<ItemRequestOptions>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(Mock.Of<ItemResponse<AudioTranscriptionDocument>>());

            var service = CreateServiceWithContainer(containerMock);
            var doc = new AudioTranscriptionDocument { Id = "t1", ProjectId = "" };

            await service.UpsertTranscriptionAsync(doc, "u1");

            Assert.Equal("AudioTranscription", doc.Type);
            Assert.Equal("u1", doc.UserId);
            Assert.Equal("t1", doc.ProjectId);
        }
    }
}
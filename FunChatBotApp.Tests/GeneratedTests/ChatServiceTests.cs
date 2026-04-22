namespace FunChatBotApp.Tests.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using FunChatBotApp.Models;
    using FunChatBotApp.Services;
    using Microsoft.Extensions.Configuration;
    using Microsoft.SemanticKernel;
    using Moq;
    using Xunit;

    public class ChatServiceTests
    {
        [Fact]
        public async Task ProcessStandaloneMessageAsync_ShouldPersistRedactedUserAndAssistantMessages_AndReturnRedactedAssistantResponse()
        {
            var inMemoryConfig = new Dictionary<string, string?>
            {
                ["OpenAI:Endpoint"] = "https://example.openai.azure.com/",
                ["OpenAI:ApiKey"] = "fake-key",
                ["OpenAI:DeploymentName"] = "fake-deployment"
            };
            IConfiguration config = new ConfigurationBuilder().AddInMemoryCollection(inMemoryConfig).Build();

            var cosmosMock = new Mock<CosmosDbService>();
            var kernel = Kernel.CreateBuilder().Build();
            var themeServiceMock = new Mock<ThemeService>();
            var textAnalyticsMock = new Mock<TextAnalyticsService>();

            textAnalyticsMock
                .Setup(x => x.RedactPiiAsync("My email is test@example.com"))
                .ReturnsAsync("My email is [REDACTED]");

            textAnalyticsMock
                .Setup(x => x.RedactPiiAsync(It.IsAny<string>()))
                .ReturnsAsync((string s) => s);

            var sut = new ChatService(
                config,
                cosmosMock.Object,
                kernel,
                themeServiceMock.Object,
                textAnalyticsMock.Object);

            var chat = new ChatSession
            {
                Id = Guid.NewGuid().ToString(),
                ProjectId = string.Empty,
                UserId = "user-1",
                Title = "Standalone"
            };

            var currentMessages = new List<ChatMessage>();

            var response = await sut.ProcessStandaloneMessageAsync(
                chat,
                currentMessages,
                "My email is test@example.com",
                "user-1");

            Assert.NotNull(response);
            Assert.Equal(2, currentMessages.Count);
            Assert.Equal("user", currentMessages[0].Role);
            Assert.Equal("My email is [REDACTED]", currentMessages[0].Content);
            Assert.Equal("assistant", currentMessages[1].Role);

            cosmosMock.Verify(
                x => x.UpsertMessageAsync(It.Is<ChatMessage>(m =>
                    m.Role == "user" &&
                    m.ChatId == chat.Id &&
                    m.UserId == "user-1" &&
                    m.Content == "My email is [REDACTED]"), "user-1"),
                Times.Once);

            cosmosMock.Verify(
                x => x.UpsertMessageAsync(It.Is<ChatMessage>(m =>
                    m.Role == "assistant" &&
                    m.ChatId == chat.Id &&
                    m.UserId == "user-1"), "user-1"),
                Times.Once);
        }

        [Fact]
        public async Task MoveChatToProjectAsync_ShouldUpdateChat_AndMoveAllMessagesToTargetProject()
        {
            var inMemoryConfig = new Dictionary<string, string?>
            {
                ["OpenAI:Endpoint"] = "https://example.openai.azure.com/",
                ["OpenAI:ApiKey"] = "fake-key",
                ["OpenAI:DeploymentName"] = "fake-deployment"
            };
            IConfiguration config = new ConfigurationBuilder().AddInMemoryCollection(inMemoryConfig).Build();

            var originalProjectId = "old-project";
            var targetProjectId = "new-project";
            var chatId = "chat-1";
            var userId = "user-42";

            var movedMessages = new List<ChatMessage>
            {
                new ChatMessage { ChatId = chatId, ProjectId = originalProjectId, UserId = userId, Role = "user", Content = "Hello" },
                new ChatMessage { ChatId = chatId, ProjectId = originalProjectId, UserId = userId, Role = "assistant", Content = "Hi there" }
            };

            var cosmosMock = new Mock<CosmosDbService>();
            cosmosMock
                .Setup(x => x.GetChatMessagesAsync(originalProjectId, chatId, userId))
                .ReturnsAsync(movedMessages);

            var kernel = Kernel.CreateBuilder().Build();
            var themeServiceMock = new Mock<ThemeService>();
            var textAnalyticsMock = new Mock<TextAnalyticsService>();
            textAnalyticsMock.Setup(x => x.RedactPiiAsync(It.IsAny<string>())).ReturnsAsync((string s) => s);

            var sut = new ChatService(
                config,
                cosmosMock.Object,
                kernel,
                themeServiceMock.Object,
                textAnalyticsMock.Object);

            var chat = new ChatSession
            {
                Id = chatId,
                ProjectId = originalProjectId,
                UserId = userId,
                Title = "Standalone"
            };

            var targetProject = new Project
            {
                Id = targetProjectId,
                UserId = userId,
                Name = "Target"
            };

            await sut.MoveChatToProjectAsync(chat, targetProject, userId);

            Assert.Equal(targetProjectId, chat.ProjectId);

            cosmosMock.Verify(
                x => x.UpsertProjectChatAsync(It.Is<ChatSession>(c =>
                    c.Id == chatId &&
                    c.ProjectId == targetProjectId), targetProjectId, userId),
                Times.Once);

            cosmosMock.Verify(
                x => x.UpsertMessageAsync(It.Is<ChatMessage>(m =>
                    m.ChatId == chatId &&
                    m.ProjectId == targetProjectId), userId),
                Times.Exactly(2));
        }
    }
}
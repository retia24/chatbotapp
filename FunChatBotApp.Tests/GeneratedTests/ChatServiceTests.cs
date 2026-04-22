namespace FunChatBotApp.Tests.Services
{
    using System;
    using System.Collections.Generic;
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
        public async Task ProcessStandaloneMessageAsync_RedactsUserAndAssistantMessages_AndPersistsBoth()
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["OpenAI:Endpoint"] = "https://example.openai.azure.com/",
                    ["OpenAI:ApiKey"] = "fake-key",
                    ["OpenAI:DeploymentName"] = "gpt-test"
                })
                .Build();

            var cosmosDbMock = new Mock<CosmosDbService>();
            cosmosDbMock.Setup(x => x.UpsertMessageAsync(It.IsAny<ChatMessage>(), It.IsAny<string>()))
                .Returns(Task.CompletedTask);

            var themeServiceMock = new Mock<ThemeService>();
            var textAnalyticsMock = new Mock<TextAnalyticsService>();
            textAnalyticsMock.Setup(x => x.RedactPiiAsync("raw user text")).ReturnsAsync("safe user text");
            textAnalyticsMock.Setup(x => x.RedactPiiAsync(It.IsAny<string>())).ReturnsAsync((string s) => s);

            var kernel = new Kernel();

            var sut = new ChatService(config, cosmosDbMock.Object, kernel, themeServiceMock.Object, textAnalyticsMock.Object);

            var chat = new ChatSession
            {
                Id = "chat-1",
                ProjectId = "proj-1",
                UserId = "user-1"
            };
            var currentMessages = new List<ChatMessage>();

            await Assert.ThrowsAnyAsync<Exception>(() =>
                sut.ProcessStandaloneMessageAsync(chat, currentMessages, "raw user text", "user-1"));

            textAnalyticsMock.Verify(x => x.RedactPiiAsync("raw user text"), Times.Once);
            cosmosDbMock.Verify(x => x.UpsertMessageAsync(It.Is<ChatMessage>(m =>
                m.ChatId == "chat-1" &&
                m.ProjectId == "proj-1" &&
                m.UserId == "user-1" &&
                m.Role == "user" &&
                m.Content == "safe user text"), "user-1"), Times.Once);
        }

        [Fact]
        public async Task GenerateInitialRecommendationsAsync_OnFailureFallback_PersistsAssistantMessageWithTopics()
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["OpenAI:Endpoint"] = "https://example.openai.azure.com/",
                    ["OpenAI:ApiKey"] = "fake-key",
                    ["OpenAI:DeploymentName"] = "gpt-test"
                })
                .Build();

            var cosmosDbMock = new Mock<CosmosDbService>();
            cosmosDbMock.Setup(x => x.UpsertMessageAsync(It.IsAny<ChatMessage>(), It.IsAny<string>()))
                .Returns(Task.CompletedTask);

            var themeServiceMock = new Mock<ThemeService>();
            var textAnalyticsMock = new Mock<TextAnalyticsService>();
            var kernel = new Kernel();

            var sut = new ChatService(config, cosmosDbMock.Object, kernel, themeServiceMock.Object, textAnalyticsMock.Object);

            var chat = new ChatSession
            {
                Id = "chat-2",
                ProjectId = "proj-2",
                UserId = "user-2"
            };

            await Assert.ThrowsAnyAsync<Exception>(() => sut.GenerateInitialRecommendationsAsync(chat, "user-2"));

            cosmosDbMock.Verify(x => x.UpsertMessageAsync(It.IsAny<ChatMessage>(), "user-2"), Times.Never);
        }

        [Fact]
        public async Task MoveChatToProjectAsync_UpdatesChatAndMessagesAndCallsSummaryPath()
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["OpenAI:Endpoint"] = "https://example.openai.azure.com/",
                    ["OpenAI:ApiKey"] = "fake-key",
                    ["OpenAI:DeploymentName"] = "gpt-test"
                })
                .Build();

            var messages = new List<ChatMessage>
            {
                new ChatMessage
                {
                    Id = "m1",
                    ChatId = "chat-3",
                    ProjectId = "standalone-proj",
                    UserId = "user-3",
                    Role = "user",
                    Content = "hello"
                },
                new ChatMessage
                {
                    Id = "m2",
                    ChatId = "chat-3",
                    ProjectId = "standalone-proj",
                    UserId = "user-3",
                    Role = "assistant",
                    Content = "hi"
                }
            };

            var cosmosDbMock = new Mock<CosmosDbService>();
            cosmosDbMock.Setup(x => x.UpsertProjectChatAsync(It.IsAny<ChatSession>(), It.IsAny<string>(), It.IsAny<string>()))
                .Returns(Task.CompletedTask);
            cosmosDbMock.Setup(x => x.GetChatMessagesAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(messages);
            cosmosDbMock.Setup(x => x.UpsertMessageAsync(It.IsAny<ChatMessage>(), It.IsAny<string>()))
                .Returns(Task.CompletedTask);

            var themeServiceMock = new Mock<ThemeService>();
            var textAnalyticsMock = new Mock<TextAnalyticsService>();
            var kernel = new Kernel();

            var sut = new ChatService(config, cosmosDbMock.Object, kernel, themeServiceMock.Object, textAnalyticsMock.Object);

            var chat = new ChatSession
            {
                Id = "chat-3",
                ProjectId = "standalone-proj",
                UserId = "user-3"
            };
            var targetProject = new Project
            {
                Id = "target-proj",
                UserId = "user-3",
                Name = "Target",
                JointSummary = ""
            };

            await Assert.ThrowsAnyAsync<Exception>(() => sut.MoveChatToProjectAsync(chat, targetProject, "user-3"));

            Assert.Equal("target-proj", chat.ProjectId);
            cosmosDbMock.Verify(x => x.UpsertProjectChatAsync(chat, "target-proj", "user-3"), Times.Once);
            cosmosDbMock.Verify(x => x.GetChatMessagesAsync("standalone-proj", "chat-3", "user-3"), Times.Once);
            cosmosDbMock.Verify(x => x.UpsertMessageAsync(It.Is<ChatMessage>(m => m.ProjectId == "target-proj"), "user-3"), Times.Exactly(2));
        }
    }
}
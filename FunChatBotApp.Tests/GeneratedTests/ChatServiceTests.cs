using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FunChatBotApp.Models;
using FunChatBotApp.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Moq;
using OpenAI.Chat;
using Xunit;

namespace FunChatBotApp.Tests.Services
{
    public class ChatServiceTests
    {
        private readonly Mock<IConfiguration> _mockConfig;
        private readonly Mock<CosmosDbService> _mockCosmosDb;
        private readonly Mock<Kernel> _mockKernel;
        private readonly Mock<ThemeService> _mockThemeService;
        private readonly Mock<TextAnalyticsService> _mockTextAnalytics;
        private readonly ChatService _chatService;

        public ChatServiceTests()
        {
            _mockConfig = new Mock<IConfiguration>();
            _mockCosmosDb = new Mock<CosmosDbService>();
            _mockKernel = new Mock<Kernel>();
            _mockThemeService = new Mock<ThemeService>();
            _mockTextAnalytics = new Mock<TextAnalyticsService>();

            _mockConfig.Setup(c => c["OpenAI:Endpoint"]).Returns("https://fakeendpoint");
            _mockConfig.Setup(c => c["OpenAI:ApiKey"]).Returns("fakeapikey");
            _mockConfig.Setup(c => c["OpenAI:DeploymentName"]).Returns("fakedeployment");

            // Setup Kernel Clone to return the same mock for simplicity.
            _mockKernel.Setup(k => k.Clone()).Returns(_mockKernel.Object);
            _mockKernel.SetupGet(k => k.Plugins).Returns(new Microsoft.SemanticKernel.Plugins.PluginCollection(_mockKernel.Object));
            _mockKernel.SetupGet(k => k.FunctionInvocationFilters).Returns(new List<Microsoft.SemanticKernel.SkillDefinition.IFunctionInvocationFilter>());

            _mockKernel.Setup(k => k.GetRequiredService<Microsoft.SemanticKernel.ChatCompletion.IChatCompletionService>())
                       .Returns(Mock.Of<Microsoft.SemanticKernel.ChatCompletion.IChatCompletionService>());

            _chatService = new ChatService(_mockConfig.Object, _mockCosmosDb.Object, _mockKernel.Object, _mockThemeService.Object, _mockTextAnalytics.Object);
        }

        [Fact]
        public async Task SendMessageAsync_ReturnsContentText()
        {
            // Arrange
            var history = new List<OpenAI.Chat.ChatMessage>
            {
                new UserChatMessage("Hello")
            };

            var chatCompletionMock = new Mock<ChatClientPublic>();
            var completionResult = new ClientResult<ChatCompletion>
            {
                Value = new ChatCompletion(
                    new List<ChatMessageBase>
                    {
                        new ChatMessageBase("assistant", "response text")
                    },
                    new Response())
            };

            var mockChatClient = new Mock<ChatClient>();
            mockChatClient.Setup(c => c.CompleteChatAsync(It.IsAny<List<ChatMessage>>(), default))
                .ReturnsAsync(completionResult);

            // Replace the private _chatClient field using reflection
            var field = typeof(ChatService).GetField("_chatClient", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            field.SetValue(_chatService, mockChatClient.Object);

            // Act
            var result = await _chatService.SendMessageAsync(history);

            // Assert
            Assert.Equal("response text", result);
        }

        [Fact]
        public async Task ProcessProjectMessageAsync_AddsUserAndAssistantMessages()
        {
            // Arrange
            var userMessageText = "User sensitive info";
            _mockTextAnalytics.Setup(x => x.RedactPiiAsync(It.IsAny<string>())).ReturnsAsync<string>(s => s);

            var project = new Project { Id = "proj1", JointSummary = "summary" };
            var chat = new ChatSession { Id = "chat1", ActiveDocumentText = "DocText" };
            var currentMessages = new List<Models.ChatMessage>();
            string userId = "user1";

            _mockCosmosDb.Setup(x => x.UpsertMessageAsync(It.IsAny<Models.ChatMessage>(), userId)).Returns(Task.CompletedTask);
            _mockCosmosDb.Setup(x => x.GetProjectMessagesAsync(project.Id, userId, It.IsAny<int>()))
                         .ReturnsAsync(new List<Models.ChatMessage>());

            var mockChatCompletionService = new Mock<Microsoft.SemanticKernel.ChatCompletion.IChatCompletionService>();
            var chatHistory = new Microsoft.SemanticKernel.ChatCompletion.ChatHistory();

            var resultContent = new Microsoft.SemanticKernel.ChatCompletion.ChatMessageContentResult("assistant response");
            var completionTask = Task.FromResult(resultContent);

            Microsoft.SemanticKernel.ChatCompletion.ChatCompletionServiceExtensions.GetChatMessageContentAsync = 
                (chatCompletion, history, settings, kernel) => completionTask;

            // Need to setup Kernel GetRequiredService to return the mockChatCompletionService.Object
            _mockKernel.Setup(k => k.GetRequiredService<Microsoft.SemanticKernel.ChatCompletion.IChatCompletionService>())
                       .Returns(mockChatCompletionService.Object);

            mockChatCompletionService.Setup(x => x.GetChatMessageContentAsync(It.IsAny<Microsoft.SemanticKernel.ChatCompletion.ChatHistory>(), It.IsAny<OpenAIPromptExecutionSettings>(), _mockKernel.Object))
                .ReturnsAsync(resultContent);

            // Act
            var response = await _chatService.ProcessProjectMessageAsync(project, chat, currentMessages, userMessageText, userId, true, 3);

            // Assert
            Assert.Equal("assistant response", response);
            Assert.Contains(currentMessages, m => m.Role == "user" && m.Content == userMessageText);
            Assert.Contains(currentMessages, m => m.Role == "assistant" && m.Content == "assistant response");

            _mockCosmosDb.Verify(x => x.UpsertMessageAsync(It.IsAny<Models.ChatMessage>(), userId), Times.Exactly(2));
        }

        [Fact]
        public async Task ProcessStandaloneMessageAsync_AddsUserAndAssistantMessages()
        {
            // Arrange
            var userMessage = "Hello standalone";
            _mockTextAnalytics.Setup(x => x.RedactPiiAsync(It.IsAny<string>())).ReturnsAsync<string>(s => s);

            var chat = new ChatSession { Id = "chat1", ProjectId = "proj1", ActiveDocumentText = "DocTextStandalone" };
            var currentMessages = new List<Models.ChatMessage>();
            string userId = "user1";

            _mockCosmosDb.Setup(x => x.UpsertMessageAsync(It.IsAny<Models.ChatMessage>(), userId)).Returns(Task.CompletedTask);

            var mockChatCompletionService = new Mock<Microsoft.SemanticKernel.ChatCompletion.IChatCompletionService>();

            var resultContent = new Microsoft.SemanticKernel.ChatCompletion.ChatMessageContentResult("assistant standalone response");
            var completionTask = Task.FromResult(resultContent);

            Microsoft.SemanticKernel.ChatCompletion.ChatCompletionServiceExtensions.GetChatMessageContentAsync = 
                (chatCompletion, history, settings, kernel) => completionTask;

            _mockKernel.Setup(k => k.Clone()).Returns(_mockKernel.Object);
            _mockKernel.Setup(k => k.GetRequiredService<Microsoft.SemanticKernel.ChatCompletion.IChatCompletionService>())
                       .Returns(mockChatCompletionService.Object);

            mockChatCompletionService.Setup(x => x.GetChatMessageContentAsync(It.IsAny<Microsoft.SemanticKernel.ChatCompletion.ChatHistory>(), It.IsAny<OpenAIPromptExecutionSettings>(), _mockKernel.Object))
                .ReturnsAsync(resultContent);

            // Act
            var response = await _chatService.ProcessStandaloneMessageAsync(chat, currentMessages, userMessage, userId, true, 2);

            // Assert
            Assert.Equal("assistant standalone response", response);
            Assert.Contains(currentMessages, m => m.Role == "user" && m.Content == userMessage);
            Assert.Contains(currentMessages, m => m.Role == "assistant" && m.Content == "assistant standalone response");

            _mockCosmosDb.Verify(x => x.UpsertMessageAsync(It.IsAny<Models.ChatMessage>(), userId), Times.Exactly(2));
        }

        [Fact]
        public async Task GenerateInitialRecommendationsAsync_ReturnsAssistantMessage()
        {
            // Arrange
            var chat = new ChatSession { Id = "chat1", ProjectId = "proj1" };
            var userId = "user1";

            var pluginMock = new Mock<UserStrategistPlugin>(_mockCosmosDb.Object, userId);
            pluginMock.Setup(p => p.GenerateRecommendationsAsync(_mockKernel.Object))
                .ReturnsAsync("Topic1\nTopic2\nTopic3");

            var messageStored = false;
            _mockCosmosDb.Setup(x => x.UpsertMessageAsync(It.IsAny<Models.ChatMessage>(), userId))
                         .Callback(() => messageStored = true)
                         .Returns(Task.CompletedTask);

            // Replace the instantiation of UserStrategistPlugin with a wrapper to use mock
            var chatService = new ChatService(_mockConfig.Object, _mockCosmosDb.Object, _mockKernel.Object, _mockThemeService.Object, _mockTextAnalytics.Object);

            // Act
            var result = await chatService.GenerateInitialRecommendationsAsync(chat, userId);

            // Assert
            Assert.Equal("assistant", result.Role);
            Assert.Equal(chat.Id, result.ChatId);
            Assert.Equal(userId, result.UserId);
            Assert.Contains("Szia", result.Content);
            Assert.Contains("Topic1", result.Topics);
            Assert.True(messageStored);
        }

        [Fact]
        public async Task GenerateUserProfileSummaryAsync_UpdatesUserProfileAndReturnsSummary()
        {
            // Arrange
            var userId = "user1";
            var userMessages = new List<Models.ChatMessage>
            {
                new Models.ChatMessage { Content = "Message1" },
                new Models.ChatMessage { Content = "Message2" }
            };

            var generatedText = "User profile summary text.";

            var completionResult = new ClientResult<ChatCompletion>
            {
                Value = new ChatCompletion(
                    new List<ChatMessageBase>
                    {
                        new ChatMessageBase("assistant", generatedText)
                    },
                    new Response())
            };

            var mockChatClient = new Mock<ChatClient>();
            mockChatClient.Setup(c => c.CompleteChatAsync(It.IsAny<List<ChatMessage>>(), default))
                .ReturnsAsync(completionResult);

            var field = typeof(ChatService).GetField("_chatClient", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            field.SetValue(_chatService, mockChatClient.Object);

            var existingProfile = new UserProfile { Summary = "Old summary" };
            _mockCosmosDb.Setup(x => x.GetUserProfileAsync(userId)).ReturnsAsync(existingProfile);
            _mockCosmosDb.Setup(x => x.UpsertUserProfileAsync(It.IsAny<UserProfile>(), userId)).Returns(Task.CompletedTask);

            // Act
            var result = await _chatService.GenerateUserProfileSummaryAsync(userId, userMessages);

            // Assert
            Assert.Equal(generatedText, result);
            _mockCosmosDb.Verify(x => x.GetUserProfileAsync(userId), Times.Once);
            _mockCosmosDb.Verify(x => x.UpsertUserProfileAsync(It.Is<UserProfile>(p => p.Summary == generatedText), userId), Times.Once);
        }
    }
}
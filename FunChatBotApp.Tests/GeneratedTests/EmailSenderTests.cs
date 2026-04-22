namespace FunChatBotApp.Tests.Services
{
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using FunChatBotApp.Data;
    using FunChatBotApp.Services;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.Logging;
    using Moq;
    using Xunit;

    public class EmailSenderTests
    {
        [Fact]
        public async Task Constructor_WithMissingConnectionString_LogsWarning_AndSendMethodsDoNotThrow()
        {
            var loggerMock = new Mock<ILogger<EmailSender>>();
            var configValues = new Dictionary<string, string?>
            {
                ["CommunicationServices:ConnectionString"] = null,
                ["CommunicationServices:SenderAddress"] = "sender@contoso.com"
            };
            IConfiguration configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(configValues)
                .Build();

            var sut = new EmailSender(loggerMock.Object, configuration);

            var user = new ApplicationUser { Email = "user@contoso.com" };
            await sut.SendConfirmationLinkAsync(user, "user@contoso.com", "https://confirm");
            await sut.SendPasswordResetCodeAsync(user, "user@contoso.com", "123456");
            await sut.SendPasswordResetLinkAsync(user, "user@contoso.com", "https://reset");

            loggerMock.VerifyLog(LogLevel.Warning, "Email ConnectionString nincs beállítva!", Times.Once());
            loggerMock.VerifyLog(LogLevel.Warning, "EmailClient nincs inicializálva, mert hiányzik a ConnectionString.", Times.Exactly(3));
        }

        [Fact]
        public async Task Constructor_WithPlaceholderConnectionString_LogsWarning_AndSendConfirmationDoesNotThrow()
        {
            var loggerMock = new Mock<ILogger<EmailSender>>();
            var configValues = new Dictionary<string, string?>
            {
                ["CommunicationServices:ConnectionString"] = "<AZURE_COMMUNICATION_CONNECTION_STRING>",
                ["CommunicationServices:SenderAddress"] = "sender@contoso.com"
            };
            IConfiguration configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(configValues)
                .Build();

            var sut = new EmailSender(loggerMock.Object, configuration);

            var user = new ApplicationUser { Email = "user@contoso.com" };
            await sut.SendConfirmationLinkAsync(user, "user@contoso.com", "https://confirm");

            loggerMock.VerifyLog(LogLevel.Warning, "Email ConnectionString nincs beállítva!", Times.Once());
            loggerMock.VerifyLog(LogLevel.Warning, "EmailClient nincs inicializálva, mert hiányzik a ConnectionString.", Times.Once());
        }

        [Fact]
        public async Task SendPasswordResetCode_WithMissingConnectionString_LogsClientNotInitializedWarning()
        {
            var loggerMock = new Mock<ILogger<EmailSender>>();
            var configValues = new Dictionary<string, string?>
            {
                ["CommunicationServices:ConnectionString"] = "",
                ["CommunicationServices:SenderAddress"] = "sender@contoso.com"
            };
            IConfiguration configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(configValues)
                .Build();

            var sut = new EmailSender(loggerMock.Object, configuration);

            var user = new ApplicationUser();
            await sut.SendPasswordResetCodeAsync(user, "user@contoso.com", "999999");

            loggerMock.VerifyLog(LogLevel.Warning, "EmailClient nincs inicializálva, mert hiányzik a ConnectionString.", Times.Once());
        }
    }

    internal static class LoggerMoqExtensions
    {
        public static void VerifyLog<T>(this Mock<ILogger<T>> loggerMock, LogLevel level, string message, Times times)
        {
            loggerMock.Verify(
                x => x.Log(
                    level,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString() != null && v.ToString()!.Contains(message)),
                    It.IsAny<System.Exception>(),
                    It.IsAny<System.Func<It.IsAnyType, System.Exception?, string>>()),
                times);
        }
    }
}
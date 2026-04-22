namespace FunChatBotApp.Tests.Services
{
    using System;
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
        public async Task Constructor_WithMissingConnectionString_AndSendConfirmation_DoesNotThrow()
        {
            var loggerMock = new Mock<ILogger<EmailSender>>();
            var configurationMock = new Mock<IConfiguration>();

            configurationMock.Setup(c => c["CommunicationServices:ConnectionString"]).Returns((string?)null);

            var sut = new EmailSender(loggerMock.Object, configurationMock.Object);

            var user = new ApplicationUser();
            var ex = await Record.ExceptionAsync(() =>
                sut.SendConfirmationLinkAsync(user, "test@example.com", "https://example.com/confirm"));

            Assert.Null(ex);
        }

        [Fact]
        public async Task Constructor_WithPlaceholderConnectionString_AndSendPasswordResetCode_DoesNotThrow()
        {
            var loggerMock = new Mock<ILogger<EmailSender>>();
            var configurationMock = new Mock<IConfiguration>();

            configurationMock
                .Setup(c => c["CommunicationServices:ConnectionString"])
                .Returns("<AZURE_COMMUNICATION_CONNECTION_STRING>");

            var sut = new EmailSender(loggerMock.Object, configurationMock.Object);

            var user = new ApplicationUser();
            var ex = await Record.ExceptionAsync(() =>
                sut.SendPasswordResetCodeAsync(user, "test@example.com", "123456"));

            Assert.Null(ex);
        }

        [Fact]
        public async Task SendPasswordResetLinkAsync_WithMissingSenderAddress_DoesNotThrow()
        {
            var loggerMock = new Mock<ILogger<EmailSender>>();
            var configurationMock = new Mock<IConfiguration>();

            configurationMock
                .Setup(c => c["CommunicationServices:ConnectionString"])
                .Returns((string?)null);

            configurationMock
                .Setup(c => c["CommunicationServices:SenderAddress"])
                .Returns((string?)null);

            var sut = new EmailSender(loggerMock.Object, configurationMock.Object);

            var user = new ApplicationUser();
            var ex = await Record.ExceptionAsync(() =>
                sut.SendPasswordResetLinkAsync(user, "test@example.com", "https://example.com/reset"));

            Assert.Null(ex);
        }

        [Fact]
        public async Task SendConfirmationLinkAsync_WithPlaceholderSenderAddress_DoesNotThrow()
        {
            var loggerMock = new Mock<ILogger<EmailSender>>();
            var configurationMock = new Mock<IConfiguration>();

            configurationMock
                .Setup(c => c["CommunicationServices:ConnectionString"])
                .Returns((string?)null);

            configurationMock
                .Setup(c => c["CommunicationServices:SenderAddress"])
                .Returns("<YOUR_VERIFIED_MAILFROM_ADDRESS>");

            var sut = new EmailSender(loggerMock.Object, configurationMock.Object);

            var user = new ApplicationUser();
            var ex = await Record.ExceptionAsync(() =>
                sut.SendConfirmationLinkAsync(user, "test@example.com", "https://example.com/confirm"));

            Assert.Null(ex);
        }
    }
}
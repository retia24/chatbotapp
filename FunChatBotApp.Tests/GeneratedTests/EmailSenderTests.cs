using System;
using System.Threading.Tasks;
using Azure;
using Azure.Communication.Email;
using FunChatBotApp.Data;
using FunChatBotApp.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace FunChatBotApp.Tests.Services
{
    public class EmailSenderTests
    {
        private readonly Mock<ILogger<EmailSender>> _loggerMock;
        private readonly Mock<IConfiguration> _configurationMock;
        private readonly Mock<EmailClient> _emailClientMock;

        public EmailSenderTests()
        {
            _loggerMock = new Mock<ILogger<EmailSender>>(MockBehavior.Strict);
            _configurationMock = new Mock<IConfiguration>(MockBehavior.Strict);
            _emailClientMock = new Mock<EmailClient>(MockBehavior.Strict);
        }

        private EmailSender CreateEmailSenderWithConnectionString(string connectionString)
        {
            _configurationMock.Reset();
            _loggerMock.Reset();

            _configurationMock.Setup(c => c["CommunicationServices:ConnectionString"]).Returns(connectionString);
            if (!string.IsNullOrEmpty(connectionString) && connectionString != "<AZURE_COMMUNICATION_CONNECTION_STRING>")
            {
                // We cannot inject EmailClient directly because EmailSender creates it internally.
                // So we create a derived test class to override email client usage.
                return new TestEmailSender(_loggerMock.Object, _configurationMock.Object, _emailClientMock.Object);
            }
            else
            {
                return new EmailSender(_loggerMock.Object, _configurationMock.Object);
            }
        }

        private class TestEmailSender : EmailSender
        {
            private readonly EmailClient _testEmailClient;

            public TestEmailSender(ILogger<EmailSender> logger, IConfiguration configuration, EmailClient emailClient)
                : base(logger, configuration)
            {
                _testEmailClient = emailClient;
            }

            protected override EmailClient? CreateEmailClient(string connectionString)
            {
                return _testEmailClient;
            }

            public Task SendEmailAsyncTest(string email, string subject, string htmlMessage)
            {
                return InvokeSendEmailAsync(email, subject, htmlMessage);
            }
        }

        [Fact]
        public void Constructor_WithMissingConnectionString_LogsWarning()
        {
            // Arrange
            _configurationMock.Setup(c => c["CommunicationServices:ConnectionString"]).Returns("");
            _loggerMock.Setup(x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Email ConnectionString nincs beállítva!")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()));

            // Act
            var sender = new EmailSender(_loggerMock.Object, _configurationMock.Object);

            // Assert
            _loggerMock.VerifyAll();
        }

        [Fact]
        public async Task SendConfirmationLinkAsync_CallsSendEmailAsync_WithCorrectParameters()
        {
            // Arrange
            var connectionString = "valid-connection-string";
            var senderAddress = "sender@example.com";
            var user = new ApplicationUser();
            var email = "test@example.com";
            var confirmationLink = "http://confirm.link";

            var emailSendOperationMock = new Mock<EmailSendOperation>();
            emailSendOperationMock.SetupGet(o => o.Id).Returns("operation-id");

            _configurationMock.Setup(c => c["CommunicationServices:ConnectionString"]).Returns(connectionString);
            _configurationMock.Setup(c => c["CommunicationServices:SenderAddress"]).Returns(senderAddress);

            _emailClientMock.Reset();
            _emailClientMock
                .Setup(c => c.SendAsync(
                    WaitUntil.Started,
                    It.Is<EmailMessage>(em =>
                        em.RecipientAddress == email &&
                        em.SenderAddress == senderAddress &&
                        em.Content.Subject == "Megerősítő email" &&
                        em.Content.Html.Contains(confirmationLink)
                    ),
                    default))
                .ReturnsAsync(emailSendOperationMock.Object);

            var sender = new TestEmailSender(_loggerMock.Object, _configurationMock.Object, _emailClientMock.Object);

            _loggerMock.Setup(x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString().Contains($"Email sikeresen elküldve ide: {email}")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()));

            // Act
            await sender.SendConfirmationLinkAsync(user, email, confirmationLink);

            // Assert
            _emailClientMock.VerifyAll();
            _loggerMock.VerifyAll();
        }

        [Fact]
        public async Task SendPasswordResetCodeAsync_CallsSendEmailAsync_WithCorrectParameters()
        {
            // Arrange
            var connectionString = "valid-connection-string";
            var senderAddress = "sender@example.com";
            var user = new ApplicationUser();
            var email = "reset@example.com";
            var resetCode = "123456";

            var emailSendOperationMock = new Mock<EmailSendOperation>();
            emailSendOperationMock.SetupGet(o => o.Id).Returns("operation-id");

            _configurationMock.Setup(c => c["CommunicationServices:ConnectionString"]).Returns(connectionString);
            _configurationMock.Setup(c => c["CommunicationServices:SenderAddress"]).Returns(senderAddress);

            _emailClientMock.Reset();
            _emailClientMock
                .Setup(c => c.SendAsync(
                    WaitUntil.Started,
                    It.Is<EmailMessage>(em =>
                        em.RecipientAddress == email &&
                        em.SenderAddress == senderAddress &&
                        em.Content.Subject == "Jelszó visszaállítása" &&
                        em.Content.Html.Contains(resetCode)
                    ),
                    default))
                .ReturnsAsync(emailSendOperationMock.Object);

            var sender = new TestEmailSender(_loggerMock.Object, _configurationMock.Object, _emailClientMock.Object);

            _loggerMock.Setup(x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString().Contains($"Email sikeresen elküldve ide: {email}")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()));

            // Act
            await sender.SendPasswordResetCodeAsync(user, email, resetCode);

            // Assert
            _emailClientMock.VerifyAll();
            _loggerMock.VerifyAll();
        }

        [Fact]
        public async Task SendPasswordResetLinkAsync_CallsSendEmailAsync_WithCorrectParameters()
        {
            // Arrange
            var connectionString = "valid-connection-string";
            var senderAddress = "sender@example.com";
            var user = new ApplicationUser();
            var email = "link@example.com";
            var resetLink = "http://reset.link";

            var emailSendOperationMock = new Mock<EmailSendOperation>();
            emailSendOperationMock.SetupGet(o => o.Id).Returns("operation-id");

            _configurationMock.Setup(c => c["CommunicationServices:ConnectionString"]).Returns(connectionString);
            _configurationMock.Setup(c => c["CommunicationServices:SenderAddress"]).Returns(senderAddress);

            _emailClientMock.Reset();
            _emailClientMock
                .Setup(c => c.SendAsync(
                    WaitUntil.Started,
                    It.Is<EmailMessage>(em =>
                        em.RecipientAddress == email &&
                        em.SenderAddress == senderAddress &&
                        em.Content.Subject == "Jelszó visszaállítása" &&
                        em.Content.Html.Contains(resetLink)
                    ),
                    default))
                .ReturnsAsync(emailSendOperationMock.Object);

            var sender = new TestEmailSender(_loggerMock.Object, _configurationMock.Object, _emailClientMock.Object);

            _loggerMock.Setup(x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString().Contains($"Email sikeresen elküldve ide: {email}")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()));

            // Act
            await sender.SendPasswordResetLinkAsync(user, email, resetLink);

            // Assert
            _emailClientMock.VerifyAll();
            _loggerMock.VerifyAll();
        }

        [Fact]
        public async Task SendEmailAsync_WithNullEmailClient_LogsWarning()
        {
            // Arrange
            _configurationMock.Setup(c => c["CommunicationServices:ConnectionString"]).Returns("");
            _configurationMock.Setup(c => c["CommunicationServices:SenderAddress"]).Returns("sender@example.com");

            var sender = new EmailSender(_loggerMock.Object, _configurationMock.Object);

            _loggerMock.Setup(x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("EmailClient nincs inicializálva")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()));

            // Act
            await sender.SendConfirmationLinkAsync(new ApplicationUser(), "to@example.com", "link");

            // Assert
            _loggerMock.VerifyAll();
        }

        [Fact]
        public async Task SendEmailAsync_WithInvalidSenderAddress_LogsWarningAndDoesNotSend()
        {
            // Arrange
            var connectionString = "valid-connection-string";

            _configurationMock.Setup(c => c["CommunicationServices:ConnectionString"]).Returns(connectionString);
            _configurationMock.SetupSequence(c => c["CommunicationServices:SenderAddress"])
                .Returns("")
                .Returns("<YOUR_VERIFIED_MAILFROM_ADDRESS>");

            var sender = new TestEmailSender(_loggerMock.Object, _configurationMock.Object, _emailClientMock.Object);

            _loggerMock.Setup(x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("SenderAddress nincs beállítva")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()))
                .Verifiable();

            // Act & Assert first with empty sender address
            await sender.SendConfirmationLinkAsync(new ApplicationUser(), "test@example.com", "link");

            _loggerMock.Verify(x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("SenderAddress nincs beállítva")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);

            // Reset logs for second call
            _loggerMock.Invocations.Clear();

            // Act & Assert second with placeholder sender address
            await sender.SendConfirmationLinkAsync(new ApplicationUser(), "test@example.com", "link");

            _loggerMock.Verify(x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("SenderAddress nincs beállítva")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);

            _emailClientMock.Verify(c => c.SendAsync(It.IsAny<WaitUntil>(), It.IsAny<EmailMessage>(), default), Times.Never);
        }

        [Fact]
        public async Task SendEmailAsync_WhenSendThrows_LogsError()
        {
            // Arrange
            var connectionString = "valid-connection-string";
            var senderAddress = "sender@example.com";
            var email = "error@example.com";

            _configurationMock.Setup(c => c["CommunicationServices:ConnectionString"]).Returns(connectionString);
            _configurationMock.Setup(c => c["CommunicationServices:SenderAddress"]).Returns(senderAddress);

            var exception = new Exception("send failure");

            _emailClientMock.Reset();
            _emailClientMock
                .Setup(c => c.SendAsync(
                    WaitUntil.Started,
                    It.IsAny<EmailMessage>(),
                    default))
                .ThrowsAsync(exception);

            var sender = new TestEmailSender(_loggerMock.Object, _configurationMock.Object, _emailClientMock.Object);

            _loggerMock.Setup(x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString().Contains($"Hiba történt az email küldésekor ide: {email}")),
                exception,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()));

            // Act
            await sender.SendPasswordResetCodeAsync(new ApplicationUser(), email, "code");

            // Assert
            _loggerMock.VerifyAll();
        }
    }
}
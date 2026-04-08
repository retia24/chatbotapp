using Microsoft.AspNetCore.Identity;
using FunChatBotApp.Data;
using Azure.Communication.Email;
using Azure;

namespace FunChatBotApp.Services
{
    public class EmailSender : IEmailSender<ApplicationUser>
    {
        private readonly ILogger<EmailSender> _logger;
        private readonly IConfiguration _configuration;
        private readonly EmailClient? _emailClient;

        public EmailSender(ILogger<EmailSender> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;

            var connectionString = _configuration["CommunicationServices:ConnectionString"];
            if (!string.IsNullOrEmpty(connectionString) && connectionString != "<AZURE_COMMUNICATION_CONNECTION_STRING>")
            {
                _emailClient = new EmailClient(connectionString);
            }
            else
            {
                _logger.LogWarning("Email ConnectionString nincs beállítva!");
            }
        }

        public async Task SendConfirmationLinkAsync(ApplicationUser user, string email, string confirmationLink)
        {
            await SendEmailAsync(email, "Megerősítő email", $"Kérjük, erősítse meg a regisztrációját: <a href='{confirmationLink}'>ide kattintva</a>");
        }

        public async Task SendPasswordResetCodeAsync(ApplicationUser user, string email, string resetCode)
        {
            await SendEmailAsync(email, "Jelszó visszaállítása", $"A jelszó visszaállító kódja: {resetCode}");
        }

        public async Task SendPasswordResetLinkAsync(ApplicationUser user, string email, string resetLink)
        {
            await SendEmailAsync(email, "Jelszó visszaállítása", $"Kérjük, állítsa vissza a jelszavát a következő linken: <a href='{resetLink}'>ide kattintva</a>");
        }

        private async Task SendEmailAsync(string email, string subject, string htmlMessage)
        {
            if (_emailClient == null)
            {
                _logger.LogWarning("EmailClient nincs inicializálva, mert hiányzik a ConnectionString.");
                return;
            }

            try
            {
                var senderAddress = _configuration["CommunicationServices:SenderAddress"];

                if (string.IsNullOrEmpty(senderAddress) || senderAddress == "<YOUR_VERIFIED_MAILFROM_ADDRESS>")
                {
                    _logger.LogWarning("SenderAddress nincs beállítva. Az email nem lett elküldve.");
                    return;
                }

                var emailMessage = new EmailMessage(
                    senderAddress: senderAddress,
                    recipientAddress: email,
                    content: new EmailContent(subject)
                    {
                        Html = htmlMessage
                    });

                // Elküldés (WaitUntil.Started garantálja, hogy nem várunk a teljes delivery-re a UI-on)
                EmailSendOperation emailSendOperation = await _emailClient.SendAsync(
                    WaitUntil.Started,
                    emailMessage);

                _logger.LogInformation($"Email sikeresen elküldve ide: {email}. OperationId: {emailSendOperation.Id}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Hiba történt az email küldésekor ide: {email}");
            }
        }
    }
}

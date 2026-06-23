// ============================================================
// ProximaLMS/Services/EmailService.cs
// ------------------------------------------------------------
// Reusable, DI-registered email sender. Patterned on the existing
// LoginController.SendOtpEmailAsync — same SMTP config, same
// graceful failure (returns false instead of throwing) so callers
// can fire-and-forget without crashing the request path.
//
// Why this lives in the MVC project rather than the API:
//   The bulk-assign API call returns the list of newly-created
//   assignments; the MVC proxy iterates them and sends notifications.
//   Keeping email in the MVC tier means the API stays a pure data
//   service and the SMTP creds live in only one appsettings.
//
// Register in Program.cs:
//   builder.Services.AddSingleton<IEmailService, SmtpEmailService>();
//
// Then inject IEmailService anywhere you need it.
// ============================================================
using System;
using System.Net;
using System.Net.Mail;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ProximaLMS.Services
{
    public interface IEmailService
    {
        /// <summary>Send a plain-text + HTML email. Returns false on failure
        /// (does not throw — caller can fire-and-forget safely).</summary>
        Task<bool> SendAsync(string toEmail, string subject, string plainBody, string htmlBody);
    }

    public class SmtpEmailService : IEmailService
    {
        private readonly IConfiguration _config;
        private readonly ILogger<SmtpEmailService> _logger;

        public SmtpEmailService(IConfiguration config, ILogger<SmtpEmailService> logger)
        {
            _config = config;
            _logger = logger;
        }

        public async Task<bool> SendAsync(string toEmail, string subject, string plainBody, string htmlBody)
        {
            if (string.IsNullOrWhiteSpace(toEmail))
            {
                _logger.LogWarning("EmailService: empty recipient — skipping.");
                return false;
            }

            try
            {
                var s = _config.GetSection("EmailSettings");

                using var client = new SmtpClient(s["SmtpHost"], int.Parse(s["SmtpPort"] ?? "587"))
                {
                    UseDefaultCredentials = false,
                    Credentials = new NetworkCredential(s["Username"], s["Password"]),
                    EnableSsl = bool.Parse(s["EnableSsl"] ?? "true"),
                    DeliveryMethod = SmtpDeliveryMethod.Network
                };

                using var message = new MailMessage
                {
                    From = new MailAddress(s["SenderEmail"], s["SenderName"] ?? "ProximaLMS"),
                    Subject = subject ?? "ProximaLMS notification",
                    IsBodyHtml = true
                };
                message.To.Add(toEmail);

                if (!string.IsNullOrEmpty(plainBody))
                    message.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(
                        plainBody, null, "text/plain"));

                if (!string.IsNullOrEmpty(htmlBody))
                    message.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(
                        htmlBody, null, "text/html"));

                await client.SendMailAsync(message);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "EmailService send failed. To={To} Subject={Subject}", toEmail, subject);
                return false;
            }
        }
    }
}

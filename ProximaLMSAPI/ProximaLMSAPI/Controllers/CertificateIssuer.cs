// ============================================================
// ProximaLMSAPI/Services/CertificateIssuer.cs
// ------------------------------------------------------------
// Reusable certificate issuance so auto-triggers (exam pass /
// course completion) and the manual admin endpoint share one path.
//
//   IssueAsync → SP_Certificate_Issue (idempotent on student+course)
//              → if newly issued: email the PDF (with attachment)
//                and send a "certificate ready" in-app + SMS notice.
//
// Self-contained: builds the PDF via ICertificateService, sends mail
// via the same EmailSettings keys the rest of the app uses, and
// surfaces the in-app/SMS notice via INotificationService. Never
// throws into the caller — certificate issuance must not break the
// exam-submit or progress flow.
// ============================================================
using Dapper;
using MySql.Data.MySqlClient;
using System.Data;
using System.Net;
using System.Net.Mail;

namespace ProximaLMSAPI.Services
{
    public interface ICertificateIssuer
    {
        // Returns the certificate number if issued/found, else "".
        Task<string> IssueAsync(int studentId, int courseId, string source,
                                int? examAttemptId = null, string issuedBy = "system",
                                bool sendEmail = true);
    }

    public class CertificateIssuer : ICertificateIssuer
    {
        private readonly IConfiguration _config;
        private readonly ICertificateService _cert;
        private readonly INotificationService _notifier;
        private readonly ILogger<CertificateIssuer> _logger;

        public CertificateIssuer(IConfiguration config, ICertificateService cert,
                                 INotificationService notifier, ILogger<CertificateIssuer> logger)
        {
            _config = config;
            _cert = cert;
            _notifier = notifier;
            _logger = logger;
        }

        private IDbConnection Conn() => new MySqlConnection(_config.GetConnectionString("ConnectionString"));
        private string PublicBase() => (_config["PublicBaseUrl"] ?? _config["ApiBaseUrl"] ?? "").TrimEnd('/');

        public async Task<string> IssueAsync(int studentId, int courseId, string source,
                                             int? examAttemptId = null, string issuedBy = "system",
                                             bool sendEmail = true)
        {
            if (studentId <= 0 || courseId <= 0) return "";

            try
            {
                using var conn = Conn();
                var token = Guid.NewGuid().ToString("N");

                var p = new DynamicParameters();
                p.Add("p_StudentID", studentId);
                p.Add("p_CourseID", courseId);
                p.Add("p_Source", string.IsNullOrWhiteSpace(source) ? "COURSE" : source);
                p.Add("p_ExamAttemptID", examAttemptId);
                p.Add("p_VerifyToken", token);
                p.Add("p_IssuedBy", issuedBy ?? "system");
                p.Add("p_CertificateID", dbType: DbType.Int32, direction: ParameterDirection.Output);
                p.Add("p_CertificateNo", dbType: DbType.String, size: 40, direction: ParameterDirection.Output);
                p.Add("p_AlreadyExists", dbType: DbType.Byte, direction: ParameterDirection.Output);

                await conn.ExecuteAsync("SP_Certificate_Issue", p, commandType: CommandType.StoredProcedure);

                int certId = p.Get<int>("p_CertificateID");
                string certNo = p.Get<string>("p_CertificateNo") ?? "";
                bool existed = p.Get<byte>("p_AlreadyExists") == 1;

                if (certId <= 0) return certNo;

                // only email + notify on a brand-new certificate
                if (!existed)
                {
                    var (data, _) = await BuildRenderData(certId);
                    string courseTitle = data?.CourseTitle ?? "your course";

                    if (sendEmail && data != null)
                        await EmailCertificateAsync(certId, data);

                    // in-app + SMS "certificate ready" (the PDF email was sent above)
                    string link = string.IsNullOrEmpty(PublicBase())
                        ? "#" : $"{PublicBase()}/StudentDashboard";

                    await _notifier.NotifyAsync(new NotifyRequest
                    {
                        UserID = studentId,
                        EventCode = "CERTIFICATE",
                        Title = "Certificate ready!",
                        Body = $"Your certificate for <strong>{courseTitle}</strong> ({certNo}) is ready to download.",
                        LinkUrl = link,
                        Icon = "fa-solid fa-certificate",
                        SendInApp = true,
                        SendEmail = false,   // PDF email already sent with attachment
                        SendSms = true,
                        SmsText = $"ProximaLMS: Your certificate {certNo} for {courseTitle} is ready."
                    });
                }

                return certNo;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Certificate auto-issue failed. Student={S} Course={C}", studentId, courseId);
                return "";
            }
        }

        // ── helpers ──────────────────────────────────────────
        private async Task<(CertificateRenderData? data, string no)> BuildRenderData(int certId)
        {
            using var conn = Conn();
            var row = await conn.QuerySingleOrDefaultAsync("SP_Certificate_Get",
                new { p_CertificateID = certId }, commandType: CommandType.StoredProcedure);
            if (row == null) return (null, "");

            string token = (string)row.VerifyToken;
            var data = new CertificateRenderData
            {
                CertificateNo = (string)row.CertificateNo,
                StudentName = (string)row.StudentName,
                CourseTitle = (string)row.CourseTitle,
                IssuedDate = (DateTime)row.IssuedDate,
                Source = (string)row.Source,
                VerifyUrl = $"{PublicBase()}/verify/{token}",
                BackgroundImageUrl = row.BackgroundImageUrl as string,
                Orientation = (row.Orientation as string) ?? "landscape"
            };
            return (data, data.CertificateNo);
        }

        private async Task EmailCertificateAsync(int certId, CertificateRenderData data)
        {
            try
            {
                using var conn = Conn();
                var email = await conn.QuerySingleOrDefaultAsync<string>(
                    "SELECT u.Email FROM TblCertificate c JOIN TblUserMasters u ON c.StudentID = u.ID WHERE c.CertificateID = @id",
                    new { id = certId });
                if (string.IsNullOrWhiteSpace(email)) return;

                var pdf = _cert.BuildPdf(data);
                string subject = $"Your Certificate — {data.CourseTitle}";
                string html = $@"<p>Hi {data.StudentName},</p>
<p>Congratulations! Your certificate for <b>{data.CourseTitle}</b> is attached.</p>
<p>Certificate No: <b>{data.CertificateNo}</b><br/>
Verify anytime at: <a href='{data.VerifyUrl}'>{data.VerifyUrl}</a></p>
<p>— Team ProximaLMS</p>";

                await SendWithPdfAsync(email, subject, html, pdf, $"{data.CertificateNo}.pdf");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Email certificate {Id} failed", certId);
            }
        }

        private async Task SendWithPdfAsync(string toEmail, string subject,
                                            string htmlBody, byte[] pdf, string fileName)
        {
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
                    Subject = subject,
                    Body = htmlBody,
                    IsBodyHtml = true
                };
                message.To.Add(toEmail);
                using var ms = new MemoryStream(pdf);
                message.Attachments.Add(new Attachment(ms, fileName, "application/pdf"));
                await client.SendMailAsync(message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SendWithPdfAsync failed to {To}", toEmail);
            }
        }
    }
}
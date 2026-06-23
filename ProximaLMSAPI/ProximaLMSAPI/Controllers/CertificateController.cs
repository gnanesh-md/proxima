// ============================================================
// ProximaLMSAPI/Controllers/CertificateController.cs
// ------------------------------------------------------------
// Module 10 — issued certificates: issue, list, download (PDF),
// public verify, email-with-PDF, admin manual issue + revoke.
//
// Depends on: ICertificateService (DI), EmailService (DI),
// SP_Certificate_* (see Certificate_DB.sql).
// ============================================================
using Dapper;
using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
using ProximaLMSAPI.Services;
using System.Data;
using System.Net;
using System.Net.Mail;

namespace ProximaLMSAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CertificateController : ControllerBase
    {
        private readonly IConfiguration _config;
        private readonly ICertificateService _cert;
        private readonly ILogger<CertificateController> _logger;

        public CertificateController(IConfiguration config, ICertificateService cert,
                                     ILogger<CertificateController> logger)
        {
            _config = config; _cert = cert; _logger = logger;
        }

        private IDbConnection Conn() => new MySqlConnection(_config.GetConnectionString("ConnectionString"));
        private string PublicBase() => (_config["PublicBaseUrl"] ?? "").TrimEnd('/');

        // ════════════════════════════════════════════════════
        // POST api/certificate/issue
        // Body: { StudentID, CourseID, Source(COURSE|EXAM|MANUAL), ExamAttemptID?, IssuedBy?, SendEmail? }
        // Idempotent — returns existing cert if already issued.
        // ════════════════════════════════════════════════════
        [HttpPost("issue")]
        public async Task<IActionResult> Issue([FromBody] IssueRequest req)
        {
            if (req == null || req.StudentID <= 0 || req.CourseID <= 0)
                return BadRequest(new { success = false, message = "StudentID and CourseID are required." });

            try
            {
                using var conn = Conn();
                var token = Guid.NewGuid().ToString("N");

                var p = new DynamicParameters();
                p.Add("p_StudentID", req.StudentID);
                p.Add("p_CourseID", req.CourseID);
                p.Add("p_Source", string.IsNullOrWhiteSpace(req.Source) ? "COURSE" : req.Source);
                p.Add("p_ExamAttemptID", req.ExamAttemptID);
                p.Add("p_VerifyToken", token);
                p.Add("p_IssuedBy", req.IssuedBy ?? "system");
                p.Add("p_CertificateID", dbType: DbType.Int32, direction: ParameterDirection.Output);
                p.Add("p_CertificateNo", dbType: DbType.String, size: 40, direction: ParameterDirection.Output);
                p.Add("p_AlreadyExists", dbType: DbType.Byte, direction: ParameterDirection.Output);

                await conn.ExecuteAsync("SP_Certificate_Issue", p, commandType: CommandType.StoredProcedure);

                int certId = p.Get<int>("p_CertificateID");
                string certNo = p.Get<string>("p_CertificateNo");
                bool existed = p.Get<byte>("p_AlreadyExists") == 1;

                if (req.SendEmail && !existed)
                    await TryEmailCertificate(certId);

                return Ok(new { success = true, certificateId = certId, certificateNo = certNo, alreadyExisted = existed });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Certificate issue failed");
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        // GET api/certificate/student/{studentId}  — dashboard list
        [HttpGet("student/{studentId:int}")]
        public async Task<IActionResult> ListByStudent(int studentId)
        {
            using var conn = Conn();
            var rows = await conn.QueryAsync("SP_Certificate_ListByStudent",
                new { p_StudentID = studentId }, commandType: CommandType.StoredProcedure);
            return Ok(new { success = true, data = rows });
        }

        // GET api/certificate/all  — admin list
        [HttpGet("all")]
        public async Task<IActionResult> ListAll()
        {
            using var conn = Conn();
            var rows = await conn.QueryAsync("SP_Certificate_ListAll",
                commandType: CommandType.StoredProcedure);
            return Ok(new { success = true, data = rows });
        }

        // GET api/certificate/{id}/download  — streams the PDF
        [HttpGet("{id:int}/download")]
        public async Task<IActionResult> Download(int id)
        {
            var (data, no) = await BuildRenderData(id);
            if (data == null) return NotFound(new { success = false, message = "Certificate not found." });

            var pdf = _cert.BuildPdf(data);
            return File(pdf, "application/pdf", $"{no}.pdf");
        }

        // GET api/certificate/verify/{token}  — PUBLIC, no auth
        [HttpGet("verify/{token}")]
        public async Task<IActionResult> Verify(string token)
        {
            using var conn = Conn();
            var row = await conn.QuerySingleOrDefaultAsync("SP_Certificate_Verify",
                new { p_Token = token }, commandType: CommandType.StoredProcedure);

            if (row == null)
                return Ok(new { success = true, valid = false });

            // MySQL tinyint(1) comes back as bool via Dapper; Convert handles
            // bool/sbyte/int alike so we never hit a bad runtime cast.
            bool revoked = Convert.ToBoolean(row.IsRevoked ?? false);
            return Ok(new
            {
                success = true,
                valid = !revoked,
                revoked,
                certificateNo = (string)row.CertificateNo,
                studentName = (string)row.StudentName,
                courseTitle = (string)row.CourseTitle,
                issuedDate = (DateTime)row.IssuedDate,
                source = (string)row.Source
            });
        }

        // POST api/certificate/{id}/email  — re-send the PDF by email
        [HttpPost("{id:int}/email")]
        public async Task<IActionResult> Email(int id)
        {
            var ok = await TryEmailCertificate(id);
            return ok ? Ok(new { success = true })
                      : StatusCode(500, new { success = false, message = "Could not email the certificate." });
        }

        // POST api/certificate/revoke  — admin
        [HttpPost("revoke")]
        public async Task<IActionResult> Revoke([FromBody] IdRequest req)
        {
            using var conn = Conn();
            var p = new DynamicParameters();
            p.Add("p_CertificateID", req.ID);
            p.Add("p_ResultCode", dbType: DbType.Int32, direction: ParameterDirection.Output);
            await conn.ExecuteAsync("SP_Certificate_Revoke", p, commandType: CommandType.StoredProcedure);
            return Ok(new { success = p.Get<int>("p_ResultCode") > 0 });
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
                StudentName   = (string)row.StudentName,
                CourseTitle   = (string)row.CourseTitle,
                IssuedDate    = (DateTime)row.IssuedDate,
                Source        = (string)row.Source,
                VerifyUrl     = $"{PublicBase()}/verify/{token}",
                BackgroundImageUrl = row.BackgroundImageUrl as string,
                Orientation   = (row.Orientation as string) ?? "landscape"
            };
            return (data, data.CertificateNo);
        }

        private async Task<bool> TryEmailCertificate(int certId)
        {
            try
            {
                var (data, no) = await BuildRenderData(certId);
                if (data == null) return false;

                // need the student's email
                using var conn = Conn();
                var email = await conn.QuerySingleOrDefaultAsync<string>(
                    "SELECT u.Email FROM TblCertificate c JOIN TblUserMasters u ON c.StudentID = u.ID WHERE c.CertificateID = @id",
                    new { id = certId });
                if (string.IsNullOrWhiteSpace(email)) return false;

                var pdf = _cert.BuildPdf(data);
                string subject = $"Your Certificate — {data.CourseTitle}";
                string html = $@"<p>Hi {data.StudentName},</p>
<p>Congratulations! Your certificate for <b>{data.CourseTitle}</b> is attached.</p>
<p>Certificate No: <b>{data.CertificateNo}</b><br/>
Verify anytime at: <a href='{data.VerifyUrl}'>{data.VerifyUrl}</a></p>
<p>— Team ProximaLMS</p>";

                return await SendWithPdfAsync(email, subject, html, pdf, $"{no}.pdf");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Email certificate {Id} failed", certId);
                return false;
            }
        }

        // Self-contained SMTP send with a PDF attachment (same EmailSettings keys
        // the MVC EmailService uses, so no cross-project dependency).
        private async Task<bool> SendWithPdfAsync(string toEmail, string subject,
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
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SendWithPdfAsync failed to {To}", toEmail);
                return false;
            }
        }

        public class IssueRequest
        {
            public int     StudentID     { get; set; }
            public int     CourseID      { get; set; }
            public string? Source        { get; set; }
            public int?    ExamAttemptID { get; set; }
            public string? IssuedBy      { get; set; }
            public bool    SendEmail     { get; set; } = true;
        }
        public class IdRequest { public int ID { get; set; } }
    }
}

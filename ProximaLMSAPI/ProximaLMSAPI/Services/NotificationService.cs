using Dapper;
using MySql.Data.MySqlClient;
using ProximaLMSAPI.Hubs;
using System.Data;
using System.Net;
using System.Net.Mail;
using System.Text.RegularExpressions;

namespace ProximaLMSAPI.Services
{
    public class NotifyRequest
    {
        public int    UserID    { get; set; }
        public string EventCode { get; set; } = "";   // ENROLLMENT, PAYMENT, CERTIFICATE, EXAM_RESULT, ASSIGNMENT, BADGE...
        public string Title     { get; set; } = "";   // in-app title
        public string? Body     { get; set; }         // in-app body
        public string? LinkUrl  { get; set; }
        public string? Icon     { get; set; }

        // channels (the service still checks user prefs on top of these)
        public bool SendInApp = true;
        public bool SendEmail = false;
        public bool SendSms   = false;

        // email
        public string? EmailTemplateCode { get; set; }            // looked up in TblEmailTemplates
        public Dictionary<string, string>? Vars { get; set; }     // {{Var}} replacements
        // sms
        public string? SmsText { get; set; }
    }

    public interface INotificationService
    {
        Task NotifyAsync(NotifyRequest req);
    }

    public class NotificationService : INotificationService
    {
        private readonly IConfiguration _config;
        private readonly ISmsSender _sms;
        private readonly INotificationPush _push;
        private readonly ILogger<NotificationService> _logger;

        public NotificationService(IConfiguration config, ISmsSender sms,
                                   INotificationPush push, ILogger<NotificationService> logger)
        {
            _config = config; _sms = sms; _push = push; _logger = logger;
        }

        private IDbConnection Conn() => new MySqlConnection(_config.GetConnectionString("ConnectionString"));

        public async Task NotifyAsync(NotifyRequest req)
        {
            if (req == null || req.UserID <= 0) return;
            try
            {
                using var conn = Conn();
                conn.Open();

                // recipient + prefs
                var user = await conn.QuerySingleOrDefaultAsync(
                    "SELECT ID, Name, Email, MobileNumber FROM TblUserMasters WHERE ID = @id",
                    new { id = req.UserID });
                if (user == null) return;

                var pref = await conn.QuerySingleOrDefaultAsync(
                    "SP_NotifPref_Get", new { p_UserID = req.UserID },
                    commandType: CommandType.StoredProcedure);

                bool emailOn = pref == null || Convert.ToInt32(pref.EmailEnabled) == 1;
                bool smsOn   = pref == null || Convert.ToInt32(pref.SmsEnabled)   == 1;
                string muted = (pref?.MutedEvents as string) ?? "";
                bool eventMuted = !string.IsNullOrEmpty(muted) &&
                                  muted.Split(',').Select(x => x.Trim())
                                       .Contains(req.EventCode, StringComparer.OrdinalIgnoreCase);

                // ── in-app (SP also re-checks InAppEnabled + mute) ──
                if (req.SendInApp && !eventMuted)
                {
                    var p = new DynamicParameters();
                    p.Add("p_UserID", req.UserID);
                    p.Add("p_EventCode", req.EventCode);
                    p.Add("p_Title", req.Title);
                    p.Add("p_Body", req.Body ?? "");
                    p.Add("p_LinkUrl", req.LinkUrl ?? "");
                    p.Add("p_Icon", req.Icon ?? "fa-solid fa-bell");
                    p.Add("p_NotifID", dbType: DbType.Int32, direction: ParameterDirection.Output);
                    await conn.ExecuteAsync("SP_Notif_Create", p, commandType: CommandType.StoredProcedure);

                    int notifId = p.Get<int>("p_NotifID");
                    if (notifId > 0)
                    {
                        // unread count for the badge
                        var unread = await conn.ExecuteScalarAsync<int>(
                            "SP_Notif_UnreadCount", new { p_UserID = req.UserID },
                            commandType: CommandType.StoredProcedure);

                        // SignalR push (best-effort)
                        try
                        {
                            await _push.PushToUser(req.UserID, new
                            {
                                notificationId = notifId,
                                title = req.Title,
                                body = req.Body,
                                link = req.LinkUrl,
                                icon = req.Icon ?? "fa-solid fa-bell",
                                unread
                            });
                        }
                        catch (Exception ex) { _logger.LogWarning(ex, "SignalR push failed (non-fatal)"); }
                    }
                }

                // ── email ──
                if (req.SendEmail && emailOn && !eventMuted && !string.IsNullOrWhiteSpace((string?)user.Email))
                {
                    await SendTemplatedEmail(conn, (string)user.Email, (string)(user.Name ?? "User"), req);
                }

                // ── sms ──
                if (req.SendSms && smsOn && !eventMuted && !string.IsNullOrWhiteSpace((string?)user.MobileNumber))
                {
                    var text = req.SmsText ?? req.Title;
                    await _sms.SendAsync((string)user.MobileNumber, text);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "NotifyAsync failed for user {Uid} event {Ev}", req.UserID, req.EventCode);
            }
        }

        private async Task SendTemplatedEmail(IDbConnection conn, string toEmail, string name, NotifyRequest req)
        {
            try
            {
                string subject = req.Title;
                string html = req.Body ?? req.Title;

                if (!string.IsNullOrWhiteSpace(req.EmailTemplateCode))
                {
                    var tpl = await conn.QuerySingleOrDefaultAsync(
                        "SP_EmailTemplate_GetByCode", new { p_Code = req.EmailTemplateCode },
                        commandType: CommandType.StoredProcedure);
                    if (tpl != null)
                    {
                        subject = Merge((string)tpl.Subject, req.Vars, name);
                        html    = Merge((string)tpl.HtmlBody, req.Vars, name);
                    }
                }

                var s = _config.GetSection("EmailSettings");
                using var client = new SmtpClient(s["SmtpHost"], int.Parse(s["SmtpPort"] ?? "587"))
                {
                    UseDefaultCredentials = false,
                    Credentials = new NetworkCredential(s["Username"], s["Password"]),
                    EnableSsl = bool.Parse(s["EnableSsl"] ?? "true"),
                    DeliveryMethod = SmtpDeliveryMethod.Network
                };
                using var msg = new MailMessage
                {
                    From = new MailAddress(s["SenderEmail"], s["SenderName"] ?? "ProximaLMS"),
                    Subject = subject,
                    Body = html,
                    IsBodyHtml = true
                };
                msg.To.Add(toEmail);
                await client.SendMailAsync(msg);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Templated email failed to {To}", toEmail);
            }
        }

        // {{Var}} replacement; always provides {{Name}}, {{Year}}, {{PortalUrl}}
        private string Merge(string template, Dictionary<string, string>? vars, string name)
        {
            var dict = vars != null
                ? new Dictionary<string, string>(vars, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!dict.ContainsKey("Name"))      dict["Name"]      = name;
            if (!dict.ContainsKey("Year"))      dict["Year"]      = DateTime.Now.Year.ToString();
            if (!dict.ContainsKey("PortalUrl")) dict["PortalUrl"] = (_config["PortalUrl"] ?? "").TrimEnd('/');
            if (!dict.ContainsKey("LoginLink")) dict["LoginLink"] = (_config["PortalUrl"] ?? "").TrimEnd('/');

            return Regex.Replace(template ?? "", @"\{\{\s*([A-Za-z0-9_]+)\s*\}\}", m =>
            {
                var key = m.Groups[1].Value;
                return dict.TryGetValue(key, out var v) ? v : "";
            });
        }
    }
}

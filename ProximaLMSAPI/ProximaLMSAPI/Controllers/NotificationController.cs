// ============================================================
// ProximaLMSAPI/Controllers/NotificationController.cs
// ------------------------------------------------------------
// Bell endpoints (list / unread / mark read), per-user
// preferences, and admin broadcast (all or by role).
//
// DI: INotificationService, INotificationPush.
// SPs: SP_Notif_* / SP_NotifPref_* (see Notification_DB.sql).
// ============================================================
using Dapper;
using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
using ProximaLMSAPI.Hubs;
using System.Data;

namespace ProximaLMSAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class NotificationController : ControllerBase
    {
        private readonly IConfiguration _config;
        private readonly INotificationPush _push;
        private readonly ILogger<NotificationController> _logger;

        public NotificationController(IConfiguration config, INotificationPush push,
                                      ILogger<NotificationController> logger)
        {
            _config = config; _push = push; _logger = logger;
        }

        private IDbConnection Conn() => new MySqlConnection(_config.GetConnectionString("ConnectionString"));

        // GET api/notification/list/{userId}?limit=20
        [HttpGet("list/{userId:int}")]
        public async Task<IActionResult> List(int userId, [FromQuery] int limit = 20)
        {
            using var conn = Conn();
            var rows = await conn.QueryAsync("SP_Notif_List",
                new { p_UserID = userId, p_Limit = limit }, commandType: CommandType.StoredProcedure);
            return Ok(new { success = true, data = rows });
        }

        // GET api/notification/unread/{userId}
        [HttpGet("unread/{userId:int}")]
        public async Task<IActionResult> Unread(int userId)
        {
            using var conn = Conn();
            var count = await conn.ExecuteScalarAsync<int>("SP_Notif_UnreadCount",
                new { p_UserID = userId }, commandType: CommandType.StoredProcedure);
            return Ok(new { success = true, unread = count });
        }

        // POST api/notification/read   Body: { NotificationID, UserID }
        [HttpPost("read")]
        public async Task<IActionResult> MarkRead([FromBody] ReadRequest req)
        {
            using var conn = Conn();
            await conn.ExecuteAsync("SP_Notif_MarkRead",
                new { p_NotificationID = req.NotificationID, p_UserID = req.UserID },
                commandType: CommandType.StoredProcedure);
            return Ok(new { success = true });
        }

        // POST api/notification/read-all/{userId}
        [HttpPost("read-all/{userId:int}")]
        public async Task<IActionResult> MarkAllRead(int userId)
        {
            using var conn = Conn();
            await conn.ExecuteAsync("SP_Notif_MarkAllRead",
                new { p_UserID = userId }, commandType: CommandType.StoredProcedure);
            return Ok(new { success = true });
        }

        // GET api/notification/preferences/{userId}
        [HttpGet("preferences/{userId:int}")]
        public async Task<IActionResult> GetPrefs(int userId)
        {
            using var conn = Conn();
            var row = await conn.QuerySingleOrDefaultAsync("SP_NotifPref_Get",
                new { p_UserID = userId }, commandType: CommandType.StoredProcedure);
            return Ok(new { success = true, data = row });
        }

        // POST api/notification/preferences  Body: { UserID, EmailEnabled, SmsEnabled, InAppEnabled, MutedEvents }
        [HttpPost("preferences")]
        public async Task<IActionResult> SavePrefs([FromBody] PrefRequest req)
        {
            using var conn = Conn();
            var p = new DynamicParameters();
            p.Add("p_UserID", req.UserID);
            p.Add("p_EmailEnabled", req.EmailEnabled ? 1 : 0);
            p.Add("p_SmsEnabled", req.SmsEnabled ? 1 : 0);
            p.Add("p_InAppEnabled", req.InAppEnabled ? 1 : 0);
            p.Add("p_MutedEvents", req.MutedEvents ?? "");
            await conn.ExecuteAsync("SP_NotifPref_Save", p, commandType: CommandType.StoredProcedure);
            return Ok(new { success = true });
        }

        // POST api/notification/broadcast (admin)
        // Body: { RoleFilter?, Title, Body?, LinkUrl? }   RoleFilter "" = everyone
        [HttpPost("broadcast")]
        public async Task<IActionResult> Broadcast([FromBody] BroadcastRequest req)
        {
            if (req == null || string.IsNullOrWhiteSpace(req.Title))
                return BadRequest(new { success = false, message = "Title is required." });
            try
            {
                using var conn = Conn();
                var recipients = (await conn.QueryAsync<int>("SP_Notif_Broadcast",
                    new
                    {
                        p_RoleFilter = req.RoleFilter ?? "",
                        p_Title = req.Title,
                        p_Body = req.Body ?? "",
                        p_LinkUrl = req.LinkUrl ?? ""
                    },
                    commandType: CommandType.StoredProcedure)).ToList();

                // SignalR fan-out (best-effort)
                foreach (var uid in recipients)
                {
                    try
                    {
                        await _push.PushToUser(uid, new
                        {
                            title = req.Title, body = req.Body, link = req.LinkUrl,
                            icon = "fa-solid fa-bullhorn", unread = -1   // -1 = client should refetch count
                        });
                    }
                    catch { /* ignore individual push failures */ }
                }

                return Ok(new { success = true, recipients = recipients.Count });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Broadcast failed");
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        public class ReadRequest { public int NotificationID { get; set; } public int UserID { get; set; } }
        public class PrefRequest
        {
            public int     UserID       { get; set; }
            public bool    EmailEnabled { get; set; } = true;
            public bool    SmsEnabled   { get; set; } = true;
            public bool    InAppEnabled { get; set; } = true;
            public string? MutedEvents  { get; set; }
        }
        public class BroadcastRequest
        {
            public string? RoleFilter { get; set; }   // "" = all, "3" = students, etc.
            public string  Title      { get; set; } = "";
            public string? Body       { get; set; }
            public string? LinkUrl    { get; set; }
        }
    }
}

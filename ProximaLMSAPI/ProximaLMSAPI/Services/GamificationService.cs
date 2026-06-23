using Dapper;
using MySql.Data.MySqlClient;
using System.Data;

namespace ProximaLMSAPI.Services
{
    public interface IGamificationService
    {
        Task<int> AwardAsync(int studentId, string actionCode,
                             string refType = "", string refId = "", string note = "");

        Task<(int current, int longest)> TouchStreakAsync(int studentId);

        Task EvaluateAndNotifyBadgesAsync(int studentId);

        Task AwardAndEvaluateAsync(int studentId, string actionCode,
                                   string refType = "", string refId = "", string note = "");

        // Login convenience: touch streak, award DAILY_LOGIN, award
        // STREAK_7DAY at a 7-day streak, then evaluate badges.
        Task HandleLoginAsync(int studentId);
    }

    public class GamificationService : IGamificationService
    {
        private readonly IConfiguration _config;
        private readonly INotificationService _notifier;
        private readonly ILogger<GamificationService> _logger;

        public GamificationService(IConfiguration config,
                                   INotificationService notifier,
                                   ILogger<GamificationService> logger)
        {
            _config = config;
            _notifier = notifier;
            _logger = logger;
        }

        private IDbConnection Conn()
            => new MySqlConnection(_config.GetConnectionString("ConnectionString"));

        // ── points ────────────────────────────────────────────
        public async Task<int> AwardAsync(int studentId, string actionCode,
                                          string refType = "", string refId = "", string note = "")
        {
            if (studentId <= 0 || string.IsNullOrWhiteSpace(actionCode)) return 0;
            try
            {
                using var conn = Conn();
                var p = new DynamicParameters();
                p.Add("p_StudentID", studentId);
                p.Add("p_ActionCode", actionCode);
                p.Add("p_RefType", refType ?? "");
                p.Add("p_RefID", refId ?? "");
                p.Add("p_Note", note ?? "");
                p.Add("p_Awarded", dbType: DbType.Int32, direction: ParameterDirection.Output);
                await conn.ExecuteAsync("SP_Points_Award", p, commandType: CommandType.StoredProcedure);
                return p.Get<int>("p_Awarded");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AwardAsync failed: {Action} for {Student}", actionCode, studentId);
                return 0;
            }
        }

        // ── streak ────────────────────────────────────────────
        public async Task<(int current, int longest)> TouchStreakAsync(int studentId)
        {
            if (studentId <= 0) return (0, 0);
            try
            {
                using var conn = Conn();
                var p = new DynamicParameters();
                p.Add("p_StudentID", studentId);
                p.Add("p_Current", dbType: DbType.Int32, direction: ParameterDirection.Output);
                p.Add("p_Longest", dbType: DbType.Int32, direction: ParameterDirection.Output);
                await conn.ExecuteAsync("SP_Streak_Touch", p, commandType: CommandType.StoredProcedure);
                return (p.Get<int>("p_Current"), p.Get<int>("p_Longest"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "TouchStreakAsync failed for {Student}", studentId);
                return (0, 0);
            }
        }

        // ── badges ────────────────────────────────────────────
        public async Task EvaluateAndNotifyBadgesAsync(int studentId)
        {
            if (studentId <= 0) return;
            try
            {
                using var conn = Conn();

                // SP returns only unseen (Seen = 0) badges, i.e. freshly earned.
                var newBadges = (await conn.QueryAsync("SP_Badge_Evaluate",
                    new { p_StudentID = studentId },
                    commandType: CommandType.StoredProcedure)).ToList();

                if (newBadges.Count == 0) return;

                foreach (var b in newBadges)
                {
                    if (b is not IDictionary<string, object> d) continue;

                    string badgeName = d.TryGetValue("BadgeName", out var bn) && bn != null
                        ? bn.ToString() : "a badge";
                    string icon = d.TryGetValue("IconClass", out var ic) && ic != null
                        ? ic.ToString() : "fa-solid fa-medal";

                    await _notifier.NotifyAsync(new NotifyRequest
                    {
                        UserID = studentId,
                        EventCode = "BADGE",
                        Title = "Badge unlocked!",
                        Body = $"You earned the <strong>{badgeName}</strong> badge.",
                        Icon = string.IsNullOrWhiteSpace(icon) ? "fa-solid fa-medal" : icon,
                        SendInApp = true,
                        SendEmail = true,
                        EmailTemplateCode = "BADGE_EARNED",
                        Vars = new Dictionary<string, string>
                        {
                            ["BadgeName"] = badgeName
                        }
                    });
                }

                // mark seen so the same badge is never re-notified on the next event
                await conn.ExecuteAsync("SP_Badge_MarkSeen",
                    new { p_StudentID = studentId },
                    commandType: CommandType.StoredProcedure);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "EvaluateAndNotifyBadgesAsync failed for {Student}", studentId);
            }
        }

        // ── composite helpers ─────────────────────────────────
        public async Task AwardAndEvaluateAsync(int studentId, string actionCode,
                                                string refType = "", string refId = "", string note = "")
        {
            await AwardAsync(studentId, actionCode, refType, refId, note);
            await EvaluateAndNotifyBadgesAsync(studentId);
        }

        public async Task HandleLoginAsync(int studentId)
        {
            if (studentId <= 0) return;

            var (current, _) = await TouchStreakAsync(studentId);

            // once-per-UTC-day login point (dedup via date RefID)
            await AwardAsync(studentId, "DAILY_LOGIN", "Day",
                DateTime.UtcNow.ToString("yyyyMMdd"), "Daily login");

            // 7-day streak milestone (dedup per streak run via the run length)
            if (current >= 7)
            {
                await AwardAsync(studentId, "STREAK_7DAY", "Streak",
                    $"{studentId}-{current}", "7-day login streak");
            }

            await EvaluateAndNotifyBadgesAsync(studentId);
        }
    }
}
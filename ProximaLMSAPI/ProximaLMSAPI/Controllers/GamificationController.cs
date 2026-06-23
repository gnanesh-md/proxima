// ============================================================
// ProximaLMSAPI/Controllers/GamificationController.cs
// ------------------------------------------------------------
// Module 11 — the earning engine's API surface.
//
// Student reads:  summary, points history, leaderboard, badges,
//                 unseen badge notifications, mark-seen.
// Award hooks:    award (generic), streak-touch, evaluate-badges
//                 — called by the other controllers on the
//                 relevant events (see the wiring guide).
//
// SPs: SP_Points_Award, SP_Streak_Touch, SP_Badge_Evaluate,
//      SP_Badge_MarkSeen, SP_Gamif_* (see Gamification_DB.sql).
// ============================================================
using Dapper;
using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
using System.Data;

namespace ProximaLMSAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class GamificationController : ControllerBase
    {
        private readonly IConfiguration _config;
        private readonly ILogger<GamificationController> _logger;

        public GamificationController(IConfiguration config, ILogger<GamificationController> logger)
        {
            _config = config; _logger = logger;
        }

        private IDbConnection Conn() => new MySqlConnection(_config.GetConnectionString("ConnectionString"));

        // ── STUDENT READS ────────────────────────────────────

        // GET api/gamification/summary/{studentId}
        [HttpGet("summary/{studentId:int}")]
        public async Task<IActionResult> Summary(int studentId)
        {
            using var conn = Conn();
            var row = await conn.QuerySingleOrDefaultAsync("SP_Gamif_Summary",
                new { p_StudentID = studentId }, commandType: CommandType.StoredProcedure);
            return Ok(new { success = true, data = row });
        }

        // GET api/gamification/history/{studentId}
        [HttpGet("history/{studentId:int}")]
        public async Task<IActionResult> History(int studentId)
        {
            using var conn = Conn();
            var rows = await conn.QueryAsync("SP_Gamif_PointsHistory",
                new { p_StudentID = studentId }, commandType: CommandType.StoredProcedure);
            return Ok(new { success = true, data = rows });
        }

        // GET api/gamification/leaderboard/{studentId}?topN=50
        [HttpGet("leaderboard/{studentId:int}")]
        public async Task<IActionResult> Leaderboard(int studentId, [FromQuery] int topN = 50)
        {
            using var conn = Conn();
            using var multi = await conn.QueryMultipleAsync("SP_Gamif_Leaderboard",
                new { p_StudentID = studentId, p_TopN = topN },
                commandType: CommandType.StoredProcedure);
            var top = (await multi.ReadAsync()).ToList();
            var me  = await multi.ReadFirstOrDefaultAsync();
            return Ok(new { success = true, top, me });
        }

        // GET api/gamification/badges/{studentId}
        [HttpGet("badges/{studentId:int}")]
        public async Task<IActionResult> Badges(int studentId)
        {
            using var conn = Conn();
            var rows = await conn.QueryAsync("SP_Gamif_MyBadges",
                new { p_StudentID = studentId }, commandType: CommandType.StoredProcedure);
            return Ok(new { success = true, data = rows });
        }

        // GET api/gamification/notifications/{studentId}  — unseen badges
        [HttpGet("notifications/{studentId:int}")]
        public async Task<IActionResult> Notifications(int studentId)
        {
            using var conn = Conn();
            var rows = await conn.QueryAsync("SP_Badge_Evaluate",
                new { p_StudentID = studentId }, commandType: CommandType.StoredProcedure);
            return Ok(new { success = true, data = rows });
        }

        // POST api/gamification/notifications/{studentId}/seen
        [HttpPost("notifications/{studentId:int}/seen")]
        public async Task<IActionResult> MarkSeen(int studentId)
        {
            using var conn = Conn();
            await conn.ExecuteAsync("SP_Badge_MarkSeen",
                new { p_StudentID = studentId }, commandType: CommandType.StoredProcedure);
            return Ok(new { success = true });
        }

        // ── AWARD HOOKS (called by other controllers) ────────

        // POST api/gamification/award
        // Body: { StudentID, ActionCode, RefType?, RefID?, Note? }
        // Idempotent on (StudentID, ActionCode, RefID). Re-evaluates badges.
        [HttpPost("award")]
        public async Task<IActionResult> Award([FromBody] AwardRequest req)
        {
            if (req == null || req.StudentID <= 0 || string.IsNullOrWhiteSpace(req.ActionCode))
                return BadRequest(new { success = false, message = "StudentID and ActionCode required." });
            try
            {
                using var conn = Conn();
                var p = new DynamicParameters();
                p.Add("p_StudentID", req.StudentID);
                p.Add("p_ActionCode", req.ActionCode);
                p.Add("p_RefType", req.RefType ?? "");
                p.Add("p_RefID", req.RefID ?? "");
                p.Add("p_Note", req.Note ?? "");
                p.Add("p_Awarded", dbType: DbType.Int32, direction: ParameterDirection.Output);
                await conn.ExecuteAsync("SP_Points_Award", p, commandType: CommandType.StoredProcedure);

                int awarded = p.Get<int>("p_Awarded");

                // re-evaluate badges (returns newly unseen ones)
                var newBadges = await conn.QueryAsync("SP_Badge_Evaluate",
                    new { p_StudentID = req.StudentID }, commandType: CommandType.StoredProcedure);

                return Ok(new { success = true, awarded, newBadges });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Award failed: {Action} for {Student}", req.ActionCode, req.StudentID);
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        // POST api/gamification/streak/{studentId}  — call on login
        [HttpPost("streak/{studentId:int}")]
        public async Task<IActionResult> Streak(int studentId)
        {
            try
            {
                using var conn = Conn();
                var p = new DynamicParameters();
                p.Add("p_StudentID", studentId);
                p.Add("p_Current", dbType: DbType.Int32, direction: ParameterDirection.Output);
                p.Add("p_Longest", dbType: DbType.Int32, direction: ParameterDirection.Output);
                await conn.ExecuteAsync("SP_Streak_Touch", p, commandType: CommandType.StoredProcedure);

                await conn.QueryAsync("SP_Badge_Evaluate",
                    new { p_StudentID = studentId }, commandType: CommandType.StoredProcedure);

                return Ok(new { success = true, current = p.Get<int>("p_Current"), longest = p.Get<int>("p_Longest") });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Streak touch failed for {Student}", studentId);
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        public class AwardRequest
        {
            public int     StudentID  { get; set; }
            public string  ActionCode { get; set; } = "";
            public string? RefType    { get; set; }
            public string? RefID      { get; set; }
            public string? Note       { get; set; }
        }
    }
}

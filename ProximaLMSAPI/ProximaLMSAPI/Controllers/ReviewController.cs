// ============================================================
// ProximaLMSAPI/Controllers/ReviewController.cs
// ------------------------------------------------------------
// Course reviews & star ratings — Module 08: moderation queue.
//
// PUBLIC endpoints (only APPROVED reviews):
//   POST /api/review/save             → add / update (lands PENDING)
//   GET  /api/review/course/{id}      → APPROVED reviews for a course
//   GET  /api/review/mine/{cid}/{sid} → the student's own review (any status)
//   GET  /api/review/summary          → avg rating + count (APPROVED only)
//
// ADMIN moderation endpoints:
//   GET  /api/review/queue?status=PENDING|APPROVED|REJECTED|ALL
//   GET  /api/review/queue/counts
//   POST /api/review/moderate         → approve / reject one
//   POST /api/review/bulk-moderate    → approve / reject many
// ============================================================
using Dapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using MySql.Data.MySqlClient;
using ProximaLMSAPI.Services;
using System.Data;

namespace ProximaLMSAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ReviewController : ControllerBase
    {
        private readonly IConfiguration _config;
        private readonly ILogger<ReviewController> _logger;
        private readonly IServiceScopeFactory _scopeFactory;

        public ReviewController(IConfiguration config, ILogger<ReviewController> logger,
                                IServiceScopeFactory scopeFactory)
        {
            _config = config;
            _logger = logger;
            _scopeFactory = scopeFactory;
        }

        private IDbConnection CreateConn()
            => new MySqlConnection(_config.GetConnectionString("ConnectionString"));


        // ════════════════════════════════════════════════════════
        // POST  api/review/save
        // Body: { CourseID, StudentID, Rating, ReviewText }
        // The review lands as PENDING — SP_Review_Save handles it.
        // ════════════════════════════════════════════════════════
        [HttpPost("save")]
        public async Task<IActionResult> Save([FromBody] SaveReviewApiRequest req)
        {
            if (req == null || req.CourseID <= 0 || req.StudentID <= 0)
                return BadRequest(new { success = false, message = "Invalid request." });

            if (req.Rating < 1 || req.Rating > 5)
                return BadRequest(new { success = false, message = "Rating must be 1-5." });

            try
            {
                using var conn = CreateConn();
                conn.Open();

                var p = new DynamicParameters();
                p.Add("p_CourseID", req.CourseID);
                p.Add("p_StudentID", req.StudentID);
                p.Add("p_Rating", req.Rating);
                p.Add("p_ReviewText", string.IsNullOrWhiteSpace(req.ReviewText)
                                          ? null
                                          : req.ReviewText.Trim());
                p.Add("p_ResultCode", dbType: DbType.Int32, direction: ParameterDirection.Output);
                p.Add("p_Message", dbType: DbType.String, size: 500, direction: ParameterDirection.Output);

                await conn.ExecuteAsync("SP_Review_Save", p,
                    commandType: CommandType.StoredProcedure);

                int code = p.Get<int>("p_ResultCode");
                string msg = p.Get<string>("p_Message") ?? "";

                return code == 1
                    ? Ok(new { success = true, message = msg })
                    : BadRequest(new { success = false, message = msg });
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "Error saving review");
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }


        // ════════════════════════════════════════════════════════
        // GET  api/review/course/{courseId}
        // PUBLIC — returns APPROVED reviews only.
        // ════════════════════════════════════════════════════════
        [HttpGet("course/{courseId:int}")]
        public async Task<IActionResult> GetForCourse(int courseId)
        {
            if (courseId <= 0)
                return Ok(System.Array.Empty<object>());

            try
            {
                using var conn = CreateConn();
                var rows = await conn.QueryAsync(
                    "SP_Review_GetForCourse",
                    new { p_CourseID = courseId },
                    commandType: CommandType.StoredProcedure);

                return Ok(rows.ToList());
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "Error loading reviews");
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }


        // ════════════════════════════════════════════════════════
        // GET  api/review/mine/{courseId}/{studentId}
        // Returns the student's OWN review whatever its status,
        // so the course page can show "awaiting approval" etc.
        // ════════════════════════════════════════════════════════
        [HttpGet("mine/{courseId:int}/{studentId:int}")]
        public async Task<IActionResult> GetMine(int courseId, int studentId)
        {
            if (courseId <= 0 || studentId <= 0)
                return Ok((object?)null);

            try
            {
                using var conn = CreateConn();
                var row = await conn.QueryFirstOrDefaultAsync(
                    "SP_Review_GetMine",
                    new { p_CourseID = courseId, p_StudentID = studentId },
                    commandType: CommandType.StoredProcedure);

                return Ok(row);  // null if the student has no review
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "Error loading own review");
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }


        // ════════════════════════════════════════════════════════
        // GET  api/review/summary
        // Avg rating + count — APPROVED reviews only.
        // ════════════════════════════════════════════════════════
        [HttpGet("summary")]
        public async Task<IActionResult> GetSummary()
        {
            try
            {
                using var conn = CreateConn();
                var rows = await conn.QueryAsync(
                    "SP_Review_GetSummary",
                    commandType: CommandType.StoredProcedure);

                return Ok(rows.ToList());
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "Error loading review summary");
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }


        // ════════════════════════════════════════════════════════
        // GET  api/review/queue?status=PENDING
        // ADMIN moderation queue.
        // ════════════════════════════════════════════════════════
        [HttpGet("queue")]
        public async Task<IActionResult> GetQueue([FromQuery] string status = "PENDING")
        {
            var allowed = new[] { "PENDING", "APPROVED", "REJECTED", "ALL" };
            string filter = (status ?? "PENDING").Trim().ToUpper();
            if (!allowed.Contains(filter)) filter = "PENDING";

            try
            {
                using var conn = CreateConn();
                var rows = await conn.QueryAsync(
                    "SP_Review_GetQueue",
                    new { p_Status = filter },
                    commandType: CommandType.StoredProcedure);

                return Ok(rows.ToList());
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "Error loading moderation queue");
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }


        // ════════════════════════════════════════════════════════
        // GET  api/review/queue/counts
        // ════════════════════════════════════════════════════════
        [HttpGet("queue/counts")]
        public async Task<IActionResult> GetQueueCounts()
        {
            try
            {
                using var conn = CreateConn();
                var row = await conn.QueryFirstOrDefaultAsync(
                    "SP_Review_GetQueueCounts",
                    commandType: CommandType.StoredProcedure);

                return Ok(row ?? new { PendingCount = 0, ApprovedCount = 0, RejectedCount = 0, TotalCount = 0 });
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "Error loading queue counts");
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }


        // ════════════════════════════════════════════════════════
        // POST  api/review/moderate
        // Body: { ReviewID, NewStatus, ModeratedBy, Note }
        // ════════════════════════════════════════════════════════
        [HttpPost("moderate")]
        public async Task<IActionResult> Moderate([FromBody] ModerateReviewApiRequest req)
        {
            if (req == null || req.ReviewID <= 0)
                return BadRequest(new { success = false, message = "Invalid request." });

            string status = (req.NewStatus ?? "").Trim().ToUpper();
            if (status != "APPROVED" && status != "REJECTED")
                return BadRequest(new { success = false, message = "Status must be APPROVED or REJECTED." });

            try
            {
                using var conn = CreateConn();
                conn.Open();

                var p = new DynamicParameters();
                p.Add("p_ReviewID", req.ReviewID);
                p.Add("p_NewStatus", status);
                p.Add("p_ModeratedBy", string.IsNullOrWhiteSpace(req.ModeratedBy) ? "Admin" : req.ModeratedBy.Trim());
                p.Add("p_Note", string.IsNullOrWhiteSpace(req.Note) ? null : req.Note.Trim());
                p.Add("p_ResultCode", dbType: DbType.Int32, direction: ParameterDirection.Output);
                p.Add("p_Message", dbType: DbType.String, size: 500, direction: ParameterDirection.Output);

                await conn.ExecuteAsync("SP_Review_Moderate", p,
                    commandType: CommandType.StoredProcedure);

                int code = p.Get<int>("p_ResultCode");
                string msg = p.Get<string>("p_Message") ?? "";

                // award WRITE_REVIEW points only when the review is APPROVED
                if (code == 1 && status == "APPROVED")
                {
                    int studentId = await conn.ExecuteScalarAsync<int?>(
                        "SELECT StudentID FROM TblCourseReview WHERE ReviewID = @id",
                        new { id = req.ReviewID }) ?? 0;

                    if (studentId > 0)
                        FireReviewPoints(new List<(int, int)> { (req.ReviewID, studentId) });
                }

                return code == 1
                    ? Ok(new { success = true, message = msg })
                    : BadRequest(new { success = false, message = msg });
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "Error moderating review {Id}", req.ReviewID);
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }


        // ════════════════════════════════════════════════════════
        // POST  api/review/bulk-moderate
        // Body: { ReviewIDs: [12,15,18], NewStatus, ModeratedBy }
        // ════════════════════════════════════════════════════════
        [HttpPost("bulk-moderate")]
        public async Task<IActionResult> BulkModerate([FromBody] BulkModerateApiRequest req)
        {
            if (req == null || req.ReviewIDs == null || req.ReviewIDs.Count == 0)
                return BadRequest(new { success = false, message = "No reviews selected." });

            string status = (req.NewStatus ?? "").Trim().ToUpper();
            if (status != "APPROVED" && status != "REJECTED")
                return BadRequest(new { success = false, message = "Status must be APPROVED or REJECTED." });

            // Build a safe CSV of integers only.
            string csv = string.Join(",", req.ReviewIDs.Where(id => id > 0).Distinct());
            if (csv.Length == 0)
                return BadRequest(new { success = false, message = "No valid review ids." });

            try
            {
                using var conn = CreateConn();
                conn.Open();

                var p = new DynamicParameters();
                p.Add("p_ReviewIDs", csv);
                p.Add("p_NewStatus", status);
                p.Add("p_ModeratedBy", string.IsNullOrWhiteSpace(req.ModeratedBy) ? "Admin" : req.ModeratedBy.Trim());
                p.Add("p_ResultCode", dbType: DbType.Int32, direction: ParameterDirection.Output);
                p.Add("p_Message", dbType: DbType.String, size: 500, direction: ParameterDirection.Output);

                await conn.ExecuteAsync("SP_Review_BulkModerate", p,
                    commandType: CommandType.StoredProcedure);

                int code = p.Get<int>("p_ResultCode");
                string msg = p.Get<string>("p_Message") ?? "";

                // award WRITE_REVIEW points for each review now APPROVED
                if (code == 1 && status == "APPROVED")
                {
                    var approved = (await conn.QueryAsync(
                        "SELECT ReviewID, StudentID FROM TblCourseReview " +
                        "WHERE FIND_IN_SET(ReviewID, @csv) > 0 AND Status = 'APPROVED'",
                        new { csv })).ToList();

                    var items = new List<(int, int)>();
                    foreach (var r in approved)
                    {
                        if (r is not IDictionary<string, object> d) continue;
                        int rid = d.TryGetValue("ReviewID", out var rv) && rv != null ? Convert.ToInt32(rv) : 0;
                        int sid = d.TryGetValue("StudentID", out var sv) && sv != null ? Convert.ToInt32(sv) : 0;
                        if (rid > 0 && sid > 0) items.Add((rid, sid));
                    }
                    FireReviewPoints(items);
                }

                return code == 1
                    ? Ok(new { success = true, message = msg })
                    : BadRequest(new { success = false, message = msg });
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "Error bulk-moderating reviews");
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }


        // ────────────────────────────────────────────────────────
        // Award WRITE_REVIEW points (per approved review) and evaluate
        // badges, in a background DI scope. Dedup-safe per ReviewID, so
        // re-approving the same review never double-credits.
        // ────────────────────────────────────────────────────────
        private void FireReviewPoints(List<(int reviewId, int studentId)> items)
        {
            if (items == null || items.Count == 0) return;

            _ = Task.Run(async () =>
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var gamif = scope.ServiceProvider.GetRequiredService<IGamificationService>();

                    foreach (var (reviewId, studentId) in items)
                    {
                        if (studentId <= 0) continue;
                        await gamif.AwardAsync(studentId, "WRITE_REVIEW",
                            "Review", reviewId.ToString(), "Approved review");
                        await gamif.EvaluateAndNotifyBadgesAsync(studentId);
                    }
                }
                catch (System.Exception ex)
                {
                    _logger.LogError(ex, "Review points award failed (non-fatal).");
                }
            });
        }
    }


    // ── Request DTOs ──────────────────────────────────────────
    public class SaveReviewApiRequest
    {
        public int CourseID { get; set; }
        public int StudentID { get; set; }
        public int Rating { get; set; }
        public string ReviewText { get; set; } = "";
    }

    public class ModerateReviewApiRequest
    {
        public int ReviewID { get; set; }
        public string NewStatus { get; set; } = "";   // APPROVED | REJECTED
        public string ModeratedBy { get; set; } = "";
        public string Note { get; set; } = "";
    }

    public class BulkModerateApiRequest
    {
        public System.Collections.Generic.List<int> ReviewIDs { get; set; } = new();
        public string NewStatus { get; set; } = "";
        public string ModeratedBy { get; set; } = "";
    }
}

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
    public class ProgressController : ControllerBase
    {
        private readonly IConfiguration _config;
        private readonly ILogger<ProgressController> _logger;
        private readonly IServiceScopeFactory _scopeFactory;

        public ProgressController(IConfiguration config, ILogger<ProgressController> logger,
                                  IServiceScopeFactory scopeFactory)
        {
            _config = config;
            _logger = logger;
            _scopeFactory = scopeFactory;
        }

        private IDbConnection CreateConn()
            => new MySqlConnection(_config.GetConnectionString("ConnectionString"));


        // ════════════════════════════════════════════════════════
        // POST  api/progress/mark
        // Body: { StudentID, CourseID, ContentID, Completed }
        // Manual "Mark Complete" override — keeps any saved position.
        // ════════════════════════════════════════════════════════
        [HttpPost("mark")]
        public async Task<IActionResult> Mark([FromBody] MarkProgressApiRequest req)
        {
            if (req == null || req.StudentID <= 0 || req.CourseID <= 0 || req.ContentID <= 0)
                return BadRequest(new { success = false, message = "Invalid request." });

            try
            {
                using var conn = CreateConn();
                conn.Open();

                var p = new DynamicParameters();
                p.Add("p_StudentID", req.StudentID);
                p.Add("p_CourseID", req.CourseID);
                p.Add("p_ContentID", req.ContentID);
                p.Add("p_Completed", req.Completed ? 1 : 0);
                p.Add("p_Total", dbType: DbType.Int32, direction: ParameterDirection.Output);
                p.Add("p_Done", dbType: DbType.Int32, direction: ParameterDirection.Output);
                p.Add("p_ResultCode", dbType: DbType.Int32, direction: ParameterDirection.Output);
                p.Add("p_Message", dbType: DbType.String, size: 500, direction: ParameterDirection.Output);

                await conn.ExecuteAsync("SP_Progress_MarkContent", p,
                    commandType: CommandType.StoredProcedure);

                int code = p.Get<int>("p_ResultCode");
                int total = p.Get<int>("p_Total");
                int done = p.Get<int>("p_Done");
                string msg = p.Get<string>("p_Message") ?? "";

                if (code != 1)
                    return BadRequest(new { success = false, message = msg });

                int percent = total > 0
                    ? (int)System.Math.Round(done * 100.0 / total)
                    : 0;

                // gamification: lesson completion (when marking done) + course completion
                bool courseComplete = total > 0 && done >= total;
                FireProgressGamification(req.StudentID, req.CourseID, req.ContentID,
                                         lessonComplete: req.Completed, courseComplete: courseComplete);

                return Ok(new
                {
                    success = true,
                    completed = req.Completed,
                    completedContents = done,
                    totalContents = total,
                    percent = percent,
                    message = msg
                });
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "Error marking progress");
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }


        // ════════════════════════════════════════════════════════
        // POST  api/progress/position
        // Body: { StudentID, CourseID, ContentID, Position, Duration }
        // Saves the resume point. The SP auto-marks the lesson
        // complete once Position >= 90% of Duration.
        // Returns: { success, completed, completedContents,
        //            totalContents, percent }
        // ════════════════════════════════════════════════════════
        [HttpPost("position")]
        public async Task<IActionResult> SavePosition([FromBody] SavePositionApiRequest req)
        {
            if (req == null || req.StudentID <= 0 || req.CourseID <= 0 || req.ContentID <= 0)
                return BadRequest(new { success = false, message = "Invalid request." });

            try
            {
                using var conn = CreateConn();
                conn.Open();

                var p = new DynamicParameters();
                p.Add("p_StudentID", req.StudentID);
                p.Add("p_CourseID", req.CourseID);
                p.Add("p_ContentID", req.ContentID);
                p.Add("p_Position", req.Position < 0 ? 0 : req.Position);
                p.Add("p_Duration", req.Duration < 0 ? 0 : req.Duration);
                p.Add("p_Total", dbType: DbType.Int32, direction: ParameterDirection.Output);
                p.Add("p_Done", dbType: DbType.Int32, direction: ParameterDirection.Output);
                p.Add("p_IsCompleted", dbType: DbType.Int32, direction: ParameterDirection.Output);
                p.Add("p_ResultCode", dbType: DbType.Int32, direction: ParameterDirection.Output);
                p.Add("p_Message", dbType: DbType.String, size: 500, direction: ParameterDirection.Output);

                await conn.ExecuteAsync("SP_Progress_SavePosition", p,
                    commandType: CommandType.StoredProcedure);

                int code = p.Get<int>("p_ResultCode");
                int total = p.Get<int>("p_Total");
                int done = p.Get<int>("p_Done");
                int completed = p.Get<int>("p_IsCompleted");
                string msg = p.Get<string>("p_Message") ?? "";

                if (code != 1)
                    return BadRequest(new { success = false, message = msg });

                int percent = total > 0
                    ? (int)System.Math.Round(done * 100.0 / total)
                    : 0;

                // gamification: only when the SP just auto-completed the lesson,
                // or when the course as a whole has reached 100%
                bool courseComplete = total > 0 && done >= total;
                if (completed == 1 || courseComplete)
                {
                    FireProgressGamification(req.StudentID, req.CourseID, req.ContentID,
                                             lessonComplete: completed == 1, courseComplete: courseComplete);
                }

                return Ok(new
                {
                    success = true,
                    completed = completed == 1,
                    completedContents = done,
                    totalContents = total,
                    percent = percent
                });
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "Error saving video position");
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }


        // ════════════════════════════════════════════════════════
        // GET  api/progress/course/{studentId}/{courseId}
        // Returns: [ { ContentID, LastPositionSeconds, IsCompleted }, ... ]
        // ════════════════════════════════════════════════════════
        [HttpGet("course/{studentId:int}/{courseId:int}")]
        public async Task<IActionResult> GetForCourse(int studentId, int courseId)
        {
            if (studentId <= 0 || courseId <= 0)
                return Ok(System.Array.Empty<object>());

            try
            {
                using var conn = CreateConn();
                var rows = await conn.QueryAsync<CourseProgressApiRow>(
                    "SP_Progress_GetForCourse",
                    new { p_StudentID = studentId, p_CourseID = courseId },
                    commandType: CommandType.StoredProcedure);

                return Ok(rows.ToList());
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "Error loading course progress");
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }


        // ════════════════════════════════════════════════════════
        // GET  api/progress/summary/{studentId}
        // Returns: [ { CourseID, TotalContents, CompletedContents }, ... ]
        // ════════════════════════════════════════════════════════
        [HttpGet("summary/{studentId:int}")]
        public async Task<IActionResult> GetSummary(int studentId)
        {
            if (studentId <= 0)
                return Ok(System.Array.Empty<object>());

            try
            {
                using var conn = CreateConn();
                var rows = await conn.QueryAsync(
                    "SP_Progress_GetSummary",
                    new { p_StudentID = studentId },
                    commandType: CommandType.StoredProcedure);

                return Ok(rows.ToList());
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "Error loading progress summary");
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }


        // ────────────────────────────────────────────────────────
        // Award COMPLETE_LESSON / COMPLETE_COURSE points and evaluate
        // badges in a background DI scope. Point awards are dedup-safe
        // (SP_Points_Award), so repeated calls never double-credit.
        // ────────────────────────────────────────────────────────
        private void FireProgressGamification(int studentId, int courseId, int contentId,
                                              bool lessonComplete, bool courseComplete)
        {
            if (studentId <= 0 || (!lessonComplete && !courseComplete)) return;

            _ = Task.Run(async () =>
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var gamif = scope.ServiceProvider.GetRequiredService<IGamificationService>();

                    if (lessonComplete)
                        await gamif.AwardAsync(studentId, "COMPLETE_LESSON",
                            "Content", contentId.ToString(), "Lesson completed");

                    if (courseComplete)
                        await gamif.AwardAsync(studentId, "COMPLETE_COURSE",
                            "Course", courseId.ToString(), "Course completed");

                    await gamif.EvaluateAndNotifyBadgesAsync(studentId);

                    // auto-issue a course-completion certificate (idempotent)
                    if (courseComplete)
                    {
                        var issuer = scope.ServiceProvider.GetRequiredService<ICertificateIssuer>();
                        await issuer.IssueAsync(studentId, courseId, "COURSE",
                            examAttemptId: null, issuedBy: "system", sendEmail: true);
                    }
                }
                catch (System.Exception ex)
                {
                    _logger.LogError(ex, "Progress gamification failed (non-fatal). Student={S}", studentId);
                }
            });
        }
    }


    // ── DTOs ──────────────────────────────────────────────────
    public class MarkProgressApiRequest
    {
        public int StudentID { get; set; }
        public int CourseID { get; set; }
        public int ContentID { get; set; }
        public bool Completed { get; set; }
    }

    public class SavePositionApiRequest
    {
        public int StudentID { get; set; }
        public int CourseID { get; set; }
        public int ContentID { get; set; }
        public int Position { get; set; }
        public int Duration { get; set; }
    }

    /// <summary>Shape returned by SP_Progress_GetForCourse.</summary>
    public class CourseProgressApiRow
    {
        public int ContentID { get; set; }
        public int LastPositionSeconds { get; set; }
        public bool IsCompleted { get; set; }
    }
}
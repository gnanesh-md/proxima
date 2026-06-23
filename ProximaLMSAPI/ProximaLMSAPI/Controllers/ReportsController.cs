// ============================================================
// ProximaLMSAPI/Controllers/ReportsController.cs
// ------------------------------------------------------------
// Module 15 — admin analytics, student dashboard summary, and a
// GENERIC server-side CSV exporter that works for any report
// (it serializes whatever columns the SP returns).
//
// SPs: SP_Admin_* / SP_Student_DashboardSummary (Reports_DB.sql),
//      plus existing report SPs reused by the CSV endpoint.
// ============================================================
using Dapper;
using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
using System.Data;
using System.Text;

namespace ProximaLMSAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ReportsController : ControllerBase
    {
        private readonly IConfiguration _config;
        private readonly ILogger<ReportsController> _logger;

        public ReportsController(IConfiguration config, ILogger<ReportsController> logger)
        {
            _config = config; _logger = logger;
        }

        private IDbConnection Conn() => new MySqlConnection(_config.GetConnectionString("ConnectionString"));

        // ── ADMIN ANALYTICS ──────────────────────────────────

        // GET api/reports/admin/overview  — health + revenue chart + top courses + coupons in one call
        [HttpGet("admin/overview")]
        public async Task<IActionResult> AdminOverview()
        {
            using var conn = Conn();
            var health  = await conn.QuerySingleOrDefaultAsync("SP_Admin_PlatformHealth", commandType: CommandType.StoredProcedure);
            var revenue = await conn.QueryAsync("SP_Admin_RevenueMonthly", commandType: CommandType.StoredProcedure);
            var top     = await conn.QueryAsync("SP_Admin_TopCourses", new { p_Limit = 10 }, commandType: CommandType.StoredProcedure);

            using var multi = await conn.QueryMultipleAsync("SP_Admin_CouponAnalytics", commandType: CommandType.StoredProcedure);
            var coupons = (await multi.ReadAsync()).ToList();
            var couponHead = await multi.ReadFirstOrDefaultAsync();

            return Ok(new { success = true, health, revenue, topCourses = top, coupons, couponSummary = couponHead });
        }

        // GET api/reports/admin/top-courses?limit=8
        // Standalone top-courses list for the admin dashboard table widget.
        [HttpGet("admin/top-courses")]
        public async Task<IActionResult> AdminTopCourses([FromQuery] int limit = 8)
        {
            try
            {
                using var conn = Conn();
                var rows = await conn.QueryAsync("SP_Admin_TopCourses",
                    new { p_Limit = limit <= 0 ? 8 : limit },
                    commandType: CommandType.StoredProcedure);
                return Ok(rows);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AdminTopCourses failed");
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        // ── STUDENT DASHBOARD ────────────────────────────────

        // GET api/reports/student/{studentId}
        [HttpGet("student/{studentId:int}")]
        public async Task<IActionResult> StudentDashboard(int studentId)
        {
            using var conn = Conn();
            using var multi = await conn.QueryMultipleAsync("SP_Student_DashboardSummary",
                new { p_StudentID = studentId }, commandType: CommandType.StoredProcedure);
            var summary = await multi.ReadFirstOrDefaultAsync();
            var pending = (await multi.ReadAsync()).ToList();
            return Ok(new { success = true, summary, pending });
        }

        // GET api/reports/student/{studentId}/weekly-activity
        // Returns [Sun,Mon,Tue,Wed,Thu,Fri,Sat] = lessons completed per day
        // over a rolling 7-day window ending today. Feeds the dashboard bar chart.
        [HttpGet("student/{studentId:int}/weekly-activity")]
        public async Task<IActionResult> StudentWeeklyActivity(int studentId)
        {
            var week = new int[7]; // index 0=Sun .. 6=Sat
            if (studentId <= 0) return Ok(week);

            try
            {
                using var conn = Conn();
                var rows = await conn.QueryAsync(@"
                    SELECT DAYOFWEEK(CompletedDate) AS Dow, COUNT(*) AS Cnt
                    FROM   TblStudentCourseProgress
                    WHERE  StudentID   = @sid
                      AND  IsCompleted = 1
                      AND  CompletedDate IS NOT NULL
                      AND  CompletedDate >= (CURDATE() - INTERVAL 6 DAY)
                    GROUP BY DAYOFWEEK(CompletedDate)",
                    new { sid = studentId });

                foreach (var r in rows)
                {
                    int dow = Convert.ToInt32(r.Dow); // MySQL DAYOFWEEK: 1=Sun .. 7=Sat
                    int cnt = Convert.ToInt32(r.Cnt);
                    int idx = dow - 1;
                    if (idx >= 0 && idx < 7) week[idx] = cnt;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "weekly-activity failed for {Sid}", studentId);
            }

            return Ok(week);
        }

        // GET api/reports/student/{studentId}/upcoming-deadlines?limit=5
        // Enrolled, not-yet-completed courses that carry a DueDate, soonest
        // first. Feeds the dashboard "Upcoming Deadlines" card (which fetches
        // /Assignments/UpcomingDeadlines on the MVC side). Always returns a
        // JSON array so the client never has to special-case errors.
        [HttpGet("student/{studentId:int}/upcoming-deadlines")]
        public async Task<IActionResult> StudentUpcomingDeadlines(int studentId, [FromQuery] int limit = 5)
        {
            if (studentId <= 0) return Ok(Array.Empty<object>());
            if (limit <= 0 || limit > 50) limit = 5;

            try
            {
                using var conn = Conn();
                var rows = await conn.QueryAsync(@"
                    SELECT c.CourseTitle AS Title,
                           c.CourseTitle AS CourseName,
                           sc.DueDate    AS DueDate,
                           'Course'      AS Type
                    FROM   TblStudentCourses sc
                    JOIN   TblCourseMaster c ON c.CourseID = sc.CourseID
                    WHERE  sc.StudentID = @sid
                      AND  sc.IsActive  = 1
                      AND  sc.DueDate IS NOT NULL
                      AND  NOT EXISTS (
                            SELECT 1 FROM TblPointsLedger l
                            WHERE l.StudentID = @sid
                              AND l.ActionCode = 'COMPLETE_COURSE'
                              AND l.RefID = CAST(sc.CourseID AS CHAR))
                    ORDER BY sc.DueDate ASC
                    LIMIT @lim;",
                    new { sid = studentId, lim = limit });

                return Ok(rows);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "upcoming-deadlines failed for {Sid}", studentId);
                return Ok(Array.Empty<object>());
            }
        }

        // ── GENERIC CSV EXPORT ───────────────────────────────
        // GET api/reports/export?report=NAME&...params
        // Whitelisted report → SP map. Streams a .csv of the SP's columns.
        [HttpGet("export")]
        public async Task<IActionResult> Export([FromQuery] string report,
                                                [FromQuery] int? id,
                                                [FromQuery] DateTime? from,
                                                [FromQuery] DateTime? to)
        {
            if (string.IsNullOrWhiteSpace(report))
                return BadRequest(new { success = false, message = "report is required." });

            // whitelist: report key → (sp, params, filename)
            (string sp, object? prm, string file)? map = report.ToLowerInvariant() switch
            {
                "revenue_monthly" => ("SP_Admin_RevenueMonthly", null, "revenue_monthly"),
                "top_courses"     => ("SP_Admin_TopCourses", new { p_Limit = 100 }, "top_courses"),
                "coupon_usage"    => ("SP_Coupon_UsageReport", null, "coupon_usage"),
                "team_completion" => ("SP_Assignment_GetTeamCompletion", new { p_ManagerID = id ?? 0 }, "team_completion"),
                "tutor_revenue"   => ("SP_Revenue_GetForTutor", new { p_TutorID = id ?? 0 }, "tutor_revenue"),
                "course_students" => ("SP_Instructor_GetCourseStudents", new { p_CourseID = id ?? 0, p_TutorID = 0 }, "course_students"),
                "points_history"  => ("SP_Gamif_PointsHistory", new { p_StudentID = id ?? 0 }, "points_history"),
                "certificates"    => ("SP_Certificate_ListAll", null, "certificates"),
                _ => null
            };

            if (map == null)
                return BadRequest(new { success = false, message = $"Unknown report '{report}'." });

            try
            {
                using var conn = Conn();
                var rows = (await conn.QueryAsync(map.Value.sp, map.Value.prm,
                    commandType: CommandType.StoredProcedure)).ToList();

                var csv = ToCsv(rows);
                var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv)).ToArray(); // BOM for Excel
                var fname = $"{map.Value.file}_{DateTime.Now:yyyyMMdd_HHmm}.csv";
                return File(bytes, "text/csv", fname);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CSV export failed for {Report}", report);
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        // serialize a list of dynamic rows (IDictionary<string,object>) to CSV
        private static string ToCsv(System.Collections.Generic.List<dynamic> rows)
        {
            var sb = new StringBuilder();
            if (rows.Count == 0) return "";

            var first = (IDictionary<string, object>)rows[0];
            var cols = first.Keys.ToList();
            sb.AppendLine(string.Join(",", cols.Select(Esc)));

            foreach (var r in rows)
            {
                var d = (IDictionary<string, object>)r;
                sb.AppendLine(string.Join(",", cols.Select(c => Esc(Fmt(d.TryGetValue(c, out var v) ? v : null)))));
            }
            return sb.ToString();
        }

        private static string Fmt(object? v)
        {
            if (v == null) return "";
            if (v is DateTime dt) return dt.ToString("yyyy-MM-dd HH:mm");
            if (v is bool b) return b ? "Y" : "N";
            return v.ToString() ?? "";
        }

        private static string Esc(string s)
        {
            s ??= "";
            return (s.Contains(',') || s.Contains('"') || s.Contains('\n'))
                ? "\"" + s.Replace("\"", "\"\"") + "\""
                : s;
        }
    }
}

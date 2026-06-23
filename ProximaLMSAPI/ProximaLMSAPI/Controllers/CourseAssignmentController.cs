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
    public class CourseAssignmentController : ControllerBase
    {
        private readonly IConfiguration _config;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<CourseAssignmentController> _logger;

        public CourseAssignmentController(IConfiguration config,
                                          IServiceScopeFactory scopeFactory,
                                          ILogger<CourseAssignmentController> logger)
        {
            _config = config;
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        private IDbConnection CreateConn()
            => new MySqlConnection(_config.GetConnectionString("ConnectionString"));

        // ─────────────────────────────────────────
        // GET api/courseassignment/student/{id}
        // Get student info
        // ─────────────────────────────────────────
        [HttpGet("student/{id:int}")]
        public async Task<IActionResult> GetStudent(int id)
        {
            try
            {
                using var conn = CreateConn();
                var row = await conn.QueryFirstOrDefaultAsync(
                    "SELECT ID, Name, Email, MobileNumber, Gender, IsActive " +
                    "FROM TblUserMasters WHERE ID = @id AND RoleID = 3",
                    new { id });

                if (row == null)
                    return NotFound(new { success = false, message = "Student not found." });

                return Ok(new { success = true, data = row });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        // ─────────────────────────────────────────
        // GET api/courseassignment/courses/{studentId}
        // All courses with IsAssigned flag for a student
        // ─────────────────────────────────────────
        [HttpGet("courses/{studentId:int}")]
        public async Task<IActionResult> GetCoursesForStudent(int studentId)
        {
            try
            {
                using var conn = CreateConn();
                var rows = await conn.QueryAsync(
                    "SP_Course_GetAllWithAssignment",
                    new { p_StudentID = studentId },
                    commandType: CommandType.StoredProcedure);

                return Ok(rows.ToList());
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        // ─────────────────────────────────────────
        // GET api/courseassignment/student-courses/{studentId}
        // ONLY assigned courses for a student (used by Courses/List for students)
        // ─────────────────────────────────────────
        [HttpGet("student-courses/{studentId:int}")]
        public async Task<IActionResult> GetAssignedCourses(int studentId)
        {
            try
            {
                using var conn = CreateConn();
                var rows = await conn.QueryAsync(
                    "SP_Course_GetAssignedForStudent",
                    new { p_StudentID = studentId },
                    commandType: CommandType.StoredProcedure);

                return Ok(rows.ToList());
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        [HttpPost("assign")]
        public async Task<IActionResult> AssignCourse([FromBody] AssignCourseApiRequest req)
        {
            try
            {
                using var conn = CreateConn();
                conn.Open();

                var p = new DynamicParameters();
                p.Add("p_StudentID", req.StudentID);
                p.Add("p_CourseID", req.CourseID);
                p.Add("p_AssignedBy", req.AssignedBy ?? "Admin");
                p.Add("p_DueDate", req.DueDate);          // null = no due date
                p.Add("p_IsMandatory", req.IsMandatory ? 1 : 0);
                p.Add("p_Note", req.Note);
                p.Add("p_ResultCode", dbType: DbType.Int32, direction: ParameterDirection.Output);
                p.Add("p_Message", dbType: DbType.String, size: 500, direction: ParameterDirection.Output);

                await conn.ExecuteAsync("SP_Course_AssignToStudent", p,
                    commandType: CommandType.StoredProcedure);

                int code = p.Get<int>("p_ResultCode");
                string msg = p.Get<string>("p_Message") ?? "";

                // notify the assignee only on a genuinely NEW assignment
                // (code 0 = already enrolled / refreshed → no notification)
                if (code == 1)
                {
                    DispatchAssignmentNotifications(new List<AssignNotifyItem>
                    {
                        new AssignNotifyItem
                        {
                            StudentId = req.StudentID,
                            CourseId  = req.CourseID,
                            Title     = null,          // fetched in the helper
                            Due       = req.DueDate,
                            Mandatory = req.IsMandatory
                        }
                    });
                }

                // 1 = new, 0 = already-enrolled (refreshed), -1 = error
                return code >= 0
                    ? Ok(new { success = true, code, message = msg })
                    : StatusCode(500, new { success = false, message = msg });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        [HttpPost("bulk-assign")]
        public async Task<IActionResult> BulkAssign([FromBody] BulkAssignApiRequest req)
        {
            if (req == null
                || req.StudentIDs == null || req.StudentIDs.Count == 0
                || req.CourseIDs == null || req.CourseIDs.Count == 0)
                return BadRequest(new { success = false, message = "Pick at least one student and one course." });

            try
            {
                using var conn = (MySqlConnection)CreateConn();
                await conn.OpenAsync();

                // SP returns 2 result sets — read with QueryMultiple
                using var multi = await conn.QueryMultipleAsync(
                    "SP_Course_BulkAssign",
                    new
                    {
                        p_StudentIDs = string.Join(",", req.StudentIDs),
                        p_CourseIDs = string.Join(",", req.CourseIDs),
                        p_AssignedBy = req.AssignedBy ?? "Admin",
                        p_DueDate = req.DueDate,
                        p_IsMandatory = req.IsMandatory ? 1 : 0,
                        p_Note = req.Note
                    },
                    commandType: CommandType.StoredProcedure);

                var summary = await multi.ReadFirstOrDefaultAsync();
                var newAssignments = (await multi.ReadAsync()).ToList();

                // notify each freshly-created assignment (already-enrolled pairs
                // are excluded by the SP, so this never double-notifies)
                var toNotify = new List<AssignNotifyItem>();
                foreach (var row in newAssignments)
                {
                    if (row is not IDictionary<string, object> r) continue;

                    int sid = r.TryGetValue("StudentID", out var sv) && sv != null ? Convert.ToInt32(sv) : 0;
                    int cid = r.TryGetValue("CourseID", out var cv) && cv != null ? Convert.ToInt32(cv) : 0;
                    if (sid <= 0 || cid <= 0) continue;

                    string title = r.TryGetValue("CourseTitle", out var tv) && tv != null ? tv.ToString() : null;
                    DateTime? due = r.TryGetValue("DueDate", out var dv) && dv != null && dv != DBNull.Value
                                        ? Convert.ToDateTime(dv) : (DateTime?)null;
                    bool mand = r.TryGetValue("IsMandatory", out var mv) && mv != null && Convert.ToInt32(mv) == 1;

                    toNotify.Add(new AssignNotifyItem
                    {
                        StudentId = sid,
                        CourseId = cid,
                        Title = title,
                        Due = due,
                        Mandatory = mand
                    });
                }
                DispatchAssignmentNotifications(toNotify);

                return Ok(new
                {
                    success = true,
                    summary,
                    newAssignments
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        [HttpGet("for-user/{userId:int}")]
        public async Task<IActionResult> GetForUser(int userId)
        {
            if (userId <= 0)
                return BadRequest(new { success = false, message = "Invalid user id." });

            try
            {
                using var conn = CreateConn();
                var rows = await conn.QueryAsync(
                    "SP_Assignment_GetForUser",
                    new { p_StudentID = userId },
                    commandType: CommandType.StoredProcedure);
                return Ok(rows.ToList());
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        [HttpGet("team-completion/{managerId:int}")]
        public async Task<IActionResult> GetTeamCompletion(int managerId)
        {
            if (managerId <= 0)
                return BadRequest(new { success = false, message = "Invalid manager id." });

            try
            {
                using var conn = CreateConn();
                var rows = await conn.QueryAsync(
                    "SP_Assignment_GetTeamCompletion",
                    new { p_ManagerID = managerId },
                    commandType: CommandType.StoredProcedure);
                return Ok(rows.ToList());
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        // GET api/courseassignment/all-completion  — admin-wide completion
        // (every active assignment, not scoped to a manager team).
        [HttpGet("all-completion")]
        public async Task<IActionResult> GetAllCompletion()
        {
            try
            {
                using var conn = CreateConn();
                var rows = await conn.QueryAsync(
                    "SP_Assignment_GetAllCompletion",
                    commandType: CommandType.StoredProcedure);
                return Ok(rows.ToList());
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }


        // ─────────────────────────────────────────
        // POST api/courseassignment/remove
        // Remove a course from a student
        // ─────────────────────────────────────────
        [HttpPost("remove")]
        public async Task<IActionResult> RemoveCourse([FromBody] AssignCourseApiRequest req)
        {
            try
            {
                using var conn = CreateConn();
                conn.Open();

                var p = new DynamicParameters();
                p.Add("p_StudentID", req.StudentID);
                p.Add("p_CourseID", req.CourseID);
                p.Add("p_AssignedBy", req.AssignedBy ?? "Admin");
                p.Add("p_ResultCode", dbType: DbType.Int32, direction: ParameterDirection.Output);
                p.Add("p_Message", dbType: DbType.String, size: 500, direction: ParameterDirection.Output);

                await conn.ExecuteAsync("SP_Course_RemoveFromStudent", p,
                    commandType: CommandType.StoredProcedure);

                int code = p.Get<int>("p_ResultCode");
                string msg = p.Get<string>("p_Message") ?? "";

                return code == 1
                    ? Ok(new { success = true, message = msg })
                    : BadRequest(new { success = false, message = msg });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        // ─────────────────────────────────────────
        // POST api/courseassignment/save-all
        // Save entire assignment list for a student (replaces all)
        // ─────────────────────────────────────────
        [HttpPost("save-all")]
        public async Task<IActionResult> SaveAll([FromBody] SaveAllApiRequest req)
        {
            if (req.StudentID <= 0)
                return BadRequest(new { success = false, message = "Invalid student ID." });

            try
            {
                using var conn = CreateConn();
                conn.Open();

                // Step 1: Deactivate ALL existing assignments for this student
                await conn.ExecuteAsync(
                    "UPDATE TblStudentCourses SET IsActive = 0 WHERE StudentID = @sid",
                    new { sid = req.StudentID });

                // Step 2: Re-activate (or insert) each selected course
                int count = 0;
                foreach (var courseId in (req.CourseIDs ?? new List<int>()))
                {
                    await conn.ExecuteAsync(@"
                        INSERT INTO TblStudentCourses
                            (StudentID, CourseID, AssignedBy, IsActive)
                        VALUES
                            (@sid, @cid, @by, 1)
                        ON DUPLICATE KEY UPDATE
                            IsActive     = 1,
                            AssignedBy   = @by,
                            AssignedDate = NOW()",
                        new { sid = req.StudentID, cid = courseId, by = req.AssignedBy ?? "Admin" });
                    count++;
                }

                return Ok(new
                {
                    success = true,
                    message = $"{count} course(s) assigned successfully."
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }


        // ─────────────────────────────────────────
        // Background COURSE_ASSIGNED notifications.
        // Runs in its own DI scope (NotificationService is scoped),
        // sends in-app + email per assignee, and never blocks or
        // throws into the request pipeline. save-all is intentionally
        // NOT wired here — it reactivates/replaces and would spam.
        // ─────────────────────────────────────────
        private sealed class AssignNotifyItem
        {
            public int StudentId { get; set; }
            public int CourseId { get; set; }
            public string Title { get; set; }
            public DateTime? Due { get; set; }
            public bool Mandatory { get; set; }
        }

        private void DispatchAssignmentNotifications(List<AssignNotifyItem> items)
        {
            if (items == null || items.Count == 0) return;

            _ = Task.Run(async () =>
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var notifier = scope.ServiceProvider.GetRequiredService<INotificationService>();

                    string baseUrl = (_config["PublicBaseUrl"] ?? _config["ApiBaseUrl"] ?? "").TrimEnd('/');

                    foreach (var it in items)
                    {
                        if (it.StudentId <= 0) continue;

                        string title = it.Title;
                        if (string.IsNullOrWhiteSpace(title))
                        {
                            using var c = CreateConn();
                            title = await c.ExecuteScalarAsync<string>(
                                "SELECT CourseTitle FROM TblCourseMaster WHERE CourseID = @c",
                                new { c = it.CourseId }) ?? "a course";
                        }

                        string clause =
                            it.Mandatory && it.Due.HasValue ? $" — mandatory, due by {it.Due.Value:dd MMM yyyy}" :
                            it.Mandatory ? " — mandatory" :
                            it.Due.HasValue ? $" — due by {it.Due.Value:dd MMM yyyy}" : "";

                        string link = string.IsNullOrEmpty(baseUrl) ? "#" : $"{baseUrl}/Courses/Details/{it.CourseId}";

                        await notifier.NotifyAsync(new NotifyRequest
                        {
                            UserID = it.StudentId,
                            EventCode = "ASSIGNMENT",
                            Title = it.Mandatory ? "New mandatory course assigned" : "New course assigned",
                            Body = $"You've been assigned <strong>{title}</strong>{clause}.",
                            LinkUrl = link,
                            Icon = "fa-solid fa-clipboard-list",
                            SendInApp = true,
                            SendEmail = true,
                            EmailTemplateCode = "COURSE_ASSIGNED",
                            Vars = new Dictionary<string, string>
                            {
                                ["CourseTitle"] = title,
                                ["DueClause"] = clause
                            }
                        });
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Assignment notifications failed (non-fatal).");
                }
            });
        }
    }

    // ── REQUEST DTOs ───────────────────────────────────────────
    public class AssignCourseApiRequest
    {
        public int StudentID { get; set; }
        public int CourseID { get; set; }
        public string AssignedBy { get; set; }

        // ── NEW (module 08) ──
        public DateTime? DueDate { get; set; }
        public bool IsMandatory { get; set; }
        public string Note { get; set; }
    }


    public class BulkAssignApiRequest
    {
        public List<int> StudentIDs { get; set; } = new();
        public List<int> CourseIDs { get; set; } = new();
        public string AssignedBy { get; set; }
        public DateTime? DueDate { get; set; }
        public bool IsMandatory { get; set; }
        public string Note { get; set; }
    }

    public class SaveAllApiRequest
    {
        public int StudentID { get; set; }
        public List<int> CourseIDs { get; set; } = new();
        public string AssignedBy { get; set; }
    }
}
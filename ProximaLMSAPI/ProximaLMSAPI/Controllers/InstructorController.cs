// ============================================================
// ProximaLMSAPI/Controllers/InstructorController.cs
// ------------------------------------------------------------
// Tutor-scoped (instructor) data API. Every endpoint that touches
// a course is OWNERSHIP-GUARDED at the SP level — a tutor can only
// see / mutate courses where TblCourseMaster.Instructor = their TutorID.
//
// Endpoints:
//   GET  /api/instructor/resolve/{loginUserId}        → TutorID for a login
//   GET  /api/instructor/stats/{tutorId}              → dashboard header stats
//   GET  /api/instructor/courses/{tutorId}            → tutor's own courses
//   POST /api/instructor/course/publish               → toggle IsActive (guarded)
//   GET  /api/instructor/course/{courseId}/owned/{tutorId}  → ownership + title
//   GET  /api/instructor/course/{courseId}/students/{tutorId} → roster (guarded)
// ============================================================
using Dapper;
using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
using System.Data;

namespace ProximaLMSAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class InstructorController : ControllerBase
    {
        private readonly IConfiguration _config;
        private readonly ILogger<InstructorController> _logger;

        public InstructorController(IConfiguration config, ILogger<InstructorController> logger)
        {
            _config = config;
            _logger = logger;
        }

        private IDbConnection CreateConn()
            => new MySqlConnection(_config.GetConnectionString("ConnectionString"));


        // ════════════════════════════════════════════════════════
        // GET  api/instructor/resolve/{loginUserId}
        // Maps a logged-in user (TblUserMasters.ID) → tutor profile.
        // Returns 404 if the login is not a registered/active tutor.
        // ════════════════════════════════════════════════════════
        [HttpGet("resolve/{loginUserId:int}")]
        public async Task<IActionResult> ResolveTutor(int loginUserId)
        {
            if (loginUserId <= 0)
                return BadRequest(new { Status = "Error", Message = "Invalid user id." });

            try
            {
                using var conn = CreateConn();
                var tutor = await conn.QueryFirstOrDefaultAsync(
                    "SP_Instructor_ResolveTutor",
                    new { p_LoginUserID = loginUserId },
                    commandType: CommandType.StoredProcedure);

                if (tutor == null)
                    return NotFound(new { Status = "Error", Message = "No tutor profile linked to this login." });

                return Ok(tutor);
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "Error resolving tutor for login {Login}", loginUserId);
                return StatusCode(500, new { Status = "Error", Message = ex.Message });
            }
        }


        // ════════════════════════════════════════════════════════
        // GET  api/instructor/stats/{tutorId}
        // ════════════════════════════════════════════════════════
        [HttpGet("stats/{tutorId:int}")]
        public async Task<IActionResult> GetStats(int tutorId)
        {
            if (tutorId <= 0)
                return BadRequest(new { Status = "Error", Message = "Invalid tutor id." });

            try
            {
                using var conn = CreateConn();
                var stats = await conn.QueryFirstOrDefaultAsync(
                    "SP_Instructor_GetStats",
                    new { p_TutorID = tutorId },
                    commandType: CommandType.StoredProcedure);

                return Ok(stats ?? new { });
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "Error loading instructor stats for tutor {Tutor}", tutorId);
                return StatusCode(500, new { Status = "Error", Message = ex.Message });
            }
        }


        // ════════════════════════════════════════════════════════
        // GET  api/instructor/courses/{tutorId}
        // ════════════════════════════════════════════════════════
        [HttpGet("courses/{tutorId:int}")]
        public async Task<IActionResult> GetCourses(int tutorId)
        {
            if (tutorId <= 0)
                return BadRequest(new { Status = "Error", Message = "Invalid tutor id." });

            try
            {
                using var conn = CreateConn();
                var rows = await conn.QueryAsync(
                    "SP_Instructor_GetCourses",
                    new { p_TutorID = tutorId },
                    commandType: CommandType.StoredProcedure);

                return Ok(rows.ToList());
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "Error loading courses for tutor {Tutor}", tutorId);
                return StatusCode(500, new { Status = "Error", Message = ex.Message });
            }
        }


        // ════════════════════════════════════════════════════════
        // POST api/instructor/course/publish
        // Body: { CourseID, TutorID, IsActive }
        // Ownership-guarded at the SP level. Affected==0 ⇒ not yours.
        // ════════════════════════════════════════════════════════
        [HttpPost("course/publish")]
        public async Task<IActionResult> TogglePublish([FromBody] TogglePublishApiRequest req)
        {
            if (req == null || req.CourseID <= 0 || req.TutorID <= 0)
                return BadRequest(new { Status = "Error", Message = "Invalid request." });

            try
            {
                using var conn = CreateConn();
                var p = new DynamicParameters();
                p.Add("p_CourseID", req.CourseID);
                p.Add("p_TutorID", req.TutorID);
                p.Add("p_IsActive", req.IsActive ? 1 : 0);
                p.Add("p_Affected", dbType: DbType.Int32, direction: ParameterDirection.Output);

                await conn.ExecuteAsync("SP_Instructor_TogglePublish", p,
                    commandType: CommandType.StoredProcedure);

                int affected = p.Get<int>("p_Affected");
                if (affected == 0)
                    return NotFound(new { Status = "Error", Message = "Course not found or not owned by this tutor." });

                return Ok(new
                {
                    Status = "Success",
                    CourseID = req.CourseID,
                    IsActive = req.IsActive,
                    Message = req.IsActive ? "Course published." : "Course unpublished."
                });
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "Error toggling publish for course {Course}", req?.CourseID);
                return StatusCode(500, new { Status = "Error", Message = ex.Message });
            }
        }


        // ════════════════════════════════════════════════════════
        // GET api/instructor/course/{courseId}/owned/{tutorId}
        // Returns the course header iff it belongs to the tutor.
        // ════════════════════════════════════════════════════════
        [HttpGet("course/{courseId:int}/owned/{tutorId:int}")]
        public async Task<IActionResult> GetOwnedCourse(int courseId, int tutorId)
        {
            try
            {
                using var conn = CreateConn();
                var course = await conn.QueryFirstOrDefaultAsync(
                    "SP_Instructor_GetOwnedCourse",
                    new { p_CourseID = courseId, p_TutorID = tutorId },
                    commandType: CommandType.StoredProcedure);

                if (course == null)
                    return NotFound(new { Status = "Error", Message = "Course not found or not owned." });

                return Ok(course);
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "Error checking ownership course {Course} tutor {Tutor}", courseId, tutorId);
                return StatusCode(500, new { Status = "Error", Message = ex.Message });
            }
        }


        // ════════════════════════════════════════════════════════
        // GET api/instructor/course/{courseId}/students/{tutorId}
        // Ownership-guarded roster. Progress % is merged by the MVC layer.
        // ════════════════════════════════════════════════════════
        [HttpGet("course/{courseId:int}/students/{tutorId:int}")]
        public async Task<IActionResult> GetCourseStudents(int courseId, int tutorId)
        {
            if (courseId <= 0 || tutorId <= 0)
                return BadRequest(new { Status = "Error", Message = "Invalid request." });

            try
            {
                using var conn = CreateConn();
                var rows = await conn.QueryAsync(
                    "SP_Instructor_GetCourseStudents",
                    new { p_CourseID = courseId, p_TutorID = tutorId },
                    commandType: CommandType.StoredProcedure);

                return Ok(rows.ToList());
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "Error loading roster course {Course} tutor {Tutor}", courseId, tutorId);
                return StatusCode(500, new { Status = "Error", Message = ex.Message });
            }
        }


        [HttpGet("revenue/{tutorId:int}")]
        public async Task<IActionResult> GetRevenue(int tutorId)
        {
            if (tutorId <= 0)
                return BadRequest(new { Status = "Error", Message = "Invalid tutor id." });

            try
            {
                using var conn = CreateConn();
                var rows = await conn.QueryAsync(
                    "SP_Revenue_GetForTutor",
                    new { p_TutorID = tutorId },
                    commandType: CommandType.StoredProcedure);
                return Ok(rows.ToList());
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "Error loading revenue for tutor {Tutor}", tutorId);
                return StatusCode(500, new { Status = "Error", Message = ex.Message });
            }
        }

        // GET api/instructor/revenue/{tutorId}/course/{courseId}
        // Paid-order detail for one owned course.
        [HttpGet("revenue/{tutorId:int}/course/{courseId:int}")]
        public async Task<IActionResult> GetCourseOrders(int tutorId, int courseId)
        {
            if (tutorId <= 0 || courseId <= 0)
                return BadRequest(new { Status = "Error", Message = "Invalid request." });

            try
            {
                using var conn = CreateConn();
                var rows = await conn.QueryAsync(
                    "SP_Revenue_GetCourseOrders",
                    new { p_CourseID = courseId, p_TutorID = tutorId },
                    commandType: CommandType.StoredProcedure);
                return Ok(rows.ToList());
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "Error loading orders for course {Course}", courseId);
                return StatusCode(500, new { Status = "Error", Message = ex.Message });
            }
        }

    }


    // ── DTOs ──────────────────────────────────────────────────
    public class TogglePublishApiRequest
    {
        public int CourseID { get; set; }
        public int TutorID { get; set; }
        public bool IsActive { get; set; }
    }
}

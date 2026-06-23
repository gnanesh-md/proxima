// ============================================================
// ProximaLMSAPI/Controllers/AnnouncementController.cs
// ------------------------------------------------------------
// Course announcements (authoring). Dapper + stored procedures.
//
//   GET  /api/announcement/by-course/{courseId} → list (pinned first)
//   POST /api/announcement/save                  → create / update
//   POST /api/announcement/delete                → soft delete
//   POST /api/announcement/toggle-pin            → pin / unpin
// ============================================================
using Dapper;
using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
using System.Data;

namespace ProximaLMSAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AnnouncementController : ControllerBase
    {
        private readonly IConfiguration _config;
        private readonly ILogger<AnnouncementController> _logger;

        public AnnouncementController(IConfiguration config, ILogger<AnnouncementController> logger)
        {
            _config = config;
            _logger = logger;
        }

        private MySqlConnection CreateConn()
            => new MySqlConnection(_config.GetConnectionString("ConnectionString"));

        [HttpGet("by-course/{courseId:int}")]
        public async Task<IActionResult> GetByCourse(int courseId)
        {
            try
            {
                using var conn = CreateConn();
                var rows = await conn.QueryAsync(
                    "SP_Announcement_GetByCourse",
                    new { p_CourseID = courseId },
                    commandType: CommandType.StoredProcedure);
                return Ok(rows.ToList());
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "Error listing announcements for course {Course}", courseId);
                return StatusCode(500, new { Status = "Error", Message = ex.Message });
            }
        }

        [HttpPost("save")]
        public async Task<IActionResult> Save([FromBody] AnnouncementSaveRequest req)
        {
            if (req == null || req.CourseID <= 0
                || string.IsNullOrWhiteSpace(req.Title)
                || string.IsNullOrWhiteSpace(req.Body))
                return BadRequest(new { Status = "Error", Message = "Course, title and body are required." });

            try
            {
                using var conn = CreateConn();
                var p = new DynamicParameters();
                p.Add("p_AnnouncementID", req.AnnouncementID);
                p.Add("p_CourseID", req.CourseID);
                p.Add("p_Title", req.Title.Trim());
                p.Add("p_Body", req.Body.Trim());
                p.Add("p_IsPinned", req.IsPinned ? 1 : 0);
                p.Add("p_Actor", req.Actor ?? "Admin");
                p.Add("p_OutID", dbType: DbType.Int32, direction: ParameterDirection.Output);

                await conn.ExecuteAsync("SP_Announcement_Save", p,
                    commandType: CommandType.StoredProcedure);

                return Ok(new { Status = "Success", AnnouncementID = p.Get<int>("p_OutID") });
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "Error saving announcement");
                return StatusCode(500, new { Status = "Error", Message = ex.Message });
            }
        }

        [HttpPost("delete")]
        public async Task<IActionResult> Delete([FromBody] AnnouncementIdRequest req)
        {
            if (req == null || req.AnnouncementID <= 0)
                return BadRequest(new { Status = "Error", Message = "Invalid request." });

            try
            {
                using var conn = CreateConn();
                await conn.ExecuteAsync("SP_Announcement_Delete",
                    new { p_AnnouncementID = req.AnnouncementID },
                    commandType: CommandType.StoredProcedure);
                return Ok(new { Status = "Success" });
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "Error deleting announcement {Id}", req.AnnouncementID);
                return StatusCode(500, new { Status = "Error", Message = ex.Message });
            }
        }

        [HttpPost("toggle-pin")]
        public async Task<IActionResult> TogglePin([FromBody] AnnouncementPinRequest req)
        {
            if (req == null || req.AnnouncementID <= 0)
                return BadRequest(new { Status = "Error", Message = "Invalid request." });

            try
            {
                using var conn = CreateConn();
                await conn.ExecuteAsync("SP_Announcement_TogglePin",
                    new { p_AnnouncementID = req.AnnouncementID, p_IsPinned = req.IsPinned ? 1 : 0 },
                    commandType: CommandType.StoredProcedure);
                return Ok(new { Status = "Success", IsPinned = req.IsPinned });
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "Error pinning announcement {Id}", req.AnnouncementID);
                return StatusCode(500, new { Status = "Error", Message = ex.Message });
            }
        }
    }

    public class AnnouncementSaveRequest
    {
        public int AnnouncementID { get; set; }
        public int CourseID { get; set; }
        public string Title { get; set; }
        public string Body { get; set; }
        public bool IsPinned { get; set; }
        public string Actor { get; set; }
    }
    public class AnnouncementIdRequest { public int AnnouncementID { get; set; } }
    public class AnnouncementPinRequest { public int AnnouncementID { get; set; } public bool IsPinned { get; set; } }
}

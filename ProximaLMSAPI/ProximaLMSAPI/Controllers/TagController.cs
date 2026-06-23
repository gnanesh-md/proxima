// ============================================================
// ProximaLMSAPI/Controllers/TagController.cs
// ------------------------------------------------------------
// Tag master + course-tag attachment endpoints.
//
//   GET  /api/tag/all                     → admin grid
//   GET  /api/tag/active                  → lookup dropdown
//   GET  /api/tag/{id}                    → single for edit
//   POST /api/tag/save                    → insert / update
//   POST /api/tag/toggle-status           → activate / deactivate
//   POST /api/tag/delete                  → hard delete (detaches courses)
//
//   GET  /api/tag/for-course/{courseId}   → tags currently on a course
//   POST /api/tag/set-for-course          → replace full tag set on a course
// ============================================================
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MySql.Data.MySqlClient;

namespace ProximaLMSAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TagController : ControllerBase
    {
        private readonly IConfiguration _config;
        private readonly ILogger<TagController> _logger;

        public TagController(IConfiguration config, ILogger<TagController> logger)
        {
            _config = config;
            _logger = logger;
        }

        private IDbConnection CreateConn()
            => new MySqlConnection(_config.GetConnectionString("ConnectionString"));


        // ════════════════════════════════════════════════════════
        // GET  api/tag/all?includeInactive=1
        // ════════════════════════════════════════════════════════
        [HttpGet("all")]
        public async Task<IActionResult> GetAll([FromQuery] int includeInactive = 1)
        {
            try
            {
                using var conn = CreateConn();
                var rows = await conn.QueryAsync(
                    "SP_Tag_GetAll",
                    new { p_IncludeInactive = includeInactive == 1 ? 1 : 0 },
                    commandType: CommandType.StoredProcedure);
                return Ok(rows.ToList());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading tags");
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }


        // ════════════════════════════════════════════════════════
        // GET  api/tag/active
        // Active tags only — for tag-pickers on the course form.
        // ════════════════════════════════════════════════════════
        [HttpGet("active")]
        public async Task<IActionResult> GetActive()
        {
            try
            {
                using var conn = CreateConn();
                var rows = await conn.QueryAsync(
                    "SP_Tag_GetAll",
                    new { p_IncludeInactive = 0 },
                    commandType: CommandType.StoredProcedure);
                return Ok(rows.ToList());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading active tags");
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }


        // ════════════════════════════════════════════════════════
        // GET  api/tag/{id}
        // ════════════════════════════════════════════════════════
        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetById(int id)
        {
            if (id <= 0)
                return BadRequest(new { success = false, message = "Invalid id." });

            try
            {
                using var conn = CreateConn();
                var row = await conn.QueryFirstOrDefaultAsync(
                    "SP_Tag_GetById",
                    new { p_TagID = id },
                    commandType: CommandType.StoredProcedure);

                if (row == null)
                    return NotFound(new { success = false, message = "Tag not found." });

                return Ok(row);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading tag {Id}", id);
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }


        // ════════════════════════════════════════════════════════
        // POST  api/tag/save
        // ════════════════════════════════════════════════════════
        [HttpPost("save")]
        public async Task<IActionResult> Save([FromBody] SaveTagApiRequest req)
        {
            if (req == null || string.IsNullOrWhiteSpace(req.TagName))
                return BadRequest(new { success = false, message = "Tag name is required." });

            // Light client-side color validation
            if (!string.IsNullOrWhiteSpace(req.ColorHex) && !IsValidHex(req.ColorHex))
                return BadRequest(new { success = false, message = "Colour must be a hex value like #7B2CBF." });

            try
            {
                using var conn = CreateConn();
                conn.Open();

                var p = new DynamicParameters();
                p.Add("p_TagID",      req.TagID);
                p.Add("p_TagName",    req.TagName.Trim());
                p.Add("p_Slug",       string.IsNullOrWhiteSpace(req.Slug) ? null : req.Slug.Trim());
                p.Add("p_ColorHex",   string.IsNullOrWhiteSpace(req.ColorHex) ? null : req.ColorHex.Trim());
                p.Add("p_SortOrder",  req.SortOrder);
                p.Add("p_IsActive",   req.IsActive ? 1 : 0);
                p.Add("p_ActorEmail", string.IsNullOrWhiteSpace(req.ActorEmail) ? "Admin" : req.ActorEmail.Trim());
                p.Add("p_ResultCode", dbType: DbType.Int32,  direction: ParameterDirection.Output);
                p.Add("p_Message",    dbType: DbType.String, size: 500, direction: ParameterDirection.Output);
                p.Add("p_OutTagID",   dbType: DbType.Int32,  direction: ParameterDirection.Output);

                await conn.ExecuteAsync("SP_Tag_Save", p,
                    commandType: CommandType.StoredProcedure);

                int    code = p.Get<int>("p_ResultCode");
                string msg  = p.Get<string>("p_Message") ?? "";
                int    outId = p.Get<int>("p_OutTagID");

                return code == 1
                    ? Ok(new { success = true, message = msg, tagId = outId })
                    : BadRequest(new { success = false, message = msg });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving tag");
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }


        // ════════════════════════════════════════════════════════
        // POST  api/tag/toggle-status
        // ════════════════════════════════════════════════════════
        [HttpPost("toggle-status")]
        public async Task<IActionResult> ToggleStatus([FromBody] ToggleStatusApiRequestTag req)
        {
            if (req == null || req.TagID <= 0)
                return BadRequest(new { success = false, message = "Invalid request." });

            try
            {
                using var conn = CreateConn();
                conn.Open();

                var p = new DynamicParameters();
                p.Add("p_TagID",      req.TagID);
                p.Add("p_ActorEmail", string.IsNullOrWhiteSpace(req.ActorEmail) ? "Admin" : req.ActorEmail.Trim());
                p.Add("p_ResultCode", dbType: DbType.Int32,  direction: ParameterDirection.Output);
                p.Add("p_Message",    dbType: DbType.String, size: 500, direction: ParameterDirection.Output);
                p.Add("p_NewStatus",  dbType: DbType.Byte,   direction: ParameterDirection.Output);

                await conn.ExecuteAsync("SP_Tag_ToggleStatus", p,
                    commandType: CommandType.StoredProcedure);

                int    code = p.Get<int>("p_ResultCode");
                string msg  = p.Get<string>("p_Message") ?? "";
                byte   ns   = p.Get<byte>("p_NewStatus");

                return code == 1
                    ? Ok(new { success = true, message = msg, isActive = ns == 1 })
                    : BadRequest(new { success = false, message = msg });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error toggling tag status");
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }


        // ════════════════════════════════════════════════════════
        // POST  api/tag/delete
        // ════════════════════════════════════════════════════════
        [HttpPost("delete")]
        public async Task<IActionResult> Delete([FromBody] DeleteTagApiRequest req)
        {
            if (req == null || req.TagID <= 0)
                return BadRequest(new { success = false, message = "Invalid request." });

            try
            {
                using var conn = CreateConn();
                conn.Open();

                var p = new DynamicParameters();
                p.Add("p_TagID",      req.TagID);
                p.Add("p_ResultCode", dbType: DbType.Int32,  direction: ParameterDirection.Output);
                p.Add("p_Message",    dbType: DbType.String, size: 500, direction: ParameterDirection.Output);

                await conn.ExecuteAsync("SP_Tag_Delete", p,
                    commandType: CommandType.StoredProcedure);

                int    code = p.Get<int>("p_ResultCode");
                string msg  = p.Get<string>("p_Message") ?? "";

                return code == 1
                    ? Ok(new { success = true, message = msg })
                    : BadRequest(new { success = false, message = msg });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting tag");
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }


        // ════════════════════════════════════════════════════════
        // GET  api/tag/for-course/{courseId}
        // ════════════════════════════════════════════════════════
        [HttpGet("for-course/{courseId:int}")]
        public async Task<IActionResult> GetForCourse(int courseId)
        {
            if (courseId <= 0)
                return Ok(Array.Empty<object>());

            try
            {
                using var conn = CreateConn();
                var rows = await conn.QueryAsync(
                    "SP_CourseTag_GetForCourse",
                    new { p_CourseID = courseId },
                    commandType: CommandType.StoredProcedure);
                return Ok(rows.ToList());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading tags for course {Id}", courseId);
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }


        // ════════════════════════════════════════════════════════
        // POST  api/tag/set-for-course
        // Body: { CourseID, TagIDs: [3,7,9] }
        // Replaces the full tag set for one course. Empty array
        // = remove every tag from that course.
        // ════════════════════════════════════════════════════════
        [HttpPost("set-for-course")]
        public async Task<IActionResult> SetForCourse([FromBody] SetCourseTagsApiRequest req)
        {
            if (req == null || req.CourseID <= 0)
                return BadRequest(new { success = false, message = "Invalid course id." });

            // sanitise — ints only, deduped
            string csv = req.TagIDs == null
                ? ""
                : string.Join(",", req.TagIDs.Where(id => id > 0).Distinct());

            try
            {
                using var conn = CreateConn();
                conn.Open();

                var p = new DynamicParameters();
                p.Add("p_CourseID",   req.CourseID);
                p.Add("p_TagIDs",     csv);
                p.Add("p_ResultCode", dbType: DbType.Int32,  direction: ParameterDirection.Output);
                p.Add("p_Message",    dbType: DbType.String, size: 500, direction: ParameterDirection.Output);

                await conn.ExecuteAsync("SP_CourseTag_SetForCourse", p,
                    commandType: CommandType.StoredProcedure);

                int    code = p.Get<int>("p_ResultCode");
                string msg  = p.Get<string>("p_Message") ?? "";

                return code == 1
                    ? Ok(new { success = true, message = msg })
                    : BadRequest(new { success = false, message = msg });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error setting course tags");
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }


        // ── helpers ───────────────────────────────────────────────
        private static bool IsValidHex(string s)
        {
            s = s.Trim();
            if (s.Length != 4 && s.Length != 7 && s.Length != 9) return false;
            if (s[0] != '#') return false;
            for (int i = 1; i < s.Length; i++)
            {
                char c = char.ToLower(s[i]);
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))) return false;
            }
            return true;
        }
    }


    // ── Request DTOs ──────────────────────────────────────────
    public class SaveTagApiRequest
    {
        public int     TagID      { get; set; }   // 0 = insert
        public string  TagName    { get; set; } = "";
        public string? Slug       { get; set; }
        public string? ColorHex   { get; set; }
        public int     SortOrder  { get; set; }
        public bool    IsActive   { get; set; } = true;
        public string  ActorEmail { get; set; } = "Admin";
    }

    public class ToggleStatusApiRequestTag
    {
        public int    TagID      { get; set; }
        public string ActorEmail { get; set; } = "Admin";
    }

    public class DeleteTagApiRequest
    {
        public int TagID { get; set; }
    }

    public class SetCourseTagsApiRequest
    {
        public int       CourseID { get; set; }
        public List<int> TagIDs   { get; set; } = new();
    }
}

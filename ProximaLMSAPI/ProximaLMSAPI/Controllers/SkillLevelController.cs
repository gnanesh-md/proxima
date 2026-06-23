// ============================================================
// ProximaLMSAPI/Controllers/SkillLevelController.cs
// ------------------------------------------------------------
// Skill level master CRUD (route: "skilllevel").
//
//   GET  /api/skilllevel/all              → admin grid
//   GET  /api/skilllevel/active           → dropdown source
//   GET  /api/skilllevel/{id}             → single for edit
//   POST /api/skilllevel/save             → insert / update
//   POST /api/skilllevel/toggle-status    → activate / deactivate
//   POST /api/skilllevel/delete           → hard delete (blocked if in use)
// ============================================================
using System;
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
    public class SkillLevelController : ControllerBase
    {
        private readonly IConfiguration _config;
        private readonly ILogger<SkillLevelController> _logger;

        public SkillLevelController(IConfiguration config, ILogger<SkillLevelController> logger)
        {
            _config = config;
            _logger = logger;
        }

        private IDbConnection CreateConn()
            => new MySqlConnection(_config.GetConnectionString("ConnectionString"));


        // ════════════════════════════════════════════════════════
        // GET  api/skilllevel/all?includeInactive=1
        // ════════════════════════════════════════════════════════
        [HttpGet("all")]
        public async Task<IActionResult> GetAll([FromQuery] int includeInactive = 1)
        {
            try
            {
                using var conn = CreateConn();
                var rows = await conn.QueryAsync(
                    "SP_Level_GetAll",
                    new { p_IncludeInactive = includeInactive == 1 ? 1 : 0 },
                    commandType: CommandType.StoredProcedure);

                return Ok(rows.ToList());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading skill levels");
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }


        // ════════════════════════════════════════════════════════
        // GET  api/skilllevel/active
        // Active levels only — for course-form dropdowns.
        // ════════════════════════════════════════════════════════
        [HttpGet("active")]
        public async Task<IActionResult> GetActive()
        {
            try
            {
                using var conn = CreateConn();
                var rows = await conn.QueryAsync(
                    "SP_Level_GetAll",
                    new { p_IncludeInactive = 0 },
                    commandType: CommandType.StoredProcedure);

                return Ok(rows.ToList());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading active skill levels");
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }


        // ════════════════════════════════════════════════════════
        // GET  api/skilllevel/{id}
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
                    "SP_Level_GetById",
                    new { p_LevelID = id },
                    commandType: CommandType.StoredProcedure);

                if (row == null)
                    return NotFound(new { success = false, message = "Skill level not found." });

                return Ok(row);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading skill level {Id}", id);
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }


        // ════════════════════════════════════════════════════════
        // POST  api/skilllevel/save
        // ════════════════════════════════════════════════════════
        [HttpPost("save")]
        public async Task<IActionResult> Save([FromBody] SaveLevelApiRequest req)
        {
            if (req == null || string.IsNullOrWhiteSpace(req.LevelName))
                return BadRequest(new { success = false, message = "Level name is required." });

            if (!string.IsNullOrWhiteSpace(req.ColorHex) && !IsValidHex(req.ColorHex))
                return BadRequest(new { success = false, message = "Colour must be a hex value like #16A34A." });

            try
            {
                using var conn = CreateConn();
                conn.Open();

                var p = new DynamicParameters();
                p.Add("p_LevelID",     req.LevelID);
                p.Add("p_LevelName",   req.LevelName.Trim());
                p.Add("p_Description", string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim());
                p.Add("p_ColorHex",    string.IsNullOrWhiteSpace(req.ColorHex)    ? null : req.ColorHex.Trim());
                p.Add("p_SortOrder",   req.SortOrder);
                p.Add("p_IsActive",    req.IsActive ? 1 : 0);
                p.Add("p_ActorEmail",  string.IsNullOrWhiteSpace(req.ActorEmail) ? "Admin" : req.ActorEmail.Trim());
                p.Add("p_ResultCode",  dbType: DbType.Int32,  direction: ParameterDirection.Output);
                p.Add("p_Message",     dbType: DbType.String, size: 500, direction: ParameterDirection.Output);
                p.Add("p_OutLevelID",  dbType: DbType.Int32,  direction: ParameterDirection.Output);

                await conn.ExecuteAsync("SP_Level_Save", p,
                    commandType: CommandType.StoredProcedure);

                int    code  = p.Get<int>("p_ResultCode");
                string msg   = p.Get<string>("p_Message") ?? "";
                int    outId = p.Get<int>("p_OutLevelID");

                return code == 1
                    ? Ok(new { success = true, message = msg, levelId = outId })
                    : BadRequest(new { success = false, message = msg });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving skill level");
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }


        // ════════════════════════════════════════════════════════
        // POST  api/skilllevel/toggle-status
        // ════════════════════════════════════════════════════════
        [HttpPost("toggle-status")]
        public async Task<IActionResult> ToggleStatus([FromBody] ToggleLevelApiRequest req)
        {
            if (req == null || req.LevelID <= 0)
                return BadRequest(new { success = false, message = "Invalid request." });

            try
            {
                using var conn = CreateConn();
                conn.Open();

                var p = new DynamicParameters();
                p.Add("p_LevelID",    req.LevelID);
                p.Add("p_ActorEmail", string.IsNullOrWhiteSpace(req.ActorEmail) ? "Admin" : req.ActorEmail.Trim());
                p.Add("p_ResultCode", dbType: DbType.Int32,  direction: ParameterDirection.Output);
                p.Add("p_Message",    dbType: DbType.String, size: 500, direction: ParameterDirection.Output);
                p.Add("p_NewStatus",  dbType: DbType.Byte,   direction: ParameterDirection.Output);

                await conn.ExecuteAsync("SP_Level_ToggleStatus", p,
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
                _logger.LogError(ex, "Error toggling skill level status");
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }


        // ════════════════════════════════════════════════════════
        // POST  api/skilllevel/delete
        // ════════════════════════════════════════════════════════
        [HttpPost("delete")]
        public async Task<IActionResult> Delete([FromBody] DeleteLevelApiRequest req)
        {
            if (req == null || req.LevelID <= 0)
                return BadRequest(new { success = false, message = "Invalid request." });

            try
            {
                using var conn = CreateConn();
                conn.Open();

                var p = new DynamicParameters();
                p.Add("p_LevelID",    req.LevelID);
                p.Add("p_ResultCode", dbType: DbType.Int32,  direction: ParameterDirection.Output);
                p.Add("p_Message",    dbType: DbType.String, size: 500, direction: ParameterDirection.Output);

                await conn.ExecuteAsync("SP_Level_Delete", p,
                    commandType: CommandType.StoredProcedure);

                int    code = p.Get<int>("p_ResultCode");
                string msg  = p.Get<string>("p_Message") ?? "";

                return code == 1
                    ? Ok(new { success = true, message = msg })
                    : BadRequest(new { success = false, message = msg });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting skill level");
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
    public class SaveLevelApiRequest
    {
        public int     LevelID     { get; set; }   // 0 = insert
        public string  LevelName   { get; set; } = "";
        public string? Description { get; set; }
        public string? ColorHex    { get; set; }
        public int     SortOrder   { get; set; }
        public bool    IsActive    { get; set; } = true;
        public string  ActorEmail  { get; set; } = "Admin";
    }

    public class ToggleLevelApiRequest
    {
        public int    LevelID    { get; set; }
        public string ActorEmail { get; set; } = "Admin";
    }

    public class DeleteLevelApiRequest
    {
        public int LevelID { get; set; }
    }
}

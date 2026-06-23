// ============================================================
// ProximaLMSAPI/Controllers/CategoryController.cs
// ------------------------------------------------------------
// Category master — admin CRUD + a lightweight lookup endpoint
// for forms (Course create/edit, filters, etc).
//
//   GET  /api/category/all              → admin grid (incl. inactive)
//   GET  /api/category/active           → lookup dropdown (active only)
//   GET  /api/category/{id}             → single category for edit
//   POST /api/category/save             → insert or update
//   POST /api/category/toggle-status    → activate / deactivate
//   POST /api/category/delete           → hard delete (blocked if in use)
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
    public class CategoryController : ControllerBase
    {
        private readonly IConfiguration _config;
        private readonly ILogger<CategoryController> _logger;

        public CategoryController(IConfiguration config, ILogger<CategoryController> logger)
        {
            _config = config;
            _logger = logger;
        }

        private IDbConnection CreateConn()
            => new MySqlConnection(_config.GetConnectionString("ConnectionString"));


        // ════════════════════════════════════════════════════════
        // GET  api/category/all?includeInactive=1
        // Admin grid — includes inactive when includeInactive=1.
        // ════════════════════════════════════════════════════════
        [HttpGet("all")]
        public async Task<IActionResult> GetAll([FromQuery] int includeInactive = 1)
        {
            try
            {
                using var conn = CreateConn();
                var rows = await conn.QueryAsync(
                    "SP_Category_GetAll",
                    new { p_IncludeInactive = includeInactive == 1 ? 1 : 0 },
                    commandType: CommandType.StoredProcedure);

                return Ok(rows.ToList());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading categories");
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }


        // ════════════════════════════════════════════════════════
        // GET  api/category/active
        // Lookup endpoint — active categories only, minimal fields.
        // Used by course forms and filter dropdowns.
        // ════════════════════════════════════════════════════════
        [HttpGet("active")]
        public async Task<IActionResult> GetActive()
        {
            try
            {
                using var conn = CreateConn();
                var rows = await conn.QueryAsync(
                    "SP_Category_GetAll",
                    new { p_IncludeInactive = 0 },
                    commandType: CommandType.StoredProcedure);

                return Ok(rows.ToList());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading active categories");
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }


        // ════════════════════════════════════════════════════════
        // GET  api/category/{id}
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
                    "SP_Category_GetById",
                    new { p_CategoryID = id },
                    commandType: CommandType.StoredProcedure);

                if (row == null)
                    return NotFound(new { success = false, message = "Category not found." });

                return Ok(row);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading category {Id}", id);
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }


        // ════════════════════════════════════════════════════════
        // POST  api/category/save
        // Body: { CategoryID, CategoryName, ParentCategoryID,
        //         Description, SortOrder, IsActive, ActorEmail }
        // CategoryID = 0 → insert; otherwise update.
        // ════════════════════════════════════════════════════════
        [HttpPost("save")]
        public async Task<IActionResult> Save([FromBody] SaveCategoryApiRequest req)
        {
            if (req == null)
                return BadRequest(new { success = false, message = "Invalid request." });

            if (string.IsNullOrWhiteSpace(req.CategoryName))
                return BadRequest(new { success = false, message = "Category name is required." });

            try
            {
                using var conn = CreateConn();
                conn.Open();

                var p = new DynamicParameters();
                p.Add("p_CategoryID",       req.CategoryID);
                p.Add("p_CategoryName",     req.CategoryName.Trim());
                p.Add("p_ParentCategoryID", req.ParentCategoryID);
                p.Add("p_Description",      string.IsNullOrWhiteSpace(req.Description)
                                                ? null : req.Description.Trim());
                p.Add("p_SortOrder",        req.SortOrder);
                p.Add("p_IsActive",         req.IsActive ? 1 : 0);
                p.Add("p_ActorEmail",       string.IsNullOrWhiteSpace(req.ActorEmail)
                                                ? "Admin" : req.ActorEmail.Trim());
                p.Add("p_ResultCode",    dbType: DbType.Int32,  direction: ParameterDirection.Output);
                p.Add("p_Message",       dbType: DbType.String, size: 500, direction: ParameterDirection.Output);
                p.Add("p_OutCategoryID", dbType: DbType.Int32,  direction: ParameterDirection.Output);

                await conn.ExecuteAsync("SP_Category_Save", p,
                    commandType: CommandType.StoredProcedure);

                int    code = p.Get<int>("p_ResultCode");
                string msg  = p.Get<string>("p_Message") ?? "";
                int    outId = p.Get<int>("p_OutCategoryID");

                return code == 1
                    ? Ok(new { success = true, message = msg, categoryId = outId })
                    : BadRequest(new { success = false, message = msg });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving category");
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }


        // ════════════════════════════════════════════════════════
        // POST  api/category/toggle-status
        // Body: { CategoryID, ActorEmail }
        // ════════════════════════════════════════════════════════
        [HttpPost("toggle-status")]
        public async Task<IActionResult> ToggleStatus([FromBody] ToggleStatusApiRequest req)
        {
            if (req == null || req.CategoryID <= 0)
                return BadRequest(new { success = false, message = "Invalid request." });

            try
            {
                using var conn = CreateConn();
                conn.Open();

                var p = new DynamicParameters();
                p.Add("p_CategoryID", req.CategoryID);
                p.Add("p_ActorEmail", string.IsNullOrWhiteSpace(req.ActorEmail)
                                          ? "Admin" : req.ActorEmail.Trim());
                p.Add("p_ResultCode", dbType: DbType.Int32,  direction: ParameterDirection.Output);
                p.Add("p_Message",    dbType: DbType.String, size: 500, direction: ParameterDirection.Output);
                p.Add("p_NewStatus",  dbType: DbType.Byte,   direction: ParameterDirection.Output);

                await conn.ExecuteAsync("SP_Category_ToggleStatus", p,
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
                _logger.LogError(ex, "Error toggling category status");
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }


        // ════════════════════════════════════════════════════════
        // POST  api/category/delete
        // Body: { CategoryID }
        // ════════════════════════════════════════════════════════
        [HttpPost("delete")]
        public async Task<IActionResult> Delete([FromBody] DeleteCategoryApiRequest req)
        {
            if (req == null || req.CategoryID <= 0)
                return BadRequest(new { success = false, message = "Invalid request." });

            try
            {
                using var conn = CreateConn();
                conn.Open();

                var p = new DynamicParameters();
                p.Add("p_CategoryID", req.CategoryID);
                p.Add("p_ResultCode", dbType: DbType.Int32,  direction: ParameterDirection.Output);
                p.Add("p_Message",    dbType: DbType.String, size: 500, direction: ParameterDirection.Output);

                await conn.ExecuteAsync("SP_Category_Delete", p,
                    commandType: CommandType.StoredProcedure);

                int    code = p.Get<int>("p_ResultCode");
                string msg  = p.Get<string>("p_Message") ?? "";

                return code == 1
                    ? Ok(new { success = true, message = msg })
                    : BadRequest(new { success = false, message = msg });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting category");
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }
    }


    // ── Request DTOs ──────────────────────────────────────────
    public class SaveCategoryApiRequest
    {
        public int     CategoryID       { get; set; }   // 0 = insert
        public string  CategoryName     { get; set; } = "";
        public int     ParentCategoryID { get; set; }   // 0 = top level
        public string? Description      { get; set; }
        public int     SortOrder        { get; set; }
        public bool    IsActive         { get; set; } = true;
        public string  ActorEmail       { get; set; } = "Admin";
    }

    public class ToggleStatusApiRequest
    {
        public int    CategoryID { get; set; }
        public string ActorEmail { get; set; } = "Admin";
    }

    public class DeleteCategoryApiRequest
    {
        public int CategoryID { get; set; }
    }
}

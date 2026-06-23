// ============================================================
// ProximaLMSAPI/Controllers/AuditController.cs
// ------------------------------------------------------------
// FIX (May 2026):
//   • Search now sends p_FromDate, p_ToDate, p_ActorText, p_Action,
//     p_EntityType, p_Outcome, p_PageNumber, p_PageSize — matching
//     SP_AuditLog_Search exactly. (Was sending wrong names: p_Search,
//     p_ActionType, p_EntityName, p_ActorUserID, p_Page → silent fail.)
//   • GetFilters now reads result sets in correct order: Action FIRST,
//     EntityType SECOND (matches SP_AuditLog_GetFilters output).
//
// Read-only query endpoints for the admin audit screen.
//   GET /api/audit/search   → paged + filtered list
//   GET /api/audit/filters  → distinct actions + entity types
//   GET /api/audit/stats    → header tiles
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
    public class AuditController : ControllerBase
    {
        private readonly IConfiguration _config;
        private readonly ILogger<AuditController> _logger;

        public AuditController(IConfiguration config, ILogger<AuditController> logger)
        {
            _config = config;
            _logger = logger;
        }

        private IDbConnection CreateConn()
            => new MySqlConnection(_config.GetConnectionString("ConnectionString"));


        // ════════════════════════════════════════════════════════
        // GET  api/audit/search
        // Query: fromDate, toDate, actorText (email substring),
        //        actionType, entityName, outcome, page, pageSize
        // Returns: { success, rows, totalCount, page, pageSize }
        //
        // Names from the UI/old MVC controller are kept (actionType,
        // entityName, search) and mapped to the SP's actual parameter
        // names so the front-end doesn't have to change.
        // ════════════════════════════════════════════════════════
        [HttpGet("search")]
        public async Task<IActionResult> Search(
            [FromQuery] DateTime? fromDate,
            [FromQuery] DateTime? toDate,
            [FromQuery] string entityName = "",   // maps to p_EntityType
            [FromQuery] string actionType = "",   // maps to p_Action
            [FromQuery] string search = "",   // maps to p_ActorText (email contains)
            [FromQuery] string outcome = "",   // SUCCESS | FAILURE | ''
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 25)
        {
            try
            {
                using var conn = CreateConn();

                var p = new DynamicParameters();
                p.Add("p_FromDate", fromDate);
                p.Add("p_ToDate", toDate);
                p.Add("p_ActorText", search ?? "");
                p.Add("p_Action", actionType ?? "");
                p.Add("p_EntityType", entityName ?? "");
                p.Add("p_Outcome", outcome ?? "");
                p.Add("p_PageNumber", page);
                p.Add("p_PageSize", pageSize);

                using var multi = await conn.QueryMultipleAsync(
                    "SP_AuditLog_Search", p,
                    commandType: CommandType.StoredProcedure);

                var rows = (await multi.ReadAsync()).ToList();
                // SP returns a row { TotalCount } as the second set.
                var total = await multi.ReadFirstOrDefaultAsync<int>();

                return Ok(new
                {
                    success = true,
                    rows = rows,
                    totalCount = total,
                    page = page,
                    pageSize = pageSize
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Audit search failed");
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }


        // ════════════════════════════════════════════════════════
        // GET  api/audit/filters
        // Returns: { entities: [...], actionTypes: [...] }
        //
        // FIX: SP_AuditLog_GetFilters returns Action list FIRST,
        // EntityType list SECOND. Old code reversed these.
        // ════════════════════════════════════════════════════════
        [HttpGet("filters")]
        public async Task<IActionResult> GetFilters()
        {
            try
            {
                using var conn = CreateConn();

                using var multi = await conn.QueryMultipleAsync(
                    "SP_AuditLog_GetFilters",
                    commandType: CommandType.StoredProcedure);

                // ✅ Read in SP order: Action first, EntityType second
                var actionTypes = (await multi.ReadAsync<string>()).ToList();
                var entities = (await multi.ReadAsync<string>()).ToList();

                return Ok(new { entities, actionTypes });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Audit filters failed");
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }


        // ════════════════════════════════════════════════════════
        // GET  api/audit/stats
        // SP returns: { TotalEvents, Last24h, Last7d, FailureCount }
        // ════════════════════════════════════════════════════════
        [HttpGet("stats")]
        public async Task<IActionResult> GetStats()
        {
            try
            {
                using var conn = CreateConn();
                var row = await conn.QueryFirstOrDefaultAsync(
                    "SP_AuditLog_Stats",
                    commandType: CommandType.StoredProcedure);

                return Ok(row ?? new
                {
                    TotalEvents = 0,
                    Last24h = 0,
                    Last7d = 0,
                    FailureCount = 0
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Audit stats failed");
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }
    }
}

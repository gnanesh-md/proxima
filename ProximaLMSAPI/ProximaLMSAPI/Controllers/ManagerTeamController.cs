// ============================================================
// ProximaLMSAPI/Controllers/ManagerTeamController.cs
// ------------------------------------------------------------
// Lightweight, opt-in manager → employee mapping for module 08.
//
//   GET  /api/managerteam/members/{managerId}   → team members
//   POST /api/managerteam/add                   → add a member
//   POST /api/managerteam/remove                → soft-remove a member
//   GET  /api/managerteam/has-team/{userId}     → capability check (TeamSize)
// ============================================================
using Dapper;
using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
using System.Data;

namespace ProximaLMSAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ManagerTeamController : ControllerBase
    {
        private readonly IConfiguration _config;
        public ManagerTeamController(IConfiguration config) => _config = config;

        private IDbConnection CreateConn()
            => new MySqlConnection(_config.GetConnectionString("ConnectionString"));

        [HttpGet("members/{managerId:int}")]
        public async Task<IActionResult> GetMembers(int managerId)
        {
            if (managerId <= 0) return BadRequest(new { success = false, message = "Invalid manager id." });

            try
            {
                using var conn = CreateConn();
                var rows = await conn.QueryAsync(
                    "SP_ManagerTeam_GetMembers",
                    new { p_ManagerID = managerId },
                    commandType: CommandType.StoredProcedure);
                return Ok(rows.ToList());
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        [HttpPost("add")]
        public async Task<IActionResult> AddMember([FromBody] TeamMemberRequest req)
        {
            if (req == null || req.ManagerID <= 0 || req.EmployeeID <= 0)
                return BadRequest(new { success = false, message = "Invalid request." });

            if (req.ManagerID == req.EmployeeID)
                return BadRequest(new { success = false, message = "A user cannot manage themselves." });

            try
            {
                using var conn = CreateConn();
                await conn.ExecuteAsync(
                    "SP_ManagerTeam_AddMember",
                    new
                    {
                        p_ManagerID = req.ManagerID,
                        p_EmployeeID = req.EmployeeID,
                        p_AddedBy = req.AddedBy ?? "Admin"
                    },
                    commandType: CommandType.StoredProcedure);
                return Ok(new { success = true, message = "Added to team." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        [HttpPost("remove")]
        public async Task<IActionResult> RemoveMember([FromBody] TeamMemberRequest req)
        {
            if (req == null || req.ManagerID <= 0 || req.EmployeeID <= 0)
                return BadRequest(new { success = false, message = "Invalid request." });

            try
            {
                using var conn = CreateConn();
                await conn.ExecuteAsync(
                    "SP_ManagerTeam_RemoveMember",
                    new { p_ManagerID = req.ManagerID, p_EmployeeID = req.EmployeeID },
                    commandType: CommandType.StoredProcedure);
                return Ok(new { success = true, message = "Removed from team." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        // Returns { teamSize: N }. Use it to detect manager capability for the
        // logged-in user: teamSize > 0 ⇒ show the manager UI.
        [HttpGet("has-team/{userId:int}")]
        public async Task<IActionResult> HasTeam(int userId)
        {
            if (userId <= 0) return BadRequest(new { success = false, message = "Invalid user id." });

            try
            {
                using var conn = CreateConn();
                var row = await conn.QueryFirstOrDefaultAsync(
                    "SP_ManagerTeam_HasTeam",
                    new { p_ManagerID = userId },
                    commandType: CommandType.StoredProcedure);
                return Ok(row ?? new { teamSize = 0 });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }
    }

    public class TeamMemberRequest
    {
        public int ManagerID { get; set; }
        public int EmployeeID { get; set; }
        public string AddedBy { get; set; }
    }
}

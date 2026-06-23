using Dapper;
using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
using System.Data;

namespace ProximaLMSAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class RoleController : ControllerBase
    {
        private readonly IConfiguration _config;

        public RoleController(IConfiguration config)
        {
            _config = config;
        }

        private IDbConnection CreateConn()
            => new MySqlConnection(_config.GetConnectionString("ConnectionString"));

        // ─────────────────────────────────────────
        // GET api/role/list
        // Returns: plain JSON array  [ {...}, {...} ]
        // ─────────────────────────────────────────
        [HttpGet("list")]
        public async Task<IActionResult> GetList()
        {
            try
            {
                using var conn = CreateConn();

                // QueryAsync returns IEnumerable<dynamic> → ToList() → serialized as plain array
                var rows = await conn.QueryAsync(
                    "USP_Role_GetAll",
                    commandType: CommandType.StoredProcedure);

                return Ok(rows.ToList());   // ← .ToList() forces plain JSON array
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        // ─────────────────────────────────────────
        // GET api/role/{id}
        // Returns: single object  { RoleID, RoleCode, ... }
        // ─────────────────────────────────────────
        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetById(int id)
        {
            try
            {
                using var conn = CreateConn();
                var row = await conn.QueryFirstOrDefaultAsync(
                    "USP_Role_GetByID",
                    new { p_RoleID = id },
                    commandType: CommandType.StoredProcedure);

                if (row == null)
                    return NotFound(new { success = false, message = "Role not found." });

                return Ok(row);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        // ─────────────────────────────────────────
        // POST api/role/save
        // ─────────────────────────────────────────
        [HttpPost("save")]
        public async Task<IActionResult> Save([FromBody] RoleSaveRequest req)
        {
            try
            {
                using var conn = CreateConn();
                conn.Open();

                var p = new DynamicParameters();
                p.Add("p_RoleID", req.RoleID);
                p.Add("p_RoleCode", req.RoleCode?.Trim().ToUpper());
                p.Add("p_RoleName", req.RoleName?.Trim());
                p.Add("p_Description", req.Description?.Trim());
                p.Add("p_IsActive", req.IsActive ? 1 : 0);
                p.Add("p_ActionBy", req.ActionBy ?? "System");
                p.Add("p_ResultCode", dbType: DbType.Int32, direction: ParameterDirection.Output);
                p.Add("p_OutRoleID", dbType: DbType.Int32, direction: ParameterDirection.Output);
                p.Add("p_Message", dbType: DbType.String, size: 500, direction: ParameterDirection.Output);

                await conn.ExecuteAsync("SP_Role_Save", p, commandType: CommandType.StoredProcedure);

                int code = p.Get<int>("p_ResultCode");
                int roleId = p.Get<int>("p_OutRoleID");
                string msg = p.Get<string>("p_Message") ?? "";

                // code 1 = inserted, 2 = updated, -1 = duplicate
                if (code == 1 || code == 2)
                    return Ok(new { success = true, message = msg, roleId });

                return BadRequest(new { success = false, message = msg });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        // ─────────────────────────────────────────
        // POST api/role/toggle-status
        // ─────────────────────────────────────────
        [HttpPost("toggle-status")]
        public async Task<IActionResult> ToggleStatus([FromBody] RoleToggleRequest req)
        {
            try
            {
                using var conn = CreateConn();
                conn.Open();

                var p = new DynamicParameters();
                p.Add("p_RoleID", req.RoleID);
                p.Add("p_IsActive", req.IsActive ? 1 : 0);
                p.Add("p_ActionBy", req.ActionBy ?? "System");
                p.Add("p_ResultCode", dbType: DbType.Int32, direction: ParameterDirection.Output);
                p.Add("p_Message", dbType: DbType.String, size: 500, direction: ParameterDirection.Output);

                await conn.ExecuteAsync("SP_Role_ToggleStatus", p, commandType: CommandType.StoredProcedure);

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
        // POST api/role/delete
        // ─────────────────────────────────────────
        [HttpPost("delete")]
        public async Task<IActionResult> Delete([FromBody] RoleDeleteRequest req)
        {
            try
            {
                using var conn = CreateConn();
                conn.Open();

                var p = new DynamicParameters();
                p.Add("p_RoleID", req.RoleID);
                p.Add("p_ActionBy", req.ActionBy ?? "System");
                p.Add("p_ResultCode", dbType: DbType.Int32, direction: ParameterDirection.Output);
                p.Add("p_Message", dbType: DbType.String, size: 500, direction: ParameterDirection.Output);

                await conn.ExecuteAsync("SP_Role_Delete", p, commandType: CommandType.StoredProcedure);

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
        // GET api/role/{id}/permissions
        // Returns: plain JSON array
        // ─────────────────────────────────────────
        [HttpGet("{id:int}/permissions")]
        public async Task<IActionResult> GetPermissions(int id)
        {
            try
            {
                using var conn = CreateConn();
                var rows = await conn.QueryAsync(
                    "USP_RolePermissions_GetByRoleID",
                    new { p_RoleID = id },
                    commandType: CommandType.StoredProcedure);

                return Ok(rows.ToList());
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        // ─────────────────────────────────────────
        // POST api/role/permissions/save
        // ─────────────────────────────────────────
        [HttpPost("permissions/save")]
        public async Task<IActionResult> SavePermissions([FromBody] SavePermissionsApiRequest req)
        {
            if (req?.Permissions == null || !req.Permissions.Any())
                return BadRequest(new { success = false, message = "No permissions provided." });

            try
            {
                using var conn = CreateConn();
                conn.Open();

                foreach (var perm in req.Permissions)
                {
                    var p = new DynamicParameters();
                    p.Add("p_RoleID", req.RoleID);
                    p.Add("p_ScreenID", perm.ScreenID);
                    p.Add("p_CanView", perm.CanView ? 1 : 0);
                    p.Add("p_CanCreate", perm.CanCreate ? 1 : 0);
                    p.Add("p_CanEdit", perm.CanEdit ? 1 : 0);
                    p.Add("p_CanDelete", perm.CanDelete ? 1 : 0);
                    p.Add("p_ActionBy", req.ActionBy ?? "System");

                    await conn.ExecuteAsync(
                        "SP_RolePermissions_Save", p,
                        commandType: CommandType.StoredProcedure);
                }

                return Ok(new
                {
                    success = true,
                    message = $"{req.Permissions.Count} permission(s) saved successfully."
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        // ─────────────────────────────────────────
        // GET api/role/{id}/users
        // Returns: plain JSON array
        // ─────────────────────────────────────────
        [HttpGet("{id:int}/users")]
        public async Task<IActionResult> GetUsers(int id)
        {
            try
            {
                using var conn = CreateConn();
                var rows = await conn.QueryAsync(
                    "SP_Role_GetUsers",
                    new { p_RoleID = id },
                    commandType: CommandType.StoredProcedure);

                return Ok(rows.ToList());
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        // ─────────────────────────────────────────
        // POST api/role/assign-user
        // ─────────────────────────────────────────
        [HttpPost("assign-user")]
        public async Task<IActionResult> AssignUser([FromBody] RoleUserRequest req)
        {
            try
            {
                using var conn = CreateConn();
                conn.Open();

                var p = new DynamicParameters();
                p.Add("p_RoleID", req.RoleID);
                p.Add("p_UserID", req.UserID);
                p.Add("p_ActionBy", req.ActionBy ?? "System");
                p.Add("p_ResultCode", dbType: DbType.Int32, direction: ParameterDirection.Output);
                p.Add("p_Message", dbType: DbType.String, size: 500, direction: ParameterDirection.Output);

                await conn.ExecuteAsync("SP_Role_AssignUser", p, commandType: CommandType.StoredProcedure);

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
        // POST api/role/remove-user
        // ─────────────────────────────────────────
        [HttpPost("remove-user")]
        public async Task<IActionResult> RemoveUser([FromBody] RoleUserRequest req)
        {
            try
            {
                using var conn = CreateConn();
                conn.Open();

                var p = new DynamicParameters();
                p.Add("p_RoleID", req.RoleID);
                p.Add("p_UserID", req.UserID);
                p.Add("p_ActionBy", req.ActionBy ?? "System");
                p.Add("p_ResultCode", dbType: DbType.Int32, direction: ParameterDirection.Output);
                p.Add("p_Message", dbType: DbType.String, size: 500, direction: ParameterDirection.Output);

                await conn.ExecuteAsync("SP_Role_RemoveUser", p, commandType: CommandType.StoredProcedure);

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
    }

    // ══════════════════════════════════════════
    // REQUEST DTOs
    // ══════════════════════════════════════════
    public class RoleSaveRequest
    {
        public int RoleID { get; set; }
        public string RoleCode { get; set; }
        public string RoleName { get; set; }
        public string Description { get; set; }
        public bool IsActive { get; set; }
        public string ActionBy { get; set; }
    }

    public class RoleToggleRequest
    {
        public int RoleID { get; set; }
        public bool IsActive { get; set; }
        public string ActionBy { get; set; }
    }

    public class RoleDeleteRequest
    {
        public int RoleID { get; set; }
        public string ActionBy { get; set; }
    }

    public class RoleUserRequest
    {
        public int RoleID { get; set; }
        public int UserID { get; set; }
        public string ActionBy { get; set; }
    }

    public class SavePermissionsApiRequest
    {
        public int RoleID { get; set; }
        public string ActionBy { get; set; }
        public List<PermissionItem> Permissions { get; set; } = new();
    }

    public class PermissionItem
    {
        public int ScreenID { get; set; }
        public bool CanView { get; set; }
        public bool CanCreate { get; set; }
        public bool CanEdit { get; set; }
        public bool CanDelete { get; set; }
    }
}
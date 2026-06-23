// ============================================================
// ProximaLMSAPI/Controllers/BadgeController.cs
// ============================================================
using Dapper;
using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
using System.Data;

namespace ProximaLMSAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class BadgeController : ControllerBase
    {
        private readonly IConfiguration _config;
        public BadgeController(IConfiguration config) => _config = config;
        private IDbConnection Conn() => new MySqlConnection(_config.GetConnectionString("ConnectionString"));

        [HttpGet("list")]
        public async Task<IActionResult> List()
        {
            using var conn = Conn();
            var rows = await conn.QueryAsync(
                "SP_Badge_List", commandType: CommandType.StoredProcedure);
            return Ok(new { success = true, data = rows });
        }

        [HttpPost("save")]
        public async Task<IActionResult> Save([FromBody] BadgeDto dto)
        {
            using var conn = Conn();
            var p = new DynamicParameters();
            p.Add("p_ID",            dto.BadgeID);
            p.Add("p_BadgeName",     dto.BadgeName);
            p.Add("p_Description",   dto.Description ?? "");
            p.Add("p_IconClass",     dto.IconClass ?? "fa-award");
            p.Add("p_BadgeColor",    dto.BadgeColor ?? "#7B2CBF");
            p.Add("p_CriteriaType",  dto.CriteriaType);
            p.Add("p_CriteriaValue", dto.CriteriaValue);
            p.Add("p_SortOrder",     dto.SortOrder);
            p.Add("p_IsActive",      dto.IsActive ? 1 : 0);
            p.Add("p_ResultCode",    dbType: DbType.Int32, direction: ParameterDirection.Output);

            await conn.ExecuteAsync("SP_Badge_Save", p, commandType: CommandType.StoredProcedure);
            return Ok(new { success = p.Get<int>("p_ResultCode") == 1, message = "Badge saved." });
        }

        [HttpPost("delete")]
        public async Task<IActionResult> Delete([FromBody] IdRequest req)
        {
            using var conn = Conn();
            var p = new DynamicParameters();
            p.Add("p_ID",         req.ID);
            p.Add("p_ResultCode", dbType: DbType.Int32, direction: ParameterDirection.Output);
            await conn.ExecuteAsync("SP_Badge_Delete", p, commandType: CommandType.StoredProcedure);
            return Ok(new { success = p.Get<int>("p_ResultCode") > 0 });
        }

        public class BadgeDto
        {
            public int     BadgeID       { get; set; }
            public string  BadgeName     { get; set; } = "";
            public string? Description   { get; set; }
            public string? IconClass     { get; set; }
            public string? BadgeColor    { get; set; }
            public string  CriteriaType  { get; set; } = "POINTS_TOTAL";
            public int     CriteriaValue { get; set; }
            public int     SortOrder     { get; set; }
            public bool    IsActive      { get; set; } = true;
        }

        public class IdRequest { public int ID { get; set; } }
    }
}

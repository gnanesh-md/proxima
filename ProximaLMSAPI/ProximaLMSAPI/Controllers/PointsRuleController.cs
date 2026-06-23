// ============================================================
// ProximaLMSAPI/Controllers/PointsRuleController.cs
// ============================================================
using Dapper;
using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
using System.Data;

namespace ProximaLMSAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class PointsRuleController : ControllerBase
    {
        private readonly IConfiguration _config;
        public PointsRuleController(IConfiguration config) => _config = config;
        private IDbConnection Conn() => new MySqlConnection(_config.GetConnectionString("ConnectionString"));

        [HttpGet("list")]
        public async Task<IActionResult> List()
        {
            using var conn = Conn();
            var rows = await conn.QueryAsync(
                "SP_PointsRule_List", commandType: CommandType.StoredProcedure);
            return Ok(new { success = true, data = rows });
        }

        [HttpPost("save")]
        public async Task<IActionResult> Save([FromBody] PointsRuleDto dto)
        {
            using var conn = Conn();
            var p = new DynamicParameters();
            p.Add("p_ID",          dto.RuleID);
            p.Add("p_ActionCode",  dto.ActionCode);
            p.Add("p_ActionLabel", dto.ActionLabel);
            p.Add("p_Points",      dto.Points);
            p.Add("p_Description", dto.Description ?? "");
            p.Add("p_IsActive",    dto.IsActive ? 1 : 0);
            p.Add("p_ResultCode",  dbType: DbType.Int32, direction: ParameterDirection.Output);

            await conn.ExecuteAsync("SP_PointsRule_Save", p, commandType: CommandType.StoredProcedure);
            int code = p.Get<int>("p_ResultCode");
            return code == 1
                ? Ok(new      { success = true,  message = "Rule saved." })
                : BadRequest(new { success = false, message = "Action code already exists." });
        }

        [HttpPost("delete")]
        public async Task<IActionResult> Delete([FromBody] IdRequest req)
        {
            using var conn = Conn();
            var p = new DynamicParameters();
            p.Add("p_ID",         req.ID);
            p.Add("p_ResultCode", dbType: DbType.Int32, direction: ParameterDirection.Output);
            await conn.ExecuteAsync("SP_PointsRule_Delete", p, commandType: CommandType.StoredProcedure);
            return Ok(new { success = p.Get<int>("p_ResultCode") > 0 });
        }

        public class PointsRuleDto
        {
            public int     RuleID      { get; set; }
            public string  ActionCode  { get; set; } = "";
            public string  ActionLabel { get; set; } = "";
            public int     Points      { get; set; }
            public string? Description { get; set; }
            public bool    IsActive    { get; set; } = true;
        }

        public class IdRequest { public int ID { get; set; } }
    }
}

// ============================================================
// ProximaLMSAPI/Controllers/LanguageController.cs
// ============================================================
using Dapper;
using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
using System.Data;

namespace ProximaLMSAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class LanguageController : ControllerBase
    {
        private readonly IConfiguration _config;
        public LanguageController(IConfiguration config) => _config = config;
        private IDbConnection Conn() => new MySqlConnection(_config.GetConnectionString("ConnectionString"));

        [HttpGet("list")]
        public async Task<IActionResult> List()
        {
            using var conn = Conn();
            var rows = await conn.QueryAsync(
                "SP_Language_List", commandType: CommandType.StoredProcedure);
            return Ok(new { success = true, data = rows });
        }

        [HttpPost("save")]
        public async Task<IActionResult> Save([FromBody] LanguageDto dto)
        {
            using var conn = Conn();
            var p = new DynamicParameters();
            p.Add("p_ID",           dto.LanguageID);
            p.Add("p_LanguageName", dto.LanguageName);
            p.Add("p_LanguageCode", dto.LanguageCode);
            p.Add("p_NativeName",   dto.NativeName ?? "");
            p.Add("p_SortOrder",    dto.SortOrder);
            p.Add("p_IsActive",     dto.IsActive ? 1 : 0);
            p.Add("p_ResultCode",   dbType: DbType.Int32, direction: ParameterDirection.Output);

            await conn.ExecuteAsync("SP_Language_Save", p, commandType: CommandType.StoredProcedure);
            int code = p.Get<int>("p_ResultCode");

            return code == 1
                ? Ok(new      { success = true,  message = "Language saved." })
                : BadRequest(new { success = false, message = "Language code already exists." });
        }

        [HttpPost("delete")]
        public async Task<IActionResult> Delete([FromBody] IdRequest req)
        {
            using var conn = Conn();
            var p = new DynamicParameters();
            p.Add("p_ID",         req.ID);
            p.Add("p_ResultCode", dbType: DbType.Int32, direction: ParameterDirection.Output);

            await conn.ExecuteAsync("SP_Language_Delete", p, commandType: CommandType.StoredProcedure);
            return Ok(new { success = p.Get<int>("p_ResultCode") > 0 });
        }

        public class LanguageDto
        {
            public int    LanguageID    { get; set; }
            public string LanguageName  { get; set; } = "";
            public string LanguageCode  { get; set; } = "";
            public string? NativeName   { get; set; }
            public int    SortOrder     { get; set; }
            public bool   IsActive      { get; set; } = true;
        }

        public class IdRequest { public int ID { get; set; } }
    }
}

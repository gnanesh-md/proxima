// ============================================================
// ProximaLMSAPI/Controllers/CertificateTemplateController.cs
// ============================================================
using Dapper;
using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
using System.Data;

namespace ProximaLMSAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CertificateTemplateController : ControllerBase
    {
        private readonly IConfiguration _config;
        public CertificateTemplateController(IConfiguration config) => _config = config;
        private IDbConnection Conn() => new MySqlConnection(_config.GetConnectionString("ConnectionString"));

        [HttpGet("list")]
        public async Task<IActionResult> List()
        {
            using var conn = Conn();
            var rows = await conn.QueryAsync(
                "SP_CertTemplate_List", commandType: CommandType.StoredProcedure);
            return Ok(new { success = true, data = rows });
        }

        [HttpGet("{id:int}")]
        public async Task<IActionResult> Get(int id)
        {
            using var conn = Conn();
            var row = await conn.QuerySingleOrDefaultAsync(
                "SP_CertTemplate_Get", new { p_ID = id },
                commandType: CommandType.StoredProcedure);
            return row == null
                ? NotFound(new { success = false, message = "Not found." })
                : Ok(new { success = true, data = row });
        }

        [HttpPost("save")]
        public async Task<IActionResult> Save([FromBody] CertDto dto)
        {
            using var conn = Conn();
            var p = new DynamicParameters();
            p.Add("p_ID",                 dto.TemplateID);
            p.Add("p_TemplateName",       dto.TemplateName);
            p.Add("p_HtmlBody",           dto.HtmlBody);
            p.Add("p_BackgroundImageUrl", dto.BackgroundImageUrl ?? "");
            p.Add("p_Orientation",        dto.Orientation ?? "landscape");
            p.Add("p_IsDefault",          dto.IsDefault ? 1 : 0);
            p.Add("p_IsActive",           dto.IsActive  ? 1 : 0);
            p.Add("p_ResultCode", dbType: DbType.Int32, direction: ParameterDirection.Output);
            p.Add("p_NewID",      dbType: DbType.Int32, direction: ParameterDirection.Output);

            await conn.ExecuteAsync("SP_CertTemplate_Save", p, commandType: CommandType.StoredProcedure);
            return Ok(new
            {
                success = p.Get<int>("p_ResultCode") == 1,
                id      = p.Get<int>("p_NewID")
            });
        }

        [HttpPost("delete")]
        public async Task<IActionResult> Delete([FromBody] IdRequest req)
        {
            using var conn = Conn();
            var p = new DynamicParameters();
            p.Add("p_ID",         req.ID);
            p.Add("p_ResultCode", dbType: DbType.Int32, direction: ParameterDirection.Output);

            await conn.ExecuteAsync("SP_CertTemplate_Delete", p, commandType: CommandType.StoredProcedure);
            int code = p.Get<int>("p_ResultCode");
            return code == -1
                ? BadRequest(new { success = false, message = "Cannot delete the default template." })
                : Ok(new { success = code > 0 });
        }

        public class CertDto
        {
            public int     TemplateID         { get; set; }
            public string  TemplateName       { get; set; } = "";
            public string  HtmlBody           { get; set; } = "";
            public string? BackgroundImageUrl { get; set; }
            public string? Orientation        { get; set; }
            public bool    IsDefault          { get; set; }
            public bool    IsActive           { get; set; } = true;
        }

        public class IdRequest { public int ID { get; set; } }
    }
}

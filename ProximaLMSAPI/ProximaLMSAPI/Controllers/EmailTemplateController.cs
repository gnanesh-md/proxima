// ============================================================
// ProximaLMSAPI/Controllers/EmailTemplateController.cs
// ============================================================
using Dapper;
using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
using System.Data;
using System.Text.RegularExpressions;

namespace ProximaLMSAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class EmailTemplateController : ControllerBase
    {
        private readonly IConfiguration _config;
        public EmailTemplateController(IConfiguration config) => _config = config;
        private IDbConnection Conn() => new MySqlConnection(_config.GetConnectionString("ConnectionString"));

        [HttpGet("list")]
        public async Task<IActionResult> List()
        {
            using var conn = Conn();
            var rows = await conn.QueryAsync(
                "SP_EmailTemplate_List", commandType: CommandType.StoredProcedure);
            return Ok(new { success = true, data = rows });
        }

        [HttpGet("{id:int}")]
        public async Task<IActionResult> Get(int id)
        {
            using var conn = Conn();
            var row = await conn.QuerySingleOrDefaultAsync(
                "SP_EmailTemplate_Get", new { p_ID = id },
                commandType: CommandType.StoredProcedure);
            return row == null
                ? NotFound(new { success = false, message = "Not found." })
                : Ok(new { success = true, data = row });
        }

        [HttpGet("by-code/{code}")]
        public async Task<IActionResult> GetByCode(string code)
        {
            using var conn = Conn();
            var row = await conn.QuerySingleOrDefaultAsync(
                "SP_EmailTemplate_GetByCode", new { p_Code = code },
                commandType: CommandType.StoredProcedure);
            return row == null
                ? NotFound(new { success = false, message = "Not found." })
                : Ok(new { success = true, data = row });
        }

        [HttpPost("save")]
        public async Task<IActionResult> Save([FromBody] EmailTemplateDto dto)
        {
            using var conn = Conn();
            var p = new DynamicParameters();
            p.Add("p_ID",           dto.TemplateID);
            p.Add("p_TemplateCode", dto.TemplateCode);
            p.Add("p_TemplateName", dto.TemplateName);
            p.Add("p_Subject",      dto.Subject);
            p.Add("p_HtmlBody",     dto.HtmlBody);
            p.Add("p_Variables",    dto.Variables ?? "");
            p.Add("p_IsActive",     dto.IsActive ? 1 : 0);
            p.Add("p_ResultCode",   dbType: DbType.Int32, direction: ParameterDirection.Output);

            await conn.ExecuteAsync("SP_EmailTemplate_Save", p, commandType: CommandType.StoredProcedure);
            int code = p.Get<int>("p_ResultCode");
            return code == 1
                ? Ok(new      { success = true,  message = "Template saved." })
                : BadRequest(new { success = false, message = "Template code already exists." });
        }

        public class EmailTemplateDto
        {
            public int     TemplateID   { get; set; }
            public string  TemplateCode { get; set; } = "";
            public string  TemplateName { get; set; } = "";
            public string  Subject      { get; set; } = "";
            public string  HtmlBody     { get; set; } = "";
            public string? Variables    { get; set; }
            public bool    IsActive     { get; set; } = true;
        }
    }


    // ════════════════════════════════════════════════════════════
    // TemplateMerger
    // ------------------------------------------------------------
    // Use this in your LoginController to replace BuildOtpEmailHtml.
    //
    //   1. Fetch the template by code (Dapper + SP_EmailTemplate_GetByCode)
    //   2. Call TemplateMerger.Merge(html, variables) — returns rendered HTML
    //   3. Send it via SmtpClient as before.
    //
    // Example replacement for BuildOtpEmailHtml:
    //
    //   var tpl = await conn.QuerySingleOrDefaultAsync(
    //       "SP_EmailTemplate_GetByCode",
    //       new { p_Code = "OTP_LOGIN" },
    //       commandType: CommandType.StoredProcedure);
    //   string subject = (string)tpl.Subject;
    //   string html    = (string)tpl.HtmlBody;
    //   var data = new Dictionary<string, string> {
    //       ["UserName"] = userName,
    //       ["OTP"]      = otp,
    //       ["Year"]     = DateTime.Now.Year.ToString()
    //   };
    //   subject = TemplateMerger.Merge(subject, data);
    //   html    = TemplateMerger.Merge(html,    data);
    // ════════════════════════════════════════════════════════════
    public static class TemplateMerger
    {
        // Matches {{Variable}} — letters, digits, underscores allowed inside.
        private static readonly Regex Token = new(
            @"\{\{\s*([A-Za-z_][A-Za-z0-9_]*)\s*\}\}",
            RegexOptions.Compiled);

        public static string Merge(string template, IDictionary<string, string> values)
        {
            if (string.IsNullOrEmpty(template) || values == null) return template ?? "";

            return Token.Replace(template, m =>
            {
                string key = m.Groups[1].Value;
                return values.TryGetValue(key, out var v) ? (v ?? "") : m.Value;
            });
        }
    }
}

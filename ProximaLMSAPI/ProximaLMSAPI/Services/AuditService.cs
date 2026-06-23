// ============================================================
// ProximaLMSAPI/Services/AuditService.cs
// ------------------------------------------------------------
// FIX (May 2026):
//   • AuditEntry now mirrors TblAuditLog one-for-one.
//   • Calls SP_AuditLog_Insert (was: SP_AuditLog_Write, which
//     never existed in the DB — every write was silently failing).
//   • Parameter names + types match the SP signature exactly.
// ============================================================
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MySql.Data.MySqlClient;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ProximaLMSAPI.Services
{
    public interface IAuditService
    {
        Task WriteAsync(AuditEntry entry);
    }

    /// <summary>One row in TblAuditLog. Fields map 1:1 to columns.</summary>
    public class AuditEntry
    {
        public int ActorUserID { get; set; }
        public string ActorEmail { get; set; } = "";
        public int ActorRoleID { get; set; }
        public string ActorRoleName { get; set; } = "";

        /// <summary>"Controller.Action" — e.g. "Role.Save"</summary>
        public string Action { get; set; } = "";

        /// <summary>Usually the controller name — e.g. "Role"</summary>
        public string EntityType { get; set; } = "";

        /// <summary>Route {id} when present, else blank</summary>
        public string EntityID { get; set; } = "";

        public string HttpMethod { get; set; } = "";
        public string Path { get; set; } = "";
        public int StatusCode { get; set; }

        /// <summary>"SUCCESS" or "FAILURE"</summary>
        public string Outcome { get; set; } = "SUCCESS";

        public string? IpAddress { get; set; }
        public string? UserAgent { get; set; }

        /// <summary>Redacted request body JSON</summary>
        public string? Detail { get; set; }
    }

    public class AuditService : IAuditService
    {
        private readonly IConfiguration _config;
        private readonly ILogger<AuditService> _logger;

        // Any JSON property whose name CONTAINS one of these
        // (case-insensitive) has its value redacted before storage.
        private static readonly string[] SensitiveKeys =
        {
            "password", "pwd", "otp", "token", "secret",
            "signature", "cardnumber", "card_number", "cvv", "apikey"
        };

        private const int MaxDetailChars = 4000;

        public AuditService(IConfiguration config, ILogger<AuditService> logger)
        {
            _config = config;
            _logger = logger;
        }

        public async Task WriteAsync(AuditEntry e)
        {
            // Audit logging must NEVER break the request it describes.
            try
            {
                using var conn = new MySqlConnection(
                    _config.GetConnectionString("ConnectionString"));

                var p = new DynamicParameters();
                p.Add("p_ActorUserID", e.ActorUserID);
                p.Add("p_ActorEmail", Trim(e.ActorEmail, 150));
                p.Add("p_ActorRoleID", e.ActorRoleID);
                p.Add("p_ActorRoleName", Trim(e.ActorRoleName, 80));
                p.Add("p_Action", Trim(e.Action, 120));
                p.Add("p_EntityType", Trim(e.EntityType, 80));
                p.Add("p_EntityID", Trim(e.EntityID, 60));
                p.Add("p_HttpMethod", Trim(e.HttpMethod, 10));
                p.Add("p_Path", Trim(e.Path, 300));
                p.Add("p_StatusCode", e.StatusCode);
                p.Add("p_Outcome", Trim(e.Outcome, 10));
                p.Add("p_IpAddress", Trim(e.IpAddress, 64));
                p.Add("p_UserAgent", Trim(e.UserAgent, 300));
                p.Add("p_Detail", e.Detail);

                await conn.ExecuteAsync("SP_AuditLog_Insert", p,
                    commandType: CommandType.StoredProcedure);
            }
            catch (Exception ex)
            {
                // NOTE: SP_AuditLog_Insert also has an EXIT HANDLER FOR SQLEXCEPTION,
                // so SQL-side errors won't even get here. To debug column/type
                // mismatches, temporarily drop the EXIT HANDLER from the SP.
                _logger.LogError(ex, "Failed to write audit log entry (non-fatal).");
            }
        }

        // ── helpers ───────────────────────────────────────────────

        /// <summary>
        /// Serialises action arguments to JSON and redacts sensitive
        /// values. Returns null for empty input.
        /// </summary>
        public static string? BuildRequestData(IDictionary<string, object?> args)
        {
            if (args == null || args.Count == 0) return null;

            try
            {
                var token = JToken.FromObject(args);
                Redact(token);
                var json = token.ToString(Formatting.None);

                return json.Length > MaxDetailChars
                    ? json.Substring(0, MaxDetailChars) + "...[truncated]"
                    : json;
            }
            catch
            {
                return null;
            }
        }

        private static void Redact(JToken token)
        {
            if (token is JObject obj)
            {
                foreach (var prop in obj.Properties().ToList())
                {
                    bool sensitive = SensitiveKeys.Any(k =>
                        prop.Name.Contains(k, StringComparison.OrdinalIgnoreCase));
                    if (sensitive)
                        prop.Value = "***REDACTED***";
                    else
                        Redact(prop.Value);
                }
            }
            else if (token is JArray arr)
            {
                foreach (var item in arr) Redact(item);
            }
        }

        private static string? Trim(string? s, int max) =>
            string.IsNullOrEmpty(s) || s.Length <= max ? s : s.Substring(0, max);
    }
}

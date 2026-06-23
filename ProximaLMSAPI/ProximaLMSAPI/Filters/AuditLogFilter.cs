// ============================================================
// ProximaLMSAPI/Filters/AuditLogFilter.cs
// ------------------------------------------------------------
// FIX (May 2026):
//   • Populates the new AuditEntry shape that matches TblAuditLog.
//   • Captures status code AFTER the action runs (so Outcome is right).
//   • Extracts EntityID from route values when {id} is present.
//   • Pulls Path from Request.Path (was missing entirely).
// ============================================================
using System;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;
using ProximaLMSAPI.Services;

namespace ProximaLMSAPI.Filters
{
    public sealed class AuditLogFilter : IAsyncActionFilter
    {
        private readonly IAuditService _audit;
        private readonly ILogger<AuditLogFilter> _logger;

        // Controllers NOT audited:
        //   Audit*      → would log itself (recursion / noise)
        //   AuthToken   → pure token machinery, very chatty
        //   Auth/Login  → carries passwords / OTPs at login
        private static readonly string[] SkipControllers =
        {
            "Audit", "AuditLog", "AuthToken", "Auth", "Login"
        };

        // Only these HTTP verbs change data.
        private static readonly string[] MutatingVerbs =
        {
            "POST", "PUT", "PATCH", "DELETE"
        };

        public AuditLogFilter(IAuditService audit, ILogger<AuditLogFilter> logger)
        {
            _audit = audit;
            _logger = logger;
        }

        public async Task OnActionExecutionAsync(
            ActionExecutingContext context,
            ActionExecutionDelegate next)
        {
            string method = context.HttpContext.Request.Method.ToUpperInvariant();

            var cad = context.ActionDescriptor as ControllerActionDescriptor;
            string ctrl = cad?.ControllerName ?? "";
            string actName = cad?.ActionName ?? "";

            bool shouldAudit =
                MutatingVerbs.Contains(method) &&
                !SkipControllers.Contains(ctrl, StringComparer.OrdinalIgnoreCase);

            // Capture args BEFORE the action runs (it may mutate them).
            // Redaction happens inside BuildRequestData.
            string? detail = shouldAudit
                ? AuditService.BuildRequestData(context.ActionArguments)
                : null;

            // Run the action.
            var executed = await next();

            if (!shouldAudit) return;

            try
            {
                // ── ACTOR (from JWT in Authorization header) ─────
                var (actorId, actorEmail, actorRoleId, actorRoleName) =
                    ExtractActor(context.HttpContext);

                // ── ENTITY ID (from route {id} when present) ────
                string entityId = "";
                if (context.RouteData.Values.TryGetValue("id", out var idVal))
                    entityId = idVal?.ToString() ?? "";

                // ── STATUS / OUTCOME ────────────────────────────
                int statusCode = context.HttpContext.Response.StatusCode;
                bool ok = executed.Exception == null && statusCode < 400;

                var entry = new AuditEntry
                {
                    ActorUserID = actorId,
                    ActorEmail = actorEmail,
                    ActorRoleID = actorRoleId,
                    ActorRoleName = actorRoleName,
                    Action = $"{ctrl}.{actName}",
                    EntityType = ctrl,
                    EntityID = entityId,
                    HttpMethod = method,
                    Path = context.HttpContext.Request.Path.ToString(),
                    StatusCode = statusCode,
                    Outcome = ok ? "SUCCESS" : "FAILURE",
                    IpAddress = context.HttpContext.Connection.RemoteIpAddress?.ToString(),
                    UserAgent = context.HttpContext.Request.Headers["User-Agent"].ToString(),
                    Detail = detail
                };

                await _audit.WriteAsync(entry);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Audit filter failed (non-fatal).");
            }
        }

        // ────────────────────────────────────────────────────────
        // Extract actor info from the JWT in the Authorization header.
        // Falls back to empty values if no token / malformed token.
        // ────────────────────────────────────────────────────────
        private static (int id, string email, int roleId, string roleName)
            ExtractActor(HttpContext http)
        {
            int id = 0;
            string email = "";
            int roleId = 0;
            string roleName = "";

            var auth = http.Request.Headers["Authorization"].ToString();
            if (string.IsNullOrEmpty(auth) ||
                !auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                return (id, email, roleId, roleName);
            }

            try
            {
                var token = auth.Substring("Bearer ".Length).Trim();
                var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);

                foreach (var c in jwt.Claims)
                {
                    switch (c.Type)
                    {
                        // Common claim type names used by TokenService.
                        // Catch every variant we might emit.
                        case "userId":
                        case "UserID":
                        case "sub":
                        case ClaimTypes.NameIdentifier:
                            int.TryParse(c.Value, out id);
                            break;

                        case "email":
                        case "Email":
                        case ClaimTypes.Email:
                            email = c.Value;
                            break;

                        case "roleId":
                        case "RoleID":
                            int.TryParse(c.Value, out roleId);
                            break;

                        case "roleName":
                        case "RoleName":
                        case "role":
                        case ClaimTypes.Role:
                            roleName = c.Value;
                            break;
                    }
                }
            }
            catch
            {
                // malformed token — leave actor blank
            }

            return (id, email, roleId, roleName);
        }
    }
}

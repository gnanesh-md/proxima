// ============================================================
// ProximaLMS/Filters/RequireJwtAttribute.cs
// ------------------------------------------------------------
// Drop-in replacement for the existing RequireJwtAttribute.
//
// What changed vs the original:
//   • Now async (OnActionExecutionAsync) so it can call the API.
//   • Before every protected action it asks TokenRefreshService
//     to make sure the access token in session is still valid —
//     refreshing it transparently if it is about to expire.
//   • If the session can no longer be kept alive (refresh token
//     gone or expired), the session is cleared and the user is
//     sent to the login page — exactly as before, just no longer
//     on a hard 15-minute timer.
//
// Controllers do NOT need any changes: they keep reading
// Session["JwtToken"] and it is now always fresh.
// ============================================================
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using ProximaLMS.Models;
using ProximaLMS.Services;

namespace ProximaLMS.Filters
{
    [System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Method)]
    public sealed class RequireJwtAttribute : ActionFilterAttribute
    {
        public override async Task OnActionExecutionAsync(
            ActionExecutingContext context,
            ActionExecutionDelegate next)
        {
            // ── Skip anything explicitly marked [AllowAnonymous] ──
            var cad = context.ActionDescriptor as ControllerActionDescriptor;
            bool allowAnon =
                cad?.MethodInfo.GetCustomAttribute<AllowAnonymousAttribute>() != null ||
                cad?.ControllerTypeInfo.GetCustomAttribute<AllowAnonymousAttribute>() != null;

            if (allowAnon)
            {
                await next();
                return;
            }

            var session = context.HttpContext.Session;
            var token = session.GetString(SessionKeys.JwtToken);
            var userId = session.GetString(SessionKeys.UserID);
            var email = session.GetString(SessionKeys.Email);

            // ── Not logged in at all ──────────────────────────────
            if (string.IsNullOrEmpty(token) ||
                string.IsNullOrEmpty(userId) ||
                string.IsNullOrEmpty(email))
            {
                context.Result = new RedirectToActionResult("Index", "Home", null);
                return;
            }

            // ── Make sure the access token is still usable ────────
            // (refreshes transparently when within the expiry buffer)
            var refresher = context.HttpContext.RequestServices
                                   .GetRequiredService<ITokenRefreshService>();

            bool stillValid = await refresher.EnsureValidTokenAsync(context.HttpContext);

            if (!stillValid)
            {
                // Refresh token expired / revoked — end the session cleanly.
                session.Clear();
                context.Result = new RedirectToActionResult("Index", "Home", null);
                return;
            }

            await next();
        }
    }
}

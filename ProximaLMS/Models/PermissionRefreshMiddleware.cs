using Newtonsoft.Json.Linq;
using System.Text.Json;

namespace ProximaLMS.Middleware
{
    /// <summary>
    /// Refreshes screen permissions from the API on every request.
    /// This ensures that when an Admin disables a permission,
    /// it takes effect immediately on the NEXT page load — no re-login needed.
    /// </summary>
    public class PermissionRefreshMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly IConfiguration _config;
        private readonly ILogger<PermissionRefreshMiddleware> _logger;

        // How many seconds before permissions are re-fetched from the API.
        // 30 seconds = permissions update within 30s of being changed.
        // Lower = more real-time, Higher = less API load.
        private const int RefreshIntervalSeconds = 30;

        public PermissionRefreshMiddleware(
            RequestDelegate next,
            IConfiguration config,
            ILogger<PermissionRefreshMiddleware> logger)
        {
            _next   = next;
            _config = config;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            await TryRefreshPermissions(context);
            await _next(context);
        }

        private async Task TryRefreshPermissions(HttpContext context)
        {
            try
            {
                // Only refresh for authenticated users (JWT in session)
                var token = context.Session.GetString("JwtToken");
                if (string.IsNullOrEmpty(token)) return;

                var roleId = context.Session.GetInt32("RoleID") ?? 0;
                if (roleId == 0) return;

                // Skip static files, API calls, and non-page requests
                var path = context.Request.Path.Value ?? "";
                if (path.StartsWith("/assets/") ||
                    path.StartsWith("/api/")     ||
                    path.StartsWith("/favicon")  ||
                    path.EndsWith(".css")        ||
                    path.EndsWith(".js")         ||
                    path.EndsWith(".png")        ||
                    path.EndsWith(".jpg")        ||
                    path.EndsWith(".ico"))
                    return;

                // Check if permissions need refreshing
                var lastRefreshStr = context.Session.GetString("PermissionsLastRefresh");
                if (!string.IsNullOrEmpty(lastRefreshStr))
                {
                    if (DateTime.TryParse(lastRefreshStr, out var lastRefresh))
                    {
                        var elapsed = (DateTime.UtcNow - lastRefresh).TotalSeconds;
                        if (elapsed < RefreshIntervalSeconds)
                            return; // Still fresh — skip API call
                    }
                }

                // Refresh permissions from API
                await RefreshPermissionsFromApi(context, roleId, token);
            }
            catch (Exception ex)
            {
                // Never crash the request due to permission refresh failure
                _logger.LogWarning(ex, "Permission refresh failed silently.");
            }
        }

        private async Task RefreshPermissionsFromApi(
            HttpContext context, int roleId, string jwtToken)
        {
            var baseUrl = _config["ApiBaseUrl"]?.TrimEnd('/');
            if (string.IsNullOrEmpty(baseUrl)) return;

            using var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (m, c, ch, e) => true
            };
            using var client = new HttpClient(handler)
            {
                BaseAddress = new Uri(baseUrl),
                Timeout = TimeSpan.FromSeconds(5) // fast timeout — don't block UI
            };

            client.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", jwtToken);

            var resp = await client.GetAsync($"/api/role/{roleId}/permissions");
            if (!resp.IsSuccessStatusCode) return;

            var json  = await resp.Content.ReadAsStringAsync();
            var token = JToken.Parse(json);

            JArray? arr = null;
            if (token.Type == JTokenType.Array)
                arr = (JArray)token;
            else
                arr = token["data"] as JArray ?? token["result"] as JArray;

            if (arr == null) return;

            var perms = arr.Select(s => new
            {
                ScreenCode = s["ScreenCode"]?.Value<string>() ?? "",
                CanView    = s["CanView"]?.Value<bool>()    ?? false,
                CanCreate  = s["CanCreate"]?.Value<bool>()  ?? false,
                CanEdit    = s["CanEdit"]?.Value<bool>()    ?? false,
                CanDelete  = s["CanDelete"]?.Value<bool>()  ?? false
            }).Where(p => !string.IsNullOrEmpty(p.ScreenCode)).ToList();

            var permJson = JsonSerializer.Serialize(perms);
            context.Session.SetString("Permissions", permJson);
            context.Session.SetString("PermissionsLastRefresh", DateTime.UtcNow.ToString("O"));

            _logger.LogDebug("Permissions refreshed for RoleID={RoleId}", roleId);
        }
    }

    // ── Extension method for clean registration in Program.cs ──
    public static class PermissionRefreshMiddlewareExtensions
    {
        public static IApplicationBuilder UsePermissionRefresh(this IApplicationBuilder app)
            => app.UseMiddleware<PermissionRefreshMiddleware>();
    }
}

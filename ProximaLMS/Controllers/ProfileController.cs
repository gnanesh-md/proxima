// ============================================================
// ProximaLMS/Controllers/ProfileController.cs
// ------------------------------------------------------------
// GET  /Profile                → My Profile screen (view + edit + danger zone)
// POST /Profile/Save           → save profile extras
// POST /Profile/Deactivate     → self-service deactivation
// GET  /Profile/Deactivated    → post-deactivation landing page
// ============================================================
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;
using ProximaLMS.Filters;
using ProximaLMS.Models;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace ProximaLMS.Controllers
{
    [RequireJwt]
    public class ProfileController : Controller
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _config;
        private readonly ILogger<ProfileController> _logger;

        public ProfileController(
            IHttpClientFactory httpClientFactory,
            IConfiguration config,
            ILogger<ProfileController> logger)
        {
            _httpClientFactory = httpClientFactory;
            _config = config;
            _logger = logger;
        }

        private HttpClient CreateClient()
        {
            var token  = HttpContext.Session.GetString("JwtToken");
            var client = _httpClientFactory.CreateClient();
            client.BaseAddress = new Uri(_config["ApiBaseUrl"]);
            if (!string.IsNullOrEmpty(token))
                client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", token);
            return client;
        }


        // ═════════════════════════════════════════════════════════
        // GET /Profile
        // ═════════════════════════════════════════════════════════
        [HttpGet]
        public async Task<IActionResult> Index()
        {
            int userId = int.TryParse(HttpContext.Session.GetString("UserID"), out var u) ? u : 0;
            if (userId <= 0)
                return RedirectToAction("Index", "Login");

            var vm = new ProfileViewModel { UserID = userId };

            try
            {
                using var client = CreateClient();
                var resp = await client.GetAsync($"/api/profile/{userId}");
                if (resp.IsSuccessStatusCode)
                {
                    var json = await resp.Content.ReadAsStringAsync();
                    var token = JObject.Parse(json);
                    var data  = token["data"] as JObject;

                    if (data != null)
                    {
                        vm.FullName          = data["FullName"]?.Value<string>()          ?? "";
                        vm.Email             = data["Email"]?.Value<string>()             ?? "";
                        vm.MobileNumber      = data["MobileNumber"]?.Value<string>()      ?? "";
                        vm.Gender            = data["Gender"]?.Value<string>()            ?? "";
                        vm.RoleID            = data["RoleID"]?.Value<int>()               ?? 0;
                        vm.DateOfBirth       = data["DateOfBirth"]?.Value<DateTime?>();
                        vm.Bio               = data["Bio"]?.Value<string>()               ?? "";
                        vm.ProfilePhoto      = data["ProfilePhoto"]?.Value<string>()      ?? "";
                        vm.Interests         = data["Interests"]?.Value<string>()         ?? "";
                        vm.PreferredLanguage = data["PreferredLanguage"]?.Value<string>() ?? "";
                        vm.Location          = data["Location"]?.Value<string>()          ?? "";
                        vm.LinkedInUrl       = data["LinkedInUrl"]?.Value<string>()       ?? "";
                        vm.WebsiteUrl        = data["WebsiteUrl"]?.Value<string>()        ?? "";
                        vm.CompletionPercent = data["CompletionPercent"]?.Value<int>()    ?? 0;
                        vm.CreatedDate       = data["CreatedDate"]?.Value<DateTime?>();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Profile load failed");
                TempData["Error"] = "Could not load profile.";
            }

            return View(vm);
        }


        // ═════════════════════════════════════════════════════════
        // POST /Profile/Save        (AJAX, JSON)
        // ═════════════════════════════════════════════════════════
        [HttpPost]
        public async Task<IActionResult> Save([FromBody] ProfileSaveRequest req)
        {
            int userId = int.TryParse(HttpContext.Session.GetString("UserID"), out var u) ? u : 0;
            if (userId <= 0)
                return Json(new { success = false, message = "Session expired." });

            using var client = CreateClient();

            var payload = new
            {
                UserID            = userId,
                DateOfBirth       = req.DateOfBirth,
                Bio               = req.Bio               ?? "",
                ProfilePhoto      = req.ProfilePhoto      ?? "",
                Interests         = req.Interests         ?? "",
                PreferredLanguage = req.PreferredLanguage ?? "",
                Location          = req.Location          ?? "",
                LinkedInUrl       = req.LinkedInUrl       ?? "",
                WebsiteUrl        = req.WebsiteUrl        ?? ""
            };

            var resp = await client.PostAsJsonAsync("/api/profile/save", payload);
            var body = await resp.Content.ReadAsStringAsync();
            return Content(body, "application/json");
        }


        // ═════════════════════════════════════════════════════════
        // POST /Profile/Deactivate     (AJAX, JSON)
        // Body: { CurrentPassword, Reason }
        // On success: clears session and tells the client to redirect
        //             to /Profile/Deactivated.
        // ═════════════════════════════════════════════════════════
        [HttpPost]
        public async Task<IActionResult> Deactivate([FromBody] DeactivateRequest req)
        {
            int userId = int.TryParse(HttpContext.Session.GetString("UserID"), out var u) ? u : 0;
            if (userId <= 0)
                return Json(new { success = false, message = "Session expired." });

            if (req == null || string.IsNullOrWhiteSpace(req.CurrentPassword))
                return Json(new { success = false, message = "Current password is required." });

            using var client = CreateClient();
            var resp = await client.PostAsJsonAsync("/api/profile/deactivate", new
            {
                UserID          = userId,
                CurrentPassword = req.CurrentPassword,
                Reason          = req.Reason
            });

            var body = await resp.Content.ReadAsStringAsync();
            var jt = JObject.Parse(body);
            bool ok  = jt["success"]?.Value<bool>() ?? false;
            string m = jt["message"]?.Value<string>() ?? "";

            if (!ok)
                return Json(new { success = false, message = m });

            // Wipe session — user is logged out.
            HttpContext.Session.Clear();

            return Json(new { success = true, redirectTo = Url.Action("Deactivated", "Profile") });
        }


        // ═════════════════════════════════════════════════════════
        // GET /Profile/Deactivated   — post-deactivation landing
        // ═════════════════════════════════════════════════════════
        [HttpGet]
        [AllowAnonymous_Hack]  // attribute below — bypasses [RequireJwt] for this action
        public IActionResult Deactivated()
        {
            return View();
        }


        // ── DTOs ─────────────────────────────────────────────────
        public class ProfileSaveRequest
        {
            public DateTime? DateOfBirth       { get; set; }
            public string?   Bio               { get; set; }
            public string?   ProfilePhoto      { get; set; }
            public string?   Interests         { get; set; }
            public string?   PreferredLanguage { get; set; }
            public string?   Location          { get; set; }
            public string?   LinkedInUrl       { get; set; }
            public string?   WebsiteUrl        { get; set; }
        }

        public class DeactivateRequest
        {
            public string  CurrentPassword { get; set; } = "";
            public string? Reason          { get; set; }
        }
    }


    // ── Tiny attribute so the post-deactivation page is reachable
    //    without a session. If your [RequireJwt] already supports
    //    [AllowAnonymous], delete this and use that instead.
    [AttributeUsage(AttributeTargets.Method)]
    public class AllowAnonymous_HackAttribute : Attribute { }
}

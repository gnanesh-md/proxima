// ============================================================
// ProximaLMS/Controllers/SettingsController.cs
// ------------------------------------------------------------
// Renders all admin settings pages and forwards their AJAX
// calls through to the ProximaLMSAPI.
//
//   ★ FIX (May 2026)
//   ───────────────────────────────────────────────────────────
//   The old version declared every save/toggle/delete action as
//   `[FromBody] object body` and then re-serialised that body
//   with Newtonsoft.Json. ASP.NET Core binds bare `object` via
//   System.Text.Json — yielding a JsonElement that Newtonsoft
//   cannot serialise back to its original JSON.  The API ended
//   up receiving `{"ValueKind":1}` style payloads with every
//   real property missing, which is why "Category name is
//   required" kept firing even when you typed a name.
//
//   The fix: bind to JsonElement and forward its RAW JSON text
//   straight to the API. No re-serialisation, nothing lost.
// ============================================================
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using ProximaLMS.Filters;
using ProximaLMS.Models;

namespace ProximaLMS.Controllers
{
    [RequireJwt]
    [AdminOnly]
    public class SettingsController : Controller
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _config;
        private readonly ILogger<SettingsController> _logger;

        public SettingsController(
            IHttpClientFactory httpClientFactory,
            IConfiguration config,
            ILogger<SettingsController> logger)
        {
            _httpClientFactory = httpClientFactory;
            _config = config;
            _logger = logger;
        }

        private HttpClient CreateClient()
        {
            var token = HttpContext.Session.GetString("JwtToken");
            var client = _httpClientFactory.CreateClient();
            client.BaseAddress = new Uri(_config["ApiBaseUrl"]);
            if (!string.IsNullOrEmpty(token))
                client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", token);
            return client;
        }

        // ────────────────────────────────────────────────────────
        // GET forward — no body involved.
        // ────────────────────────────────────────────────────────
        private async Task<IActionResult> ForwardGet(string path)
        {
            try
            {
                using var client = CreateClient();
                using var req = new HttpRequestMessage(HttpMethod.Get, path);
                var resp = await client.SendAsync(req);
                var text = await resp.Content.ReadAsStringAsync();
                Response.StatusCode = (int)resp.StatusCode;
                return Content(text, "application/json");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Forward GET {Path} failed", path);
                return StatusCode(502, new
                {
                    success = false,
                    message = "Could not reach the API: " + ex.Message
                });
            }
        }

        // ────────────────────────────────────────────────────────
        // POST forward — takes the RAW JSON text from JsonElement
        // and re-sends it verbatim. No deserialise/serialise round
        // trip, so property casing and content survive untouched.
        // ────────────────────────────────────────────────────────
        private async Task<IActionResult> ForwardPost(string path, JsonElement body)
        {
            try
            {
                using var client = CreateClient();
                using var req = new HttpRequestMessage(HttpMethod.Post, path);

                // GetRawText() returns the original JSON the browser sent.
                var rawJson = body.ValueKind == JsonValueKind.Undefined
                    ? "{}"
                    : body.GetRawText();

                req.Content = new StringContent(rawJson, Encoding.UTF8, "application/json");

                var resp = await client.SendAsync(req);
                var text = await resp.Content.ReadAsStringAsync();
                Response.StatusCode = (int)resp.StatusCode;
                return Content(text, "application/json");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Forward POST {Path} failed", path);
                return StatusCode(502, new
                {
                    success = false,
                    message = "Could not reach the API: " + ex.Message
                });
            }
        }


        // ═════════════════════════════════════════════════════════
        // HUB
        // ═════════════════════════════════════════════════════════
        [HttpGet]
        public IActionResult Index() => View();


        // ═════════════════════════════════════════════════════════
        // LANGUAGES
        // ═════════════════════════════════════════════════════════
        [HttpGet] public IActionResult Languages() => View(new SettingsPageVM { Title = "Languages" });
        [HttpGet] public Task<IActionResult> LanguagesList() => ForwardGet("/api/Language/list");
        [HttpPost] public Task<IActionResult> LanguageSave([FromBody] JsonElement b) => ForwardPost("/api/Language/save", b);
        [HttpPost] public Task<IActionResult> LanguageDelete([FromBody] JsonElement b) => ForwardPost("/api/Language/delete", b);


        // ═════════════════════════════════════════════════════════
        // CERTIFICATE TEMPLATES
        // ═════════════════════════════════════════════════════════
        [HttpGet] public IActionResult Certificates() => View(new SettingsPageVM { Title = "Certificate Templates" });
        [HttpGet] public Task<IActionResult> CertificateList() => ForwardGet("/api/CertificateTemplate/list");
        [HttpGet] public Task<IActionResult> CertificateGet(int id) => ForwardGet($"/api/CertificateTemplate/{id}");
        [HttpPost] public Task<IActionResult> CertificateSave([FromBody] JsonElement b) => ForwardPost("/api/CertificateTemplate/save", b);
        [HttpPost] public Task<IActionResult> CertificateDelete([FromBody] JsonElement b) => ForwardPost("/api/CertificateTemplate/delete", b);


        // ═════════════════════════════════════════════════════════
        // POINTS RULES
        // ═════════════════════════════════════════════════════════
        [HttpGet] public IActionResult Points() => View(new SettingsPageVM { Title = "Points Rules" });
        [HttpGet] public Task<IActionResult> PointsList() => ForwardGet("/api/PointsRule/list");
        [HttpPost] public Task<IActionResult> PointsSave([FromBody] JsonElement b) => ForwardPost("/api/PointsRule/save", b);
        [HttpPost] public Task<IActionResult> PointsDelete([FromBody] JsonElement b) => ForwardPost("/api/PointsRule/delete", b);


        // ═════════════════════════════════════════════════════════
        // BADGES
        // ═════════════════════════════════════════════════════════
        [HttpGet] public IActionResult Badges() => View(new SettingsPageVM { Title = "Badges" });
        [HttpGet] public Task<IActionResult> BadgesList() => ForwardGet("/api/Badge/list");
        [HttpPost] public Task<IActionResult> BadgeSave([FromBody] JsonElement b) => ForwardPost("/api/Badge/save", b);
        [HttpPost] public Task<IActionResult> BadgeDelete([FromBody] JsonElement b) => ForwardPost("/api/Badge/delete", b);


        // ═════════════════════════════════════════════════════════
        // EMAIL TEMPLATES
        // ═════════════════════════════════════════════════════════
        [HttpGet] public IActionResult Emails() => View(new SettingsPageVM { Title = "Email Templates" });
        [HttpGet] public Task<IActionResult> EmailList() => ForwardGet("/api/EmailTemplate/list");
        [HttpGet] public Task<IActionResult> EmailGet(int id) => ForwardGet($"/api/EmailTemplate/{id}");
        [HttpPost] public Task<IActionResult> EmailSave([FromBody] JsonElement b) => ForwardPost("/api/EmailTemplate/save", b);


        // ═════════════════════════════════════════════════════════
        // PAYMENT GATEWAY
        // ═════════════════════════════════════════════════════════
        [HttpGet] public IActionResult Payment() => View(new SettingsPageVM { Title = "Payment Gateway" });
        [HttpGet] public Task<IActionResult> PaymentLoad() => ForwardGet("/api/SystemSettings/group/PAYMENT");
        [HttpGet] public Task<IActionResult> PaymentReveal(string key) => ForwardGet($"/api/SystemSettings/reveal/{Uri.EscapeDataString(key)}");
        [HttpPost] public Task<IActionResult> PaymentSave([FromBody] JsonElement b) => ForwardPost("/api/SystemSettings/save-bulk", b);


        // ═════════════════════════════════════════════════════════
        // STORAGE
        // ═════════════════════════════════════════════════════════
        [HttpGet] public IActionResult Storage() => View(new SettingsPageVM { Title = "Storage" });
        [HttpGet] public Task<IActionResult> StorageLoad() => ForwardGet("/api/SystemSettings/group/STORAGE");
        [HttpPost] public Task<IActionResult> StorageSave([FromBody] JsonElement b) => ForwardPost("/api/SystemSettings/save-bulk", b);


        // ═════════════════════════════════════════════════════════
        // CATEGORIES (hierarchical)
        // ═════════════════════════════════════════════════════════
        [HttpGet] public IActionResult Categories() => View(new SettingsPageVM { Title = "Categories" });
        [HttpGet] public Task<IActionResult> CategoryList() => ForwardGet("/api/Category/all");
        [HttpGet] public Task<IActionResult> CategoryGet(int id) => ForwardGet($"/api/Category/{id}");
        [HttpPost] public Task<IActionResult> CategorySave([FromBody] JsonElement b) => ForwardPost("/api/Category/save", b);
        [HttpPost] public Task<IActionResult> CategoryToggle([FromBody] JsonElement b) => ForwardPost("/api/Category/toggle-status", b);
        [HttpPost] public Task<IActionResult> CategoryDelete([FromBody] JsonElement b) => ForwardPost("/api/Category/delete", b);


        // ═════════════════════════════════════════════════════════
        // TAGS
        // ═════════════════════════════════════════════════════════
        [HttpGet] public IActionResult Tags() => View(new SettingsPageVM { Title = "Tags" });
        [HttpGet] public Task<IActionResult> TagList() => ForwardGet("/api/Tag/all");
        [HttpGet] public Task<IActionResult> TagGet(int id) => ForwardGet($"/api/Tag/{id}");
        [HttpPost] public Task<IActionResult> TagSave([FromBody] JsonElement b) => ForwardPost("/api/Tag/save", b);
        [HttpPost] public Task<IActionResult> TagToggle([FromBody] JsonElement b) => ForwardPost("/api/Tag/toggle-status", b);
        [HttpPost] public Task<IActionResult> TagDelete([FromBody] JsonElement b) => ForwardPost("/api/Tag/delete", b);


        // ═════════════════════════════════════════════════════════
        // SKILL LEVELS
        // ═════════════════════════════════════════════════════════
        [HttpGet] public IActionResult SkillLevels() => View(new SettingsPageVM { Title = "Skill Levels" });
        [HttpGet] public Task<IActionResult> SkillLevelList() => ForwardGet("/api/SkillLevel/all");
        [HttpGet] public Task<IActionResult> SkillLevelGet(int id) => ForwardGet($"/api/SkillLevel/{id}");
        [HttpPost] public Task<IActionResult> SkillLevelSave([FromBody] JsonElement b) => ForwardPost("/api/SkillLevel/save", b);
        [HttpPost] public Task<IActionResult> SkillLevelToggle([FromBody] JsonElement b) => ForwardPost("/api/SkillLevel/toggle-status", b);
        [HttpPost] public Task<IActionResult> SkillLevelDelete([FromBody] JsonElement b) => ForwardPost("/api/SkillLevel/delete", b);


        // ═════════════════════════════════════════════════════════
        // REFERRAL POLICY (single record)
        // ═════════════════════════════════════════════════════════
        [HttpGet] public IActionResult ReferralPolicy() => View(new SettingsPageVM { Title = "Referral Policy" });
        [HttpGet] public Task<IActionResult> ReferralPolicyLoad() => ForwardGet("/api/Referral/policy");
        [HttpPost] public Task<IActionResult> ReferralPolicySave([FromBody] JsonElement b) => ForwardPost("/api/Referral/policy/save", b);
    }


    // ════════════════════════════════════════════════════════════
    // Tiny attribute that bounces non-admins to the home page.
    // If your project already has an [AdminOnly] (or similar)
    // attribute, delete this and use yours.
    // ════════════════════════════════════════════════════════════
    public class AdminOnlyAttribute : ActionFilterAttribute
    {
        public override void OnActionExecuting(ActionExecutingContext ctx)
        {
            int roleId = ctx.HttpContext.Session.GetInt32("RoleID") ?? 0;
            if (roleId != 1)
            {
                ctx.Result = new RedirectToActionResult("Index", "Home", null);
            }
        }
    }
}
// ============================================================
// ProximaLMS/Controllers/RegisterController.cs
// ------------------------------------------------------------
// Hosts the new 4-step Profile Creation Wizard at /Register.
// Proxies every step to the API:
//   GET  /Register                       → renders the wizard view
//   POST /Register/SendEmailOtp          → step 1
//   POST /Register/VerifyEmailOtp        → step 2
//   POST /Register/Complete              → step 3 (creates user + auto-login)
//   POST /Register/SaveProfile           → step 4 (optional extras)
// ============================================================
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ProximaLMS.Models;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;

namespace ProximaLMS.Controllers
{
    public class RegisterController : Controller
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _config;
        private readonly ILogger<RegisterController> _logger;

        public RegisterController(
            IHttpClientFactory httpClientFactory,
            IConfiguration config,
            ILogger<RegisterController> logger)
        {
            _httpClientFactory = httpClientFactory;
            _config = config;
            _logger = logger;
        }

        private HttpClient CreateClient(string? bearerToken = null)
        {
            var client = _httpClientFactory.CreateClient();
            client.BaseAddress = new Uri(_config["ApiBaseUrl"]);
            if (!string.IsNullOrEmpty(bearerToken))
                client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", bearerToken);
            return client;
        }


        // ═════════════════════════════════════════════════════════
        // GET /Register
        // ═════════════════════════════════════════════════════════
        [HttpGet]
        public IActionResult Index()
        {
            // If user is already logged in, push them to their dashboard.
            var roleId = HttpContext.Session.GetInt32("RoleID") ?? 0;
            if (roleId > 0)
                return roleId == 1
                    ? RedirectToAction("Index", "Dashboard")
                    : RedirectToAction("List",  "Courses");

            return View(new RegisterWizardViewModel());
        }


        // ═════════════════════════════════════════════════════════
        // POST /Register/SendEmailOtp        Step 1 → 2
        // Body: { Email, MobileNumber }
        // ═════════════════════════════════════════════════════════
        [HttpPost]
        public async Task<IActionResult> SendEmailOtp([FromBody] EmailOtpRequest req)
        {
            if (req == null || string.IsNullOrWhiteSpace(req.Email))
                return Json(new { success = false, message = "Email is required." });

            using var client = CreateClient();
            var resp = await client.PostAsJsonAsync("/api/Auth/send-register-otp", new
            {
                Email        = req.Email,
                MobileNumber = req.MobileNumber ?? ""
            });

            var body = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode)
            {
                var err = TryGetMessage(body) ?? "Could not send verification code.";
                return Json(new { success = false, message = err });
            }

            return Json(new { success = true, message = "Verification code sent to your email." });
        }


        // ═════════════════════════════════════════════════════════
        // POST /Register/VerifyEmailOtp      Step 2 → 3
        // Body: { Email, OTP }
        // ═════════════════════════════════════════════════════════
        [HttpPost]
        public async Task<IActionResult> VerifyEmailOtp([FromBody] VerifyEmailOtpRequest req)
        {
            if (req == null || string.IsNullOrWhiteSpace(req.Email)
                || string.IsNullOrWhiteSpace(req.OTP))
                return Json(new { success = false, message = "Email and OTP are required." });

            using var client = CreateClient();
            var resp = await client.PostAsJsonAsync("/api/Auth/verify-register-otp", new
            {
                Email = req.Email,
                OTP   = req.OTP
            });

            var body = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode)
            {
                var err = TryGetMessage(body) ?? "Invalid or expired code.";
                return Json(new { success = false, message = err });
            }

            return Json(new { success = true });
        }


        // ═════════════════════════════════════════════════════════
        // POST /Register/Complete            Step 3 → done
        // Creates the user, auto-logs them in, returns JWT bits so
        // step 4 of the wizard can call save-profile with auth.
        // ═════════════════════════════════════════════════════════
        [HttpPost]
        public async Task<IActionResult> Complete([FromBody] RegisterCompleteRequest req)
        {
            if (req == null
                || string.IsNullOrWhiteSpace(req.Email)
                || string.IsNullOrWhiteSpace(req.Password)
                || string.IsNullOrWhiteSpace(req.Name)
                || string.IsNullOrWhiteSpace(req.MobileNumber))
                return Json(new { success = false, message = "Please fill all required fields." });

            using var client = CreateClient();
            var resp = await client.PostAsJsonAsync("/api/Auth/register-complete", req);
            var body = await resp.Content.ReadAsStringAsync();

            if (!resp.IsSuccessStatusCode)
            {
                var err = TryGetMessage(body) ?? "Registration failed.";
                return Json(new { success = false, message = err });
            }

            // Save the JWT bits into session so the rest of the app
            // treats this freshly-registered user as logged in.
            var data = JObject.Parse(body);

            HttpContext.Session.SetString("JwtToken",          (string?)data["token"]            ?? "");
            HttpContext.Session.SetString("RefreshToken",      (string?)data["refreshToken"]     ?? "");
            HttpContext.Session.SetString("AccessExpiresAt",   ((DateTime?)data["accessExpiresAt"])?.ToUniversalTime().ToString("o") ?? "");
            HttpContext.Session.SetString("RefreshExpiresAt",  ((DateTime?)data["refreshExpiresAt"])?.ToUniversalTime().ToString("o") ?? "");
            HttpContext.Session.SetString("UserID",            ((int?)data["userId"])?.ToString() ?? "0");
            HttpContext.Session.SetString("Email",             (string?)data["email"]   ?? "");
            HttpContext.Session.SetInt32 ("RoleID",            (int?)data["roleId"]    ?? 0);
            HttpContext.Session.SetString("RoleName",          (string?)data["roleName"]?? "");

            // ── Referral linking (optional, non-fatal) ──────────────
            // If the new user entered a referral code, link them to the
            // referrer now. A bad/own code must NOT fail registration.
            string? referralMsg = null;
            var newUserId = (int?)data["userId"] ?? 0;
            if (newUserId > 0 && !string.IsNullOrWhiteSpace(req.ReferralCode))
            {
                try
                {
                    var refResp = await client.PostAsJsonAsync("/api/referral/register", new
                    {
                        RefereeUserID = newUserId,
                        ReferralCode  = req.ReferralCode.Trim().ToUpper()
                    });
                    var refBody = await refResp.Content.ReadAsStringAsync();
                    referralMsg = TryGetMessage(refBody)
                                  ?? (refResp.IsSuccessStatusCode ? "Referral applied!" : "Referral code could not be applied.");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Referral link failed for new user {Uid}", newUserId);
                    referralMsg = "Referral code could not be applied.";
                }
            }

            return Json(new
            {
                success      = true,
                userId       = newUserId,
                roleId       = (int?)data["roleId"],
                referralMessage = referralMsg
            });
        }


        // ═════════════════════════════════════════════════════════
        // POST /Register/SaveProfile         Step 4 (optional)
        // Body: { Bio, Interests, PreferredLanguage, Location,
        //         LinkedInUrl, WebsiteUrl, ProfilePhoto }
        // ═════════════════════════════════════════════════════════
        [HttpPost]
        public async Task<IActionResult> SaveProfile([FromBody] WizardProfileRequest req)
        {
            var token  = HttpContext.Session.GetString("JwtToken");
            var userId = HttpContext.Session.GetString("UserID");

            if (string.IsNullOrEmpty(token) || !int.TryParse(userId, out int uid) || uid <= 0)
                return Json(new { success = false, message = "Session expired. Please log in." });

            using var client = CreateClient(token);

            var payload = new
            {
                UserID            = uid,
                DateOfBirth       = (DateTime?)null,
                Bio               = req?.Bio               ?? "",
                ProfilePhoto      = req?.ProfilePhoto      ?? "",
                Interests         = req?.Interests         ?? "",
                PreferredLanguage = req?.PreferredLanguage ?? "",
                Location          = req?.Location          ?? "",
                LinkedInUrl       = req?.LinkedInUrl       ?? "",
                WebsiteUrl        = req?.WebsiteUrl        ?? ""
            };

            var resp = await client.PostAsJsonAsync("/api/profile/save", payload);
            var body = await resp.Content.ReadAsStringAsync();

            if (!resp.IsSuccessStatusCode)
                return Json(new { success = false, message = TryGetMessage(body) ?? "Could not save profile." });

            return Json(new { success = true });
        }


        // ─────────────────────────────────────────────────────────
        // helpers
        // ─────────────────────────────────────────────────────────
        private static string? TryGetMessage(string json)
        {
            try
            {
                var t = JToken.Parse(json);
                return t["Message"]?.ToString()
                    ?? t["message"]?.ToString();
            }
            catch
            {
                return null;
            }
        }

        // ── DTOs (kept private to this controller) ──
        public class EmailOtpRequest
        {
            public string  Email        { get; set; } = "";
            public string? MobileNumber { get; set; }
        }

        public class VerifyEmailOtpRequest
        {
            public string Email { get; set; } = "";
            public string OTP   { get; set; } = "";
        }

        public class RegisterCompleteRequest
        {
            public string    Name         { get; set; } = "";
            public string?   Gender       { get; set; }
            public string    MobileNumber { get; set; } = "";
            public string    Email        { get; set; } = "";
            public string    Password     { get; set; } = "";
            public DateTime? DateOfBirth  { get; set; }
            public string?   ReferralCode { get; set; }
        }

        public class WizardProfileRequest
        {
            public string? Bio               { get; set; }
            public string? Interests         { get; set; }
            public string? PreferredLanguage { get; set; }
            public string? Location          { get; set; }
            public string? LinkedInUrl       { get; set; }
            public string? WebsiteUrl        { get; set; }
            public string? ProfilePhoto      { get; set; }
        }
    }
}

using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ProximaLMS.Filters;
using System.ComponentModel.DataAnnotations;
using System.Net.Http.Headers;
using System.Text;

namespace ProximaLMS.Controllers
{
    // ============================================================
    // ProximaLMS/Controllers/ReferralController.cs   (MVC frontend)
    // ------------------------------------------------------------
    //   GET  /Referral/Index        → student "Refer & Earn" page
    //   GET  /Referral/Leaderboard  → top referrers (all roles)
    //   GET  /Referral/Policy       → admin referral settings
    //   POST /Referral/Policy       → save referral settings
    //
    // FIX (May 2026): CurrentUserId used Session.GetInt32("UserID"),
    // but LoginController/RegisterController write the key with
    // Session.SetString("UserID", "..."). The int read always
    // returned null → CurrentUserId became 0 → the page bailed
    // with "Please sign in…" even for valid logged-in users.
    // The helper now reads STRING first (matching how it's written)
    // and falls back to int / StudentID for legacy paths.
    // ============================================================
    [RequireJwt]
    public class ReferralController : Controller
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _config;
        private readonly ILogger<ReferralController> _logger;

        public ReferralController(
            IHttpClientFactory httpClientFactory,
            IConfiguration config,
            ILogger<ReferralController> logger)
        {
            _httpClientFactory = httpClientFactory;
            _config = config;
            _logger = logger;
        }

        // ─────────────────────────────────────────
        // HELPERS
        // ─────────────────────────────────────────
        private HttpClient CreateClient()
        {
            var client = _httpClientFactory.CreateClient();
            client.BaseAddress = new Uri(_config["ApiBaseUrl"]);
            var token = HttpContext.Session.GetString("JwtToken");
            if (!string.IsNullOrEmpty(token))
                client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", token);
            return client;
        }

        private string CurrentUser => HttpContext.Session.GetString("Email") ?? "User";
        private int CurrentRoleId => HttpContext.Session.GetInt32("RoleID") ?? 0;

        // ── ROBUST UserID lookup ─────────────────────────────────────
        // Login flows write UserID via Session.SetString(…); older code
        // paths used SetInt32. We try every storage form so this helper
        // never spuriously returns 0 for a real logged-in user.
        private int CurrentUserId
        {
            get
            {
                // 1) String form (what LoginController/RegisterController write)
                var asStr = HttpContext.Session.GetString("UserID");
                if (int.TryParse(asStr, out var id) && id > 0) return id;

                // 2) Int form (in case any future flow stores it that way)
                var asInt = HttpContext.Session.GetInt32("UserID");
                if (asInt.HasValue && asInt.Value > 0) return asInt.Value;

                // 3) Legacy fallback used by older student-only paths
                var sidStr = HttpContext.Session.GetString("StudentID");
                if (int.TryParse(sidStr, out var sid) && sid > 0) return sid;

                var sidInt = HttpContext.Session.GetInt32("StudentID");
                if (sidInt.HasValue && sidInt.Value > 0) return sidInt.Value;

                return 0;
            }
        }

        private List<T> SafeDeserializeList<T>(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return new List<T>();
            try
            {
                var token = JToken.Parse(json);
                if (token.Type == JTokenType.Array)
                    return token.ToObject<List<T>>() ?? new List<T>();
                if (token["data"] != null)
                    return token["data"]!.ToObject<List<T>>() ?? new List<T>();
                if (token["result"] != null)
                    return token["result"]!.ToObject<List<T>>() ?? new List<T>();
                var single = token.ToObject<T>();
                return single != null ? new List<T> { single } : new List<T>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SafeDeserializeList failed");
                return new List<T>();
            }
        }

        private (bool Success, string Message) ParseApiResponse(string json)
        {
            try
            {
                var token = JToken.Parse(json);
                bool success = token["success"]?.Value<bool>()
                            ?? token["Success"]?.Value<bool>() ?? false;
                string message = token["message"]?.Value<string>()
                              ?? token["Message"]?.Value<string>()
                              ?? (success ? "Operation successful." : "Operation failed.");
                return (success, message);
            }
            catch { return (false, "Unexpected API response."); }
        }

        // ─────────────────────────────────────────
        // GET /Referral/Index   → "Refer & Earn"
        // ─────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> Index()
        {
            int userId = CurrentUserId;
            var vm = new ReferralDashboardViewModel();

            if (userId <= 0)
            {
                // Diagnostic: log what we saw in the session so it's
                // obvious next time. Keep at Warning so it surfaces.
                _logger.LogWarning(
                    "Referral/Index: no UserID in session. " +
                    "UserID(str)='{Str}', UserID(int)={Int}, Email='{Email}', SessionKeys=[{Keys}]",
                    HttpContext.Session.GetString("UserID"),
                    HttpContext.Session.GetInt32("UserID"),
                    HttpContext.Session.GetString("Email"),
                    string.Join(", ", HttpContext.Session.Keys));

                TempData["Error"] = "Please sign in to view your referral dashboard.";
                return View(vm);
            }

            try
            {
                using var client = CreateClient();

                // 1) referral code
                var codePayload = new StringContent(
                    JsonConvert.SerializeObject(new { UserID = userId }),
                    Encoding.UTF8, "application/json");
                var codeResp = await client.PostAsync("api/referral/my-code", codePayload);
                if (codeResp.IsSuccessStatusCode)
                {
                    var codeJson = await codeResp.Content.ReadAsStringAsync();
                    vm.ReferralCode = JToken.Parse(codeJson)["referralCode"]?.ToString() ?? "";
                }

                // 2) stats + referral list
                var statsPayload = new StringContent(
                    JsonConvert.SerializeObject(new { UserID = userId }),
                    Encoding.UTF8, "application/json");
                var statsResp = await client.PostAsync("api/referral/my-stats", statsPayload);
                if (statsResp.IsSuccessStatusCode)
                {
                    var statsJson = await statsResp.Content.ReadAsStringAsync();
                    var token = JToken.Parse(statsJson);

                    var summary = token["summary"];
                    if (summary != null)
                    {
                        vm.TotalReferrals = summary["TotalReferrals"]?.Value<int>() ?? 0;
                        vm.TotalRewardPoints = summary["TotalRewardPoints"]?.Value<int>() ?? 0;
                        vm.TotalInvites = summary["TotalInvites"]?.Value<int>() ?? 0;
                        vm.PendingInvites = summary["PendingInvites"]?.Value<int>() ?? 0;
                        vm.RewardedInvites = summary["RewardedInvites"]?.Value<int>() ?? 0;
                    }

                    var referrals = token["referrals"];
                    if (referrals != null)
                        vm.Referrals = referrals.ToObject<List<ReferralEntryViewModel>>()
                                       ?? new List<ReferralEntryViewModel>();
                }

                // 3) active policy (so the page can show "what you both get")
                var policyResp = await client.GetAsync("api/referral/policy");
                if (policyResp.IsSuccessStatusCode)
                {
                    var policyJson = await policyResp.Content.ReadAsStringAsync();
                    var data = JToken.Parse(policyJson)["data"];
                    if (data != null)
                        vm.Policy = data.ToObject<ReferralPolicyViewModel>();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading referral dashboard");
                TempData["Error"] = $"Error: {ex.Message}";
            }

            return View(vm);
        }

        // ─────────────────────────────────────────
        // GET /Referral/Leaderboard
        // ─────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> Leaderboard()
        {
            var vm = new ReferralLeaderboardViewModel { CurrentUserId = CurrentUserId };

            try
            {
                using var client = CreateClient();
                var response = await client.GetAsync("api/referral/leaderboard?topN=50");
                var json = await response.Content.ReadAsStringAsync();
                vm.Entries = SafeDeserializeList<LeaderboardEntryViewModel>(json);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading referral leaderboard");
                TempData["Error"] = $"Error: {ex.Message}";
            }

            return View(vm);
        }

        // ─────────────────────────────────────────
        // GET /Referral/Policy   (Admin only)
        // ─────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> Policy()
        {
            if (CurrentRoleId != 1)
                return RedirectToAction("List", "Courses");

            var vm = new ReferralPolicyViewModel();
            try
            {
                using var client = CreateClient();
                var response = await client.GetAsync("api/referral/policy");
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var data = JToken.Parse(json)["data"];
                    if (data != null)
                        vm = data.ToObject<ReferralPolicyViewModel>() ?? vm;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading referral policy");
                TempData["Error"] = $"Error: {ex.Message}";
            }
            return View(vm);
        }

        // ─────────────────────────────────────────
        // POST /Referral/Policy   (Admin only)
        // ─────────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Policy(ReferralPolicyViewModel model)
        {
            if (CurrentRoleId != 1)
                return RedirectToAction("List", "Courses");

            if (!ModelState.IsValid)
                return View(model);

            try
            {
                var payload = new
                {
                    model.ReferrerPoints,
                    model.RefereeDiscountType,
                    model.RefereeDiscountValue,
                    model.RefereeMaxDiscount,
                    model.RefereeCouponValidityDays,
                    model.IsActive,
                    ActionBy = CurrentUser
                };

                using var client = CreateClient();
                var content = new StringContent(
                    JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
                var response = await client.PostAsync("api/referral/policy/save", content);
                var json = await response.Content.ReadAsStringAsync();
                var result = ParseApiResponse(json);

                if (response.IsSuccessStatusCode && result.Success)
                {
                    TempData["Success"] = result.Message;
                    return RedirectToAction("Policy");
                }

                TempData["Error"] = result.Message;
                return View(model);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving referral policy");
                TempData["Error"] = $"Error: {ex.Message}";
                return View(model);
            }
        }
    }

    // ══════════════════════════════════════════════
    // VIEW MODELS
    // ══════════════════════════════════════════════
    public class ReferralDashboardViewModel
    {
        public string ReferralCode { get; set; } = "";
        public int TotalReferrals { get; set; }
        public int TotalRewardPoints { get; set; }
        public int TotalInvites { get; set; }
        public int PendingInvites { get; set; }
        public int RewardedInvites { get; set; }
        public List<ReferralEntryViewModel> Referrals { get; set; } = new();
        public ReferralPolicyViewModel Policy { get; set; }
    }

    public class ReferralEntryViewModel
    {
        public int ReferralID { get; set; }
        public int RefereeUserID { get; set; }
        public string RefereeName { get; set; }
        public string RefereeEmail { get; set; }
        public string Status { get; set; }
        public int ReferrerRewardPoints { get; set; }
        public DateTime CreatedDate { get; set; }
        public DateTime? RewardedDate { get; set; }
    }

    public class ReferralLeaderboardViewModel
    {
        public int CurrentUserId { get; set; }
        public List<LeaderboardEntryViewModel> Entries { get; set; } = new();
    }

    public class LeaderboardEntryViewModel
    {
        public int Rank { get; set; }
        public int UserID { get; set; }
        public string Name { get; set; }
        public int TotalReferrals { get; set; }
        public int TotalRewardPoints { get; set; }
    }

    public class ReferralPolicyViewModel
    {
        public int PolicyID { get; set; } = 1;

        [Range(0, 100000, ErrorMessage = "Enter a valid points value")]
        public int ReferrerPoints { get; set; } = 100;

        [Required]
        public string RefereeDiscountType { get; set; } = "FLAT";

        [Range(0, 1000000, ErrorMessage = "Enter a valid discount value")]
        public decimal RefereeDiscountValue { get; set; } = 200;

        public decimal? RefereeMaxDiscount { get; set; }

        [Range(1, 365, ErrorMessage = "Validity must be between 1 and 365 days")]
        public int RefereeCouponValidityDays { get; set; } = 30;

        public bool IsActive { get; set; } = true;
        public string ModifiedBy { get; set; }
        public DateTime? ModifiedDate { get; set; }
    }
}
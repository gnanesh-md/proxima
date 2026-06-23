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
    // ProximaLMS/Controllers/CouponController.cs   (MVC frontend)
    // ------------------------------------------------------------
    // Admin-facing coupon management. Proxies the ProximaLMSAPI
    // CouponController. Admin only (RoleID == 1).
    //
    // FIX (May 2026): CurrentUserId used Session.GetInt32("UserID"),
    // but the login flow writes it via SetString. The int read always
    // returned null, so /Coupon/Validate rejected every coupon with
    // "Please sign in to use a coupon." even for logged-in students.
    // Helper now reads STRING first (matching how it's written).
    // ============================================================
    [RequireJwt]
    public class CouponController : Controller
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _config;
        private readonly ILogger<CouponController> _logger;

        public CouponController(
            IHttpClientFactory httpClientFactory,
            IConfiguration config,
            ILogger<CouponController> logger)
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

        private string CurrentUser => HttpContext.Session.GetString("Email") ?? "Admin";
        private int CurrentRoleId => HttpContext.Session.GetInt32("RoleID") ?? 0;

        // ── ROBUST UserID lookup ─────────────────────────────────────
        // Same fix as ReferralController. Login writes the key as a
        // string; older paths used int. Try every storage form so this
        // helper never spuriously returns 0 for a real logged-in user.
        private int CurrentUserId
        {
            get
            {
                var asStr = HttpContext.Session.GetString("UserID");
                if (int.TryParse(asStr, out var id) && id > 0) return id;

                var asInt = HttpContext.Session.GetInt32("UserID");
                if (asInt.HasValue && asInt.Value > 0) return asInt.Value;

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
                _logger.LogError(ex, "SafeDeserializeList failed. JSON: {Json}", json);
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

        private async Task<List<CourseLookupItem>> LoadCoursesAsync(HttpClient client)
        {
            try
            {
                var resp = await client.GetAsync("api/master/courselist");
                if (!resp.IsSuccessStatusCode) return new List<CourseLookupItem>();
                var json = await resp.Content.ReadAsStringAsync();
                return SafeDeserializeList<CourseLookupItem>(json);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Could not load course lookup");
                return new List<CourseLookupItem>();
            }
        }

        // ─────────────────────────────────────────
        // GET /Coupon/List
        // ─────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> List()
        {
            if (CurrentRoleId != 1)
                return RedirectToAction("List", "Courses");

            try
            {
                using var client = CreateClient();
                var response = await client.GetAsync("api/coupon/list");
                var json = await response.Content.ReadAsStringAsync();
                var coupons = SafeDeserializeList<CouponViewModel>(json);
                return View(coupons);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading coupon list");
                TempData["Error"] = $"Error loading coupons: {ex.Message}";
                return View(new List<CouponViewModel>());
            }
        }

        // ─────────────────────────────────────────
        // GET /Coupon/Create        → blank form
        // GET /Coupon/Create?id=5   → edit mode
        // ─────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> Create(int? id)
        {
            if (CurrentRoleId != 1)
                return RedirectToAction("List", "Courses");

            var vm = new CouponFormViewModel
            {
                IsActive = true,
                DiscountType = "PERCENT",
                StartDate = DateTime.Today,
                EndDate = DateTime.Today.AddDays(30),
                MaxUsesPerUser = 1
            };

            using var client = CreateClient();
            vm.Courses = await LoadCoursesAsync(client);

            if (id.HasValue && id > 0)
            {
                try
                {
                    var response = await client.GetAsync($"api/coupon/{id}");
                    if (response.IsSuccessStatusCode)
                    {
                        var json = await response.Content.ReadAsStringAsync();
                        var token = JToken.Parse(json);
                        var dataToken = token["data"] ?? token["result"] ?? token;
                        var loaded = dataToken.ToObject<CouponFormViewModel>();
                        if (loaded != null)
                        {
                            loaded.Courses = vm.Courses;
                            vm = loaded;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error loading coupon for edit");
                    TempData["Error"] = "Could not load coupon data.";
                }
            }

            return View(vm);
        }

        // ─────────────────────────────────────────
        // POST /Coupon/Create
        // ─────────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(CouponFormViewModel model)
        {
            if (CurrentRoleId != 1)
                return RedirectToAction("List", "Courses");

            using var client = CreateClient();

            if (model.EndDate <= model.StartDate)
                ModelState.AddModelError(nameof(model.EndDate), "End date must be after the start date.");

            if (string.Equals(model.DiscountType, "PERCENT", StringComparison.OrdinalIgnoreCase)
                && model.DiscountValue > 100)
                ModelState.AddModelError(nameof(model.DiscountValue), "Percentage cannot exceed 100.");

            if (!ModelState.IsValid)
            {
                model.Courses = await LoadCoursesAsync(client);
                return View(model);
            }

            try
            {
                var payload = new
                {
                    CouponID = model.CouponID,
                    Code = model.Code?.Trim().ToUpper(),
                    DiscountType = model.DiscountType?.Trim().ToUpper(),
                    DiscountValue = model.DiscountValue,
                    MaxDiscountAmount = model.MaxDiscountAmount,
                    MinOrderAmount = model.MinOrderAmount,
                    CourseID = model.CourseID,           // null = global
                    StartDate = model.StartDate,
                    EndDate = model.EndDate,
                    MaxUses = model.MaxUses,
                    MaxUsesPerUser = model.MaxUsesPerUser,
                    IsActive = model.IsActive,
                    ActionBy = CurrentUser
                };

                var content = new StringContent(
                    JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");

                var response = await client.PostAsync("api/coupon/save", content);
                var json = await response.Content.ReadAsStringAsync();
                var result = ParseApiResponse(json);

                if (response.IsSuccessStatusCode && result.Success)
                {
                    TempData["Success"] = result.Message;
                    return RedirectToAction("List");
                }

                TempData["Error"] = result.Message;
                model.Courses = await LoadCoursesAsync(client);
                return View(model);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving coupon");
                TempData["Error"] = $"Error: {ex.Message}";
                model.Courses = await LoadCoursesAsync(client);
                return View(model);
            }
        }

        // ─────────────────────────────────────────
        // POST /Coupon/ToggleStatus  (AJAX)
        // ─────────────────────────────────────────
        [HttpPost]
        public async Task<IActionResult> ToggleStatus([FromBody] CouponToggleRequest req)
        {
            if (CurrentRoleId != 1)
                return Json(new { success = false, message = "Unauthorized" });

            try
            {
                var payload = new { req.CouponID, req.IsActive, ActionBy = CurrentUser };
                using var client = CreateClient();
                var content = new StringContent(
                    JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
                var response = await client.PostAsync("api/coupon/toggle-status", content);
                var json = await response.Content.ReadAsStringAsync();
                var result = ParseApiResponse(json);
                return Json(new { success = result.Success, message = result.Message });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // ─────────────────────────────────────────
        // POST /Coupon/Delete  (AJAX)
        // ─────────────────────────────────────────
        [HttpPost]
        public async Task<IActionResult> Delete([FromBody] CouponDeleteRequest req)
        {
            if (CurrentRoleId != 1)
                return Json(new { success = false, message = "Unauthorized" });

            try
            {
                var payload = new { req.CouponID };
                using var client = CreateClient();
                var content = new StringContent(
                    JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
                var response = await client.PostAsync("api/coupon/delete", content);
                var json = await response.Content.ReadAsStringAsync();
                var result = ParseApiResponse(json);
                return Json(new { success = result.Success, message = result.Message });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // ─────────────────────────────────────────
        // GET /Coupon/UsageReport
        // ─────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> UsageReport()
        {
            if (CurrentRoleId != 1)
                return RedirectToAction("List", "Courses");

            try
            {
                using var client = CreateClient();
                var response = await client.GetAsync("api/coupon/usage-report");
                var json = await response.Content.ReadAsStringAsync();
                var rows = SafeDeserializeList<CouponUsageReportViewModel>(json);
                return View(rows);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading coupon usage report");
                TempData["Error"] = $"Error: {ex.Message}";
                return View(new List<CouponUsageReportViewModel>());
            }
        }

        // ─────────────────────────────────────────
        // POST /Coupon/Validate  (AJAX)  → for the checkout page
        // Body: { Code, CourseID, OrderAmount }
        // ─────────────────────────────────────────
        [HttpPost]
        public async Task<IActionResult> Validate([FromBody] CouponApplyRequest req)
        {
            int userId = CurrentUserId;
            if (userId <= 0)
            {
                _logger.LogWarning(
                    "Coupon/Validate: no UserID in session. " +
                    "UserID(str)='{Str}', UserID(int)={Int}, SessionKeys=[{Keys}]",
                    HttpContext.Session.GetString("UserID"),
                    HttpContext.Session.GetInt32("UserID"),
                    string.Join(", ", HttpContext.Session.Keys));

                return Json(new { success = false, message = "Please sign in to use a coupon." });
            }

            if (req == null || string.IsNullOrWhiteSpace(req.Code))
                return Json(new { success = false, message = "Enter a coupon code." });

            try
            {
                var payload = new
                {
                    Code = req.Code.Trim().ToUpper(),
                    UserID = userId,
                    req.CourseID,
                    req.OrderAmount
                };
                using var client = CreateClient();
                var content = new StringContent(
                    JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
                var response = await client.PostAsync("api/coupon/validate", content);
                var json = await response.Content.ReadAsStringAsync();
                // pass the API JSON straight back to the browser
                return Content(json, "application/json");
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }
    }

    // ══════════════════════════════════════════════
    // VIEW MODELS
    // ══════════════════════════════════════════════
    public class CouponViewModel
    {
        public int CouponID { get; set; }
        public string Code { get; set; }
        public string DiscountType { get; set; }
        public decimal DiscountValue { get; set; }
        public decimal? MaxDiscountAmount { get; set; }
        public decimal MinOrderAmount { get; set; }
        public int? CourseID { get; set; }
        public string CourseName { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public int MaxUses { get; set; }
        public int MaxUsesPerUser { get; set; }
        public int UsedCount { get; set; }
        public bool IsActive { get; set; }
        public string Source { get; set; }
        public string CreatedBy { get; set; }
        public DateTime CreatedDate { get; set; }
        public string StatusLabel { get; set; }
    }

    public class CouponFormViewModel
    {
        public int CouponID { get; set; }

        [Required(ErrorMessage = "Coupon code is required")]
        [StringLength(40, ErrorMessage = "Max 40 characters")]
        public string Code { get; set; }

        [Required(ErrorMessage = "Discount type is required")]
        public string DiscountType { get; set; } = "PERCENT";

        [Range(0.01, 1000000, ErrorMessage = "Enter a valid discount value")]
        public decimal DiscountValue { get; set; }

        public decimal? MaxDiscountAmount { get; set; }

        [Range(0, 1000000, ErrorMessage = "Minimum order cannot be negative")]
        public decimal MinOrderAmount { get; set; }

        public int? CourseID { get; set; }   // null = applies to all courses

        [Required(ErrorMessage = "Start date is required")]
        public DateTime StartDate { get; set; } = DateTime.Today;

        [Required(ErrorMessage = "End date is required")]
        public DateTime EndDate { get; set; } = DateTime.Today.AddDays(30);

        [Range(0, 1000000, ErrorMessage = "Max uses cannot be negative")]
        public int MaxUses { get; set; }     // 0 = unlimited

        [Range(1, 100, ErrorMessage = "Per-user limit must be at least 1")]
        public int MaxUsesPerUser { get; set; } = 1;

        public bool IsActive { get; set; } = true;

        public List<CourseLookupItem> Courses { get; set; } = new();
    }

    public class CouponUsageReportViewModel
    {
        public int CouponID { get; set; }
        public string Code { get; set; }
        public string DiscountType { get; set; }
        public decimal DiscountValue { get; set; }
        public string Source { get; set; }
        public int MaxUses { get; set; }
        public int UsedCount { get; set; }
        public int TimesRedeemed { get; set; }
        public int UniqueUsers { get; set; }
        public decimal TotalDiscountGiven { get; set; }
        public decimal GrossOrderValue { get; set; }
        public decimal NetRevenue { get; set; }
        public DateTime? LastUsedDate { get; set; }
    }

    public class CourseLookupItem
    {
        public int CourseID { get; set; }
        public string CourseTitle { get; set; }
    }

    // ── Request models ────────────────────────────
    public class CouponToggleRequest
    {
        public int CouponID { get; set; }
        public bool IsActive { get; set; }
    }

    public class CouponDeleteRequest
    {
        public int CouponID { get; set; }
    }

    public class CouponApplyRequest
    {
        public string Code { get; set; }
        public int CourseID { get; set; }
        public decimal OrderAmount { get; set; }
    }
}
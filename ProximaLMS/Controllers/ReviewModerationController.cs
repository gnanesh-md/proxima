// ============================================================
// ProximaLMS/Controllers/ReviewModerationController.cs
// ------------------------------------------------------------
// Admin-only review moderation queue. Proxies to the API.
//
//   GET  /ReviewModeration                 → queue page
//   GET  /ReviewModeration/Queue?status=…  → AJAX rows
//   POST /ReviewModeration/Moderate        → approve / reject one
//   POST /ReviewModeration/BulkModerate    → approve / reject many
// ============================================================
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using ProximaLMS.Filters;
using ProximaLMS.Models;

namespace ProximaLMS.Controllers
{
    [RequireJwt]
    public class ReviewModerationController : Controller
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _config;
        private readonly ILogger<ReviewModerationController> _logger;

        public ReviewModerationController(
            IHttpClientFactory httpClientFactory,
            IConfiguration config,
            ILogger<ReviewModerationController> logger)
        {
            _httpClientFactory = httpClientFactory;
            _config            = config;
            _logger            = logger;
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

        private bool IsAdmin() => (HttpContext.Session.GetInt32("RoleID") ?? 0) == 1;


        // ─────────────────────────────────────────────────────────
        // GET /ReviewModeration
        // ─────────────────────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> Index(string status = "PENDING")
        {
            if (!IsAdmin())
            {
                TempData["Error"] = "You don't have permission to moderate reviews.";
                return RedirectToAction("List", "Courses");
            }

            var vm = new ReviewQueueViewModel { ActiveFilter = (status ?? "PENDING").ToUpper() };

            try
            {
                var client = CreateClient();

                // counts
                var countResp = await client.GetAsync("api/review/queue/counts");
                if (countResp.IsSuccessStatusCode)
                {
                    var json   = await countResp.Content.ReadAsStringAsync();
                    var counts = JsonConvert.DeserializeObject<ReviewQueueCounts>(json);
                    if (counts != null) vm.Counts = counts;
                }

                // rows
                vm.Reviews = await LoadQueueAsync(client, vm.ActiveFilter);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading moderation queue");
                TempData["Error"] = "Could not load the moderation queue.";
            }

            return View(vm);
        }


        // ─────────────────────────────────────────────────────────
        // GET /ReviewModeration/Queue?status=PENDING   (AJAX → JSON)
        // ─────────────────────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> Queue(string status = "PENDING")
        {
            if (!IsAdmin())
                return Json(new { success = false, message = "Unauthorized" });

            try
            {
                var client = CreateClient();
                var rows   = await LoadQueueAsync(client, (status ?? "PENDING").ToUpper());

                ReviewQueueCounts counts = new();
                var countResp = await client.GetAsync("api/review/queue/counts");
                if (countResp.IsSuccessStatusCode)
                {
                    var cjson = await countResp.Content.ReadAsStringAsync();
                    counts = JsonConvert.DeserializeObject<ReviewQueueCounts>(cjson) ?? new();
                }

                return Json(new { success = true, reviews = rows, counts });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading queue rows");
                return Json(new { success = false, message = ex.Message });
            }
        }


        // ─────────────────────────────────────────────────────────
        // POST /ReviewModeration/Moderate   (AJAX → JSON)
        // ─────────────────────────────────────────────────────────
        [HttpPost]
        public async Task<IActionResult> Moderate([FromBody] ModerateRequest req)
        {
            if (!IsAdmin())
                return Json(new { success = false, message = "Unauthorized" });

            if (req == null || req.ReviewID <= 0)
                return Json(new { success = false, message = "Invalid review." });

            try
            {
                var client = CreateClient();
                var email  = HttpContext.Session.GetString("Email") ?? "Admin";

                var payload = new
                {
                    ReviewID    = req.ReviewID,
                    NewStatus   = req.NewStatus,
                    ModeratedBy = email,
                    Note        = req.Note ?? ""
                };

                var content = new StringContent(
                    JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");

                var resp = await client.PostAsync("api/review/moderate", content);
                var json = await resp.Content.ReadAsStringAsync();
                return Content(json, "application/json");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error moderating review");
                return Json(new { success = false, message = ex.Message });
            }
        }


        // ─────────────────────────────────────────────────────────
        // POST /ReviewModeration/BulkModerate   (AJAX → JSON)
        // ─────────────────────────────────────────────────────────
        [HttpPost]
        public async Task<IActionResult> BulkModerate([FromBody] BulkModerateRequest req)
        {
            if (!IsAdmin())
                return Json(new { success = false, message = "Unauthorized" });

            if (req == null || req.ReviewIDs == null || req.ReviewIDs.Count == 0)
                return Json(new { success = false, message = "No reviews selected." });

            try
            {
                var client = CreateClient();
                var email  = HttpContext.Session.GetString("Email") ?? "Admin";

                var payload = new
                {
                    ReviewIDs   = req.ReviewIDs,
                    NewStatus   = req.NewStatus,
                    ModeratedBy = email
                };

                var content = new StringContent(
                    JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");

                var resp = await client.PostAsync("api/review/bulk-moderate", content);
                var json = await resp.Content.ReadAsStringAsync();
                return Content(json, "application/json");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error bulk-moderating");
                return Json(new { success = false, message = ex.Message });
            }
        }


        // ── helper ────────────────────────────────────────────────
        private async Task<List<ReviewQueueItem>> LoadQueueAsync(HttpClient client, string status)
        {
            var resp = await client.GetAsync($"api/review/queue?status={status}");
            if (!resp.IsSuccessStatusCode)
                return new List<ReviewQueueItem>();

            var json = await resp.Content.ReadAsStringAsync();
            return JsonConvert.DeserializeObject<List<ReviewQueueItem>>(json)
                   ?? new List<ReviewQueueItem>();
        }
    }


    // ════════════════════════════════════════════════════════════
    //  VIEW MODELS  (place in ProximaLMS/Models if you prefer —
    //  kept here so the controller is a single drop-in file)
    // ════════════════════════════════════════════════════════════
    public class ReviewQueueViewModel
    {
        public string ActiveFilter { get; set; } = "PENDING";
        public ReviewQueueCounts Counts { get; set; } = new();
        public List<ReviewQueueItem> Reviews { get; set; } = new();
    }

    public class ReviewQueueCounts
    {
        public int PendingCount  { get; set; }
        public int ApprovedCount { get; set; }
        public int RejectedCount { get; set; }
        public int TotalCount    { get; set; }
    }

    public class ReviewQueueItem
    {
        public int       ReviewID       { get; set; }
        public int       CourseID       { get; set; }
        public string    CourseTitle    { get; set; } = "";
        public int       StudentID      { get; set; }
        public string    StudentName    { get; set; } = "";
        public string    StudentEmail   { get; set; } = "";
        public int       Rating         { get; set; }
        public string    ReviewText     { get; set; } = "";
        public string    Status         { get; set; } = "";
        public string?   ModeratedBy    { get; set; }
        public DateTime? ModeratedDate  { get; set; }
        public string?   ModerationNote { get; set; }
        public DateTime  CreatedDate    { get; set; }
        public DateTime? UpdatedDate    { get; set; }
    }

    public class ModerateRequest
    {
        public int     ReviewID  { get; set; }
        public string  NewStatus { get; set; } = "";   // APPROVED | REJECTED
        public string? Note      { get; set; }
    }

    public class BulkModerateRequest
    {
        public List<int> ReviewIDs { get; set; } = new();
        public string    NewStatus { get; set; } = "";
    }
}

// ============================================================
// ProximaLMS/Controllers/AuditLogController.cs
// ------------------------------------------------------------
// FIX (May 2026):
//   • AuditStats now has [JsonProperty] mappings so the SP's
//     column names (TotalEvents, FailureCount) deserialize
//     into the view-friendly property names (TotalEntries,
//     FailedActions) WITHOUT changing the .cshtml view.
//   • DistinctActors kept on the view model but always 0 — the
//     existing SP_AuditLog_Stats doesn't return it. (Add it
//     to the SP later if you want the tile populated.)
// ============================================================
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using ProximaLMS.Filters;

namespace ProximaLMS.Controllers
{
    [RequireJwt]
    public class AuditLogController : Controller
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _config;
        private readonly ILogger<AuditLogController> _logger;

        public AuditLogController(
            IHttpClientFactory httpClientFactory,
            IConfiguration config,
            ILogger<AuditLogController> logger)
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

        private bool IsAdmin()
            => (HttpContext.Session.GetInt32("RoleID") ?? 0) == 1;


        // ─────────────────────────────────────────────────────────
        // GET /AuditLog  → admin grid screen
        // ─────────────────────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> Index()
        {
            if (!IsAdmin())
            {
                TempData["Error"] = "You don't have access to the audit log.";
                return RedirectToAction("List", "Courses");
            }

            var vm = new AuditLogViewModel();

            try
            {
                var client = CreateClient();

                // stats
                var statsResp = await client.GetAsync("api/audit/stats");
                if (statsResp.IsSuccessStatusCode)
                {
                    var json = await statsResp.Content.ReadAsStringAsync();
                    vm.Stats = JsonConvert.DeserializeObject<AuditStats>(json) ?? new();
                }

                // filter dropdown values
                var filterResp = await client.GetAsync("api/audit/filters");
                if (filterResp.IsSuccessStatusCode)
                {
                    var json = await filterResp.Content.ReadAsStringAsync();
                    var filters = JsonConvert.DeserializeObject<AuditFilterOptions>(json);
                    if (filters != null) vm.Filters = filters;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading audit screen");
                TempData["Error"] = "Could not load the audit log.";
            }

            return View(vm);
        }


        // ─────────────────────────────────────────────────────────
        // GET /AuditLog/Recent?limit=N   (AJAX → JSON for dashboard)
        // ─────────────────────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> Recent(int limit = 7)
        {
            if (!IsAdmin()) return Json(new object[0]);
            try
            {
                var client = CreateClient();
                var resp = await client.GetAsync($"api/audit/search?page=1&pageSize={limit}");
                if (!resp.IsSuccessStatusCode) return Json(new object[0]);
                var json = await resp.Content.ReadAsStringAsync();
                var root = Newtonsoft.Json.Linq.JObject.Parse(json);
                // API (SP_AuditLog_Search) returns the list under "rows".
                var items = root["rows"] ?? root["Rows"]
                            ?? root["data"] ?? root["Data"]
                            ?? root["items"] ?? root["Items"];
                if (items == null) return Json(new object[0]);
                return Content(items.ToString(), "application/json");
            }
            catch { return Json(new object[0]); }
        }


        // ─────────────────────────────────────────────────────────
        // GET /AuditLog/Search   (AJAX → JSON)
        // ─────────────────────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> Search(
            string fromDate = "",
            string toDate = "",
            string entityName = "",
            string actionType = "",
            string search = "",
            string outcome = "",
            int page = 1,
            int pageSize = 25)
        {
            if (!IsAdmin())
                return Json(new { success = false, message = "Unauthorized" });

            try
            {
                var client = CreateClient();

                var query = $"api/audit/search?page={page}&pageSize={pageSize}"
                          + $"&entityName={Uri.EscapeDataString(entityName ?? "")}"
                          + $"&actionType={Uri.EscapeDataString(actionType ?? "")}"
                          + $"&search={Uri.EscapeDataString(search ?? "")}"
                          + $"&outcome={Uri.EscapeDataString(outcome ?? "")}";

                if (!string.IsNullOrWhiteSpace(fromDate))
                    query += $"&fromDate={Uri.EscapeDataString(fromDate)}";
                if (!string.IsNullOrWhiteSpace(toDate))
                    query += $"&toDate={Uri.EscapeDataString(toDate)}";

                var resp = await client.GetAsync(query);
                var json = await resp.Content.ReadAsStringAsync();
                return Content(json, "application/json");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching audit log");
                return Json(new { success = false, message = ex.Message });
            }
        }
    }


    // ════════════════════════════════════════════════════════════
    //  VIEW MODELS
    // ════════════════════════════════════════════════════════════
    public class AuditLogViewModel
    {
        public AuditStats Stats { get; set; } = new();
        public AuditFilterOptions Filters { get; set; } = new();
    }

    public class AuditStats
    {
        // ✅ JsonProperty maps the SP's column name to the view's expected property.
        // Lets us fix the binding without touching AuditLog/Index.cshtml.

        [JsonProperty("TotalEvents")]
        public int TotalEntries { get; set; }

        public int Last24h { get; set; }
        public int Last7d { get; set; }

        [JsonProperty("FailureCount")]
        public int FailedActions { get; set; }

        // Not returned by SP_AuditLog_Stats yet. Always 0 until the SP is updated.
        public int DistinctActors { get; set; }
    }

    public class AuditFilterOptions
    {
        public List<string> Entities { get; set; } = new();
        public List<string> ActionTypes { get; set; } = new();
    }
}

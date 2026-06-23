using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;
using System.Net.Http.Headers;

namespace ProximaLMS.Controllers
{
    public class AnalyticsController : Controller
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _config;

        public AnalyticsController(IHttpClientFactory httpClientFactory, IConfiguration config)
        {
            _httpClientFactory = httpClientFactory;
            _config = config;
        }

        private (System.Net.Http.HttpClient client, int roleId) Api()
        {
            var token = HttpContext.Session.GetString("JwtToken");
            var roleId = HttpContext.Session.GetInt32("RoleID") ?? 0;
            var client = _httpClientFactory.CreateClient();
            client.BaseAddress = new Uri(_config["ApiBaseUrl"]);
            if (!string.IsNullOrEmpty(token))
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return (client, roleId);
        }

        // GET /Analytics — admin only
        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var (client, roleId) = Api();
            if (roleId != 1) return RedirectToAction("Index", "Dashboard");

            var o = new JObject();
            try
            {
                var resp = await client.GetAsync("api/Reports/admin/overview");
                if (resp.IsSuccessStatusCode)
                    o = JObject.Parse(await resp.Content.ReadAsStringAsync());
            }
            catch { }

            ViewBag.Health = o["health"] as JObject ?? new JObject();
            ViewBag.Revenue = o["revenue"] as JArray ?? new JArray();
            ViewBag.TopCourses = o["topCourses"] as JArray ?? new JArray();
            ViewBag.Coupons = o["coupons"] as JArray ?? new JArray();
            ViewBag.CouponSummary = o["couponSummary"] as JObject ?? new JObject();
            return View();
        }

        // GET /Analytics/TopCourses  (AJAX → JSON for admin dashboard)
        [HttpGet]
        public async Task<IActionResult> TopCourses(int limit = 8)
        {
            var (client, roleId) = Api();
            if (roleId != 1) return Json(new object[0]);
            try
            {
                var resp = await client.GetAsync($"api/Reports/admin/top-courses?limit={limit}");
                if (!resp.IsSuccessStatusCode) return Json(new object[0]);
                var json = await resp.Content.ReadAsStringAsync();
                var root = Newtonsoft.Json.Linq.JToken.Parse(json);
                // Handle both array and {data:[...]} shapes
                if (root is Newtonsoft.Json.Linq.JArray) return Content(json, "application/json");
                var data = root["data"] ?? root["Data"] ?? root["courses"] ?? root["Courses"];
                return Content(data?.ToString() ?? "[]", "application/json");
            }
            catch { return Json(new object[0]); }
        }


        // GET /Analytics/Export?report=NAME&id=&from=&to=  — proxies the API CSV stream
        [HttpGet]
        public async Task<IActionResult> Export(string report, int? id, DateTime? from, DateTime? to)
        {
            var (client, roleId) = Api();
            if (roleId != 1) return Forbid();

            var qs = $"report={Uri.EscapeDataString(report ?? "")}";
            if (id.HasValue) qs += $"&id={id}";
            if (from.HasValue) qs += $"&from={from:yyyy-MM-dd}";
            if (to.HasValue) qs += $"&to={to:yyyy-MM-dd}";

            var resp = await client.GetAsync($"api/Reports/export?{qs}");
            if (!resp.IsSuccessStatusCode) return NotFound();

            var bytes = await resp.Content.ReadAsByteArrayAsync();
            var fname = resp.Content.Headers.ContentDisposition?.FileName?.Trim('"') ?? $"{report}.csv";
            return File(bytes, "text/csv", fname);
        }
    }
}

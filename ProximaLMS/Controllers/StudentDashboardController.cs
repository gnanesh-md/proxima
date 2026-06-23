
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;
using System.Net.Http.Headers;

namespace ProximaLMS.Controllers
{
    public class StudentDashboardController : Controller
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _config;

        public StudentDashboardController(IHttpClientFactory httpClientFactory, IConfiguration config)
        {
            _httpClientFactory = httpClientFactory;
            _config = config;
        }

        public async Task<IActionResult> Index()
        {
            var token = HttpContext.Session.GetString("JwtToken");
            var userId = HttpContext.Session.GetString("UserID");
            var name = HttpContext.Session.GetString("Email") ?? "Student";
            if (string.IsNullOrEmpty(userId)) return RedirectToAction("Index", "Login");

            var client = _httpClientFactory.CreateClient();
            client.BaseAddress = new Uri(_config["ApiBaseUrl"]);
            if (!string.IsNullOrEmpty(token))
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            ViewBag.Summary = new JObject();
            ViewBag.Pending = new JArray();
            ViewBag.StudentName = name;

            try
            {
                var resp = await client.GetAsync($"api/Reports/student/{userId}");
                if (resp.IsSuccessStatusCode)
                {
                    var o = JObject.Parse(await resp.Content.ReadAsStringAsync());
                    ViewBag.Summary = o["summary"] as JObject ?? new JObject();
                    ViewBag.Pending = o["pending"] as JArray ?? new JArray();
                }
            }
            catch { /* show zeros */ }

            // Try to resolve student's display name from session
            var fullName = HttpContext.Session.GetString("FullName");
            if (!string.IsNullOrWhiteSpace(fullName)) ViewBag.StudentName = fullName;

            return View();
        }

        // GET /StudentDashboard/WeeklyActivity  (AJAX → JSON for bar chart)
        [HttpGet]
        public async Task<IActionResult> WeeklyActivity()
        {
            var token = HttpContext.Session.GetString("JwtToken");
            var userId = HttpContext.Session.GetString("UserID");
            if (string.IsNullOrEmpty(userId)) return Json(new int[7]);

            var client = _httpClientFactory.CreateClient();
            client.BaseAddress = new Uri(_config["ApiBaseUrl"]);
            if (!string.IsNullOrEmpty(token))
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            try
            {
                var resp = await client.GetAsync($"api/Reports/student/{userId}/weekly-activity");
                if (resp.IsSuccessStatusCode)
                {
                    var json = await resp.Content.ReadAsStringAsync();
                    return Content(json, "application/json");
                }
            }
            catch { }

            return Json(new int[] { 0, 0, 0, 0, 0, 0, 0 });
        }
    }
}

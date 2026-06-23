
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;
using System.Net.Http.Headers;

namespace ProximaLMS.Controllers
{
    public class RewardsController : Controller
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _config;

        public RewardsController(IHttpClientFactory httpClientFactory, IConfiguration config)
        {
            _httpClientFactory = httpClientFactory;
            _config = config;
        }

        private (System.Net.Http.HttpClient client, string? userId) Api()
        {
            var token = HttpContext.Session.GetString("JwtToken");
            var userId = HttpContext.Session.GetString("UserID");
            var client = _httpClientFactory.CreateClient();
            client.BaseAddress = new Uri(_config["ApiBaseUrl"]);
            if (!string.IsNullOrEmpty(token))
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return (client, userId);
        }

        private async Task<JToken?> GetJson(System.Net.Http.HttpClient c, string url, string key)
        {
            try
            {
                var resp = await c.GetAsync(url);
                if (!resp.IsSuccessStatusCode) return null;
                var json = await resp.Content.ReadAsStringAsync();
                var o = JObject.Parse(json);
                return o[key];
            }
            catch { return null; }
        }

        // GET /Rewards  — the dashboard
        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var (client, userId) = Api();
            if (string.IsNullOrEmpty(userId)) return RedirectToAction("Index", "Login");

            ViewBag.Summary = await GetJson(client, $"api/Gamification/summary/{userId}", "data") ?? new JObject();
            ViewBag.Badges = await GetJson(client, $"api/Gamification/badges/{userId}", "data") ?? new JArray();
            ViewBag.History = await GetJson(client, $"api/Gamification/history/{userId}", "data") ?? new JArray();

            // leaderboard returns two roots (top + me) — fetch raw
            try
            {
                var resp = await client.GetAsync($"api/Gamification/leaderboard/{userId}?topN=50");
                if (resp.IsSuccessStatusCode)
                {
                    var o = JObject.Parse(await resp.Content.ReadAsStringAsync());
                    ViewBag.Top = o["top"] as JArray ?? new JArray();
                    ViewBag.Me = o["me"] ?? new JObject();
                }
            }
            catch { ViewBag.Top = new JArray(); ViewBag.Me = new JObject(); }

            ViewBag.StudentId = userId;
            return View();
        }

        // GET /Rewards/Notifications  — unseen badges (polled by JS)
        [HttpGet]
        public async Task<IActionResult> Notifications()
        {
            var (client, userId) = Api();
            if (string.IsNullOrEmpty(userId)) return Json(new { data = new JArray() });
            var data = await GetJson(client, $"api/Gamification/notifications/{userId}", "data") ?? new JArray();
            return Content(new JObject { ["data"] = data }.ToString(), "application/json");
        }

        // POST /Rewards/MarkSeen
        [HttpPost]
        public async Task<IActionResult> MarkSeen()
        {
            var (client, userId) = Api();
            if (string.IsNullOrEmpty(userId)) return Json(new { success = false });
            var resp = await client.PostAsync($"api/Gamification/notifications/{userId}/seen", null);
            return Json(new { success = resp.IsSuccessStatusCode });
        }
    }
}

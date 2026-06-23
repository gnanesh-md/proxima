using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;
using System.Net.Http.Headers;
using System.Text;

namespace ProximaLMS.Controllers
{
    public class NotificationsController : Controller
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _config;

        public NotificationsController(IHttpClientFactory httpClientFactory, IConfiguration config)
        {
            _httpClientFactory = httpClientFactory;
            _config = config;
        }

        private (System.Net.Http.HttpClient client, string? userId, int roleId) Api()
        {
            var token = HttpContext.Session.GetString("JwtToken");
            var userId = HttpContext.Session.GetString("UserID");
            var roleId = HttpContext.Session.GetInt32("RoleID") ?? 0;
            var client = _httpClientFactory.CreateClient();
            client.BaseAddress = new Uri(_config["ApiBaseUrl"]);
            if (!string.IsNullOrEmpty(token))
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return (client, userId, roleId);
        }

        // GET /Notifications — full list
        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var (client, userId, _) = Api();
            if (string.IsNullOrEmpty(userId)) return RedirectToAction("Index", "Login");

            var list = new JArray();
            try
            {
                var resp = await client.GetAsync($"api/Notification/list/{userId}?limit=100");
                if (resp.IsSuccessStatusCode)
                    list = JObject.Parse(await resp.Content.ReadAsStringAsync())["data"] as JArray ?? new JArray();
            }
            catch { }
            ViewBag.Items = list;
            ViewBag.UserId = userId;
            return View();
        }

        // GET /Notifications/Preferences
        [HttpGet]
        public async Task<IActionResult> Preferences()
        {
            var (client, userId, _) = Api();
            if (string.IsNullOrEmpty(userId)) return RedirectToAction("Index", "Login");

            var prefs = new JObject();
            try
            {
                var resp = await client.GetAsync($"api/Notification/preferences/{userId}");
                if (resp.IsSuccessStatusCode)
                    prefs = JObject.Parse(await resp.Content.ReadAsStringAsync())["data"] as JObject ?? new JObject();
            }
            catch { }
            ViewBag.Prefs = prefs;
            return View();
        }

        // POST /Notifications/Preferences
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Preferences(bool EmailEnabled, bool SmsEnabled, bool InAppEnabled, string? MutedEvents)
        {
            var (client, userId, _) = Api();
            if (string.IsNullOrEmpty(userId)) return RedirectToAction("Index", "Login");

            var body = new JObject
            {
                ["UserID"] = int.Parse(userId),
                ["EmailEnabled"] = EmailEnabled,
                ["SmsEnabled"] = SmsEnabled,
                ["InAppEnabled"] = InAppEnabled,
                ["MutedEvents"] = MutedEvents ?? ""
            };
            try
            {
                await client.PostAsync("api/Notification/preferences",
                    new StringContent(body.ToString(), Encoding.UTF8, "application/json"));
                TempData["PrefSaved"] = "Preferences saved.";
            }
            catch { TempData["PrefError"] = "Could not save preferences."; }
            return RedirectToAction("Preferences");
        }

        // GET /Notifications/Broadcast — admin only
        [HttpGet]
        public IActionResult Broadcast()
        {
            var (_, userId, roleId) = Api();
            if (string.IsNullOrEmpty(userId)) return RedirectToAction("Index", "Login");
            if (roleId != 1) return Forbid();
            return View();
        }

        // POST /Notifications/Broadcast
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Broadcast(string Title, string? Body, string? LinkUrl, string? RoleFilter)
        {
            var (client, userId, roleId) = Api();
            if (string.IsNullOrEmpty(userId)) return RedirectToAction("Index", "Login");
            if (roleId != 1) return Forbid();

            var body = new JObject
            {
                ["RoleFilter"] = RoleFilter ?? "",
                ["Title"] = Title,
                ["Body"] = Body ?? "",
                ["LinkUrl"] = LinkUrl ?? ""
            };
            try
            {
                var resp = await client.PostAsync("api/Notification/broadcast",
                    new StringContent(body.ToString(), Encoding.UTF8, "application/json"));
                if (resp.IsSuccessStatusCode)
                {
                    var n = JObject.Parse(await resp.Content.ReadAsStringAsync())["recipients"];
                    TempData["BcSuccess"] = $"Broadcast sent to {n} user(s).";
                }
                else TempData["BcError"] = "Broadcast failed.";
            }
            catch { TempData["BcError"] = "Broadcast failed."; }
            return RedirectToAction("Broadcast");
        }
    }
}

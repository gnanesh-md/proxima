using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;
using System.Net.Http.Headers;

namespace ProximaLMS.Controllers
{
    public class GamificationController : Controller
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _config;

        public GamificationController(IHttpClientFactory httpClientFactory, IConfiguration config)
        {
            _httpClientFactory = httpClientFactory;
            _config = config;
        }

        private (System.Net.Http.HttpClient client, string userId) Session()
        {
            var token = HttpContext.Session.GetString("JwtToken");
            var userId = HttpContext.Session.GetString("UserID") ?? "";
            var client = _httpClientFactory.CreateClient();
            client.BaseAddress = new Uri(_config["ApiBaseUrl"]);
            if (!string.IsNullOrEmpty(token))
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return (client, userId);
        }

        // GET /Gamification/Leaderboard  (full page)
        [HttpGet]
        public async Task<IActionResult> Leaderboard()
        {
            var (client, userId) = Session();
            if (string.IsNullOrEmpty(userId)) return RedirectToAction("Index", "Login");

            ViewBag.Top = new JArray();
            ViewBag.Me = new JObject();

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
            catch { }

            return View();
        }

        // GET /Gamification/MyBadges  (full page)
        [HttpGet]
        public async Task<IActionResult> MyBadges()
        {
            var (client, userId) = Session();
            if (string.IsNullOrEmpty(userId)) return RedirectToAction("Index", "Login");
            ViewBag.Badges = new JArray();
            try
            {
                var resp = await client.GetAsync($"api/Gamification/badges/{userId}");
                if (resp.IsSuccessStatusCode)
                {
                    var json = await resp.Content.ReadAsStringAsync();
                    var root = JToken.Parse(json);
                    ViewBag.Badges = root is JArray arr ? arr : (root["data"] as JArray ?? new JArray());
                }
            }
            catch { }
            return View();
        }

        // GET /Gamification/TopLearners?limit=N  (AJAX JSON for student dashboard widget)
        [HttpGet]
        public async Task<IActionResult> TopLearners(int limit = 5)
        {
            var (client, userId) = Session();
            try
            {
                var resp = await client.GetAsync($"api/Gamification/leaderboard/{userId}?topN={limit}");
                if (resp.IsSuccessStatusCode)
                {
                    var json = await resp.Content.ReadAsStringAsync();
                    var root = JObject.Parse(json);
                    var top = root["top"] as JArray ?? new JArray();
                    var me = root["me"] as JObject;
                    // Mark the current user's row
                    if (me != null)
                    {
                        bool foundMe = false;
                        foreach (var item in top)
                        {
                            if ((item["studentId"]?.Value<string>() ?? item["StudentId"]?.Value<string>()) == userId)
                            {
                                (item as JObject)?.Add("isCurrentUser", true);
                                foundMe = true;
                                break;
                            }
                        }
                        if (!foundMe && top.Count < limit)
                        {
                            var meCopy = me.DeepClone() as JObject;
                            meCopy?.Add("isCurrentUser", true);
                            if (meCopy != null) top.Add(meCopy);
                        }
                    }
                    return Content(top.ToString(), "application/json");
                }
            }
            catch { }
            return Json(new object[0]);
        }
    }
}

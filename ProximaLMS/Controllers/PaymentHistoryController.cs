using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;
using System.Net.Http.Headers;

namespace ProximaLMS.Controllers
{
    public class PaymentHistoryController : Controller
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _config;

        public PaymentHistoryController(IHttpClientFactory httpClientFactory, IConfiguration config)
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

        // GET /PaymentHistory  — student's invoices
        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var (client, userId) = Api();
            if (string.IsNullOrEmpty(userId)) return RedirectToAction("Index", "Login");

            var list = new JArray();
            try
            {
                var resp = await client.GetAsync($"api/Invoice/student/{userId}");
                if (resp.IsSuccessStatusCode)
                {
                    var json = await resp.Content.ReadAsStringAsync();
                    list = JObject.Parse(json)["data"] as JArray ?? new JArray();
                }
            }
            catch { /* empty state */ }

            ViewBag.Invoices = list;
            return View();
        }

        // GET /PaymentHistory/Invoice/{id}  — proxy the PDF
        [HttpGet]
        public async Task<IActionResult> Invoice(int id)
        {
            var (client, userId) = Api();
            if (string.IsNullOrEmpty(userId)) return RedirectToAction("Index", "Login");

            if (id <= 0)
            {
                TempData["Error"] = "Invalid invoice.";
                return RedirectToAction("Index");
            }

            try
            {
                var resp = await client.GetAsync($"api/Invoice/{id}/download");
                if (!resp.IsSuccessStatusCode)
                {
                    // Try to read the API error message for better diagnostics
                    string apiMsg = "";
                    try
                    {
                        var errJson = await resp.Content.ReadAsStringAsync();
                        var errObj = Newtonsoft.Json.JsonConvert.DeserializeObject<dynamic>(errJson);
                        apiMsg = (string?)errObj?.message ?? "";
                    }
                    catch { }

                    TempData["Error"] = string.IsNullOrEmpty(apiMsg)
                        ? $"Invoice could not be downloaded (HTTP {(int)resp.StatusCode})."
                        : $"Invoice error: {apiMsg}";
                    return RedirectToAction("Index");
                }

                var bytes = await resp.Content.ReadAsByteArrayAsync();
                var name = resp.Content.Headers.ContentDisposition?.FileName?.Trim('"') ?? $"invoice-{id}.pdf";
                return File(bytes, "application/pdf", name);
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"Unable to download invoice: {ex.Message}";
                return RedirectToAction("Index");
            }
        }
    }
}

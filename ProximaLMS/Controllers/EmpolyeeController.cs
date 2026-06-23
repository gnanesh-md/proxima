using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ProximaLMS.Models;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace ProximaLMS.Controllers
{
    public class EmployeeController : Controller
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _config;

        public EmployeeController(IHttpClientFactory httpClientFactory, IConfiguration config)
        {
            _httpClientFactory = httpClientFactory;
            _config = config;
        }

        [HttpGet]
        public async Task<IActionResult> Register()
        {
            await LoadRolesAsync();
            // Initialize with default country code
            return View(new EmployeeRequest { CountryCode = "+91" });
        }

        // Populates ViewBag.Roles for the Register view's role dropdown.
        private async Task LoadRolesAsync()
        {
            var roles = new List<SelectListItem>();
            try
            {
                var token = HttpContext.Session.GetString("JwtToken");
                var client = _httpClientFactory.CreateClient();
                client.BaseAddress = new Uri(_config["ApiBaseUrl"]);
                if (!string.IsNullOrEmpty(token))
                    client.DefaultRequestHeaders.Authorization =
                        new AuthenticationHeaderValue("Bearer", token);

                var resp = await client.GetAsync("api/Role/list");
                if (resp.IsSuccessStatusCode)
                {
                    var json = await resp.Content.ReadAsStringAsync();
                    foreach (var row in JArray.Parse(json))
                    {
                        var id = (row["RoleID"] ?? row["roleID"] ?? row["roleId"])?.ToString();
                        var name = (row["RoleName"] ?? row["roleName"])?.ToString();

                        // skip inactive roles if the API exposes an IsActive flag
                        var act = row["IsActive"] ?? row["isActive"];
                        if (act != null && act.Type != JTokenType.Null)
                        {
                            var s = act.ToString();
                            if (s == "0" || s.Equals("false", StringComparison.OrdinalIgnoreCase))
                                continue;
                        }

                        if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(name))
                            roles.Add(new SelectListItem(name, id));
                    }
                }
            }
            catch { /* leave empty; the view shows the placeholder option */ }

            ViewBag.Roles = roles;
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Register(EmployeeRequest model)
        {
            // reload roles so the dropdown survives a redisplay on validation/API errors
            await LoadRolesAsync();

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            // Map the View model to the flat structure the API expects
            var apiRequest = new
            {
                Name = model.FullName,
                Gender = model.Gender,
                MobileNumber = $"{model.CountryCode}{model.PhoneNumber}",
                Email = model.Email,
                Password = model.Password,
                RoleID = model.RoleID
            };

            try
            {
                var client = _httpClientFactory.CreateClient();
                client.BaseAddress = new Uri(_config["ApiBaseUrl"]);

                // Target the correct API route
                var response = await client.PostAsJsonAsync("api/Employee/register", apiRequest);

                if (response.IsSuccessStatusCode)
                {
                    TempData["RegisterSuccess"] = "Employee registered successfully!";
                    return RedirectToAction("Register");
                }
                else
                {
                    // Read the API's error message safely. The API returns { "message": ... }
                    // (or "Message"); reading it off a dynamic JsonElement throws, so parse the
                    // raw body instead and fall back to a generic message.
                    string apiMsg = "Registration failed. Details might already exist.";
                    try
                    {
                        var errBody = await response.Content.ReadAsStringAsync();
                        if (!string.IsNullOrWhiteSpace(errBody))
                        {
                            var jo = Newtonsoft.Json.Linq.JObject.Parse(errBody);
                            apiMsg = (jo["message"] ?? jo["Message"])?.ToString() ?? apiMsg;
                        }
                    }
                    catch { /* non-JSON body — keep the generic message */ }

                    TempData["RegisterError"] = apiMsg;
                    return View(model);
                }
            }
            catch (Exception ex)
            {
                TempData["RegisterError"] = "Connection Error: " + ex.Message;
                return View(model);
            }
        }

        [HttpPost]
        public async Task<IActionResult> ToggleStatus([FromBody] EmployeeStatusModel model)
        {
            if (model == null)
                return BadRequest(new { success = false });

            var client = _httpClientFactory.CreateClient();
            client.BaseAddress = new Uri(_config["ApiBaseUrl"]);

            var response = await client.PostAsJsonAsync("api/employee/toggle-status", model);

            if (response.IsSuccessStatusCode)
                return Json(new { success = true });

            return Json(new { success = false });
        }


        [HttpGet]
        public async Task<IActionResult> List()
        {
            var client = _httpClientFactory.CreateClient();
            var apiBaseUrl = _config["ApiBaseUrl"];

            if (string.IsNullOrEmpty(apiBaseUrl))
            {
                TempData["Error"] = "API Base URL is not configured";
                return View(new List<EmployeeDto>());
            }

            client.BaseAddress = new Uri(apiBaseUrl);

            try
            {

                var response = await client.GetAsync("api/employee/list");

                if (!response.IsSuccessStatusCode)
                {
                    TempData["Error"] = "Failed to load tutors";
                    return View(new List<EmployeeDto>());
                }

                var json = await response.Content.ReadAsStringAsync();
                var data = JsonConvert.DeserializeObject<List<EmployeeDto>>(json)
                           ?? new List<EmployeeDto>();


                return View(data);
            }
            catch (Exception ex)
            {

                TempData["Error"] = $"Error loading tutors: {ex.Message}";
                return View(new List<EmployeeDto>());
            }
        }
    }
}
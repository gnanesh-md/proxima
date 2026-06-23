using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ProximaLMS.Filters;
using ProximaLMS.Models;
using System.ComponentModel.DataAnnotations;
using System.Net.Http.Headers;
using System.Text;

namespace ProximaLMS.Controllers
{
    [RequireJwt]
    public class RoleController : Controller
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _config;
        private readonly ILogger<RoleController> _logger;

        public RoleController(
            IHttpClientFactory httpClientFactory,
            IConfiguration config,
            ILogger<RoleController> logger)
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

        private string CurrentUser =>
            HttpContext.Session.GetString("Email") ?? "Admin";

        private int CurrentRoleId =>
            HttpContext.Session.GetInt32("RoleID") ?? 0;

        /// <summary>
        /// Safely deserializes a JSON string into List<T>.
        /// Handles: plain array, wrapped object with data/result property.
        /// </summary>
        private List<T> SafeDeserializeList<T>(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return new List<T>();

            try
            {
                var token = JToken.Parse(json);

                // Case 1: Already a JSON array  → deserialize directly
                if (token.Type == JTokenType.Array)
                    return token.ToObject<List<T>>() ?? new List<T>();

                // Case 2: Wrapped object { "data": [...] }
                if (token["data"] != null)
                    return token["data"]!.ToObject<List<T>>() ?? new List<T>();

                // Case 3: Wrapped object { "result": [...] }
                if (token["result"] != null)
                    return token["result"]!.ToObject<List<T>>() ?? new List<T>();

                // Case 4: Single object → wrap in list
                var single = token.ToObject<T>();
                return single != null ? new List<T> { single } : new List<T>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SafeDeserializeList failed. JSON: {Json}", json);
                return new List<T>();
            }
        }

        // ─────────────────────────────────────────
        // GET /Role/List
        // ─────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> List()
        {
            if (CurrentRoleId != 1)
                return RedirectToAction("List", "Courses");

            try
            {
                using var client = CreateClient();
                var response = await client.GetAsync("api/role/list");
                var json = await response.Content.ReadAsStringAsync();

                _logger.LogInformation("api/role/list response: {Json}", json);

                var roles = SafeDeserializeList<RoleViewModel>(json);
                return View(roles);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading role list");
                TempData["Error"] = $"Error loading roles: {ex.Message}";
                return View(new List<RoleViewModel>());
            }
        }

        // ─────────────────────────────────────────
        // GET /Role/Create    → blank form
        // GET /Role/Create?id=5  → edit mode
        // ─────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> Create(int? id)
        {
            if (CurrentRoleId != 1)
                return RedirectToAction("List", "Courses");

            var vm = new RoleFormViewModel { IsActive = true };

            if (id.HasValue && id > 0)
            {
                try
                {
                    using var client = CreateClient();
                    var response = await client.GetAsync($"api/role/{id}");
                    if (response.IsSuccessStatusCode)
                    {
                        var json = await response.Content.ReadAsStringAsync();
                        var token = JToken.Parse(json);

                        // Handle wrapped response
                        var dataToken = token["data"] ?? token["result"] ?? token;
                        vm = dataToken.ToObject<RoleFormViewModel>() ?? vm;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error loading role for edit");
                    TempData["Error"] = "Could not load role data.";
                }
            }

            return View(vm);
        }

        // ─────────────────────────────────────────
        // POST /Role/Create
        // ─────────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(RoleFormViewModel model)
        {
            if (CurrentRoleId != 1)
                return RedirectToAction("List", "Courses");

            if (!ModelState.IsValid)
                return View(model);

            try
            {
                var payload = new
                {
                    RoleID = model.RoleID,
                    RoleCode = model.RoleCode?.Trim().ToUpper(),
                    RoleName = model.RoleName?.Trim(),
                    Description = model.Description?.Trim(),
                    IsActive = model.IsActive,
                    ActionBy = CurrentUser
                };

                using var client = CreateClient();
                var content = new StringContent(
                    JsonConvert.SerializeObject(payload),
                    Encoding.UTF8, "application/json");

                var response = await client.PostAsync("api/role/save", content);
                var json = await response.Content.ReadAsStringAsync();

                // Handle both flat and wrapped success responses
                var result = ParseApiResponse(json);

                if (response.IsSuccessStatusCode && result.Success)
                {
                    TempData["Success"] = result.Message ?? "Role saved successfully.";
                    return RedirectToAction("List");
                }

                TempData["Error"] = result.Message ?? "Save failed.";
                return View(model);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving role");
                TempData["Error"] = $"Error: {ex.Message}";
                return View(model);
            }
        }

        // ─────────────────────────────────────────
        // POST /Role/Delete  (AJAX)
        // ─────────────────────────────────────────
        [HttpPost]
        public async Task<IActionResult> Delete([FromBody] DeleteRoleRequest req)
        {
            if (CurrentRoleId != 1)
                return Json(new { success = false, message = "Unauthorized" });

            try
            {
                var payload = new { RoleID = req.RoleID, ActionBy = CurrentUser };
                using var client = CreateClient();
                var content = new StringContent(
                    JsonConvert.SerializeObject(payload),
                    Encoding.UTF8, "application/json");

                var response = await client.PostAsync("api/role/delete", content);
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
        // POST /Role/ToggleStatus  (AJAX)
        // ─────────────────────────────────────────
        [HttpPost]
        public async Task<IActionResult> ToggleStatus([FromBody] ToggleStatusRequest req)
        {
            if (CurrentRoleId != 1)
                return Json(new { success = false, message = "Unauthorized" });

            try
            {
                var payload = new
                {
                    RoleID = req.RoleID,
                    IsActive = req.IsActive,
                    ActionBy = CurrentUser
                };

                using var client = CreateClient();
                var content = new StringContent(
                    JsonConvert.SerializeObject(payload),
                    Encoding.UTF8, "application/json");

                var response = await client.PostAsync("api/role/toggle-status", content);
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
        // GET /Role/Permissions/{id}
        // ─────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> Permissions(int id)
        {
            if (CurrentRoleId != 1)
                return RedirectToAction("List", "Courses");

            try
            {
                using var client = CreateClient();

                // Load role
                var roleResp = await client.GetAsync($"api/role/{id}");
                if (!roleResp.IsSuccessStatusCode)
                {
                    TempData["Error"] = "Role not found.";
                    return RedirectToAction("List");
                }

                var roleJson = await roleResp.Content.ReadAsStringAsync();
                var roleToken = JToken.Parse(roleJson);
                var dataToken = roleToken["data"] ?? roleToken["result"] ?? roleToken;
                var role = dataToken.ToObject<RoleFormViewModel>();

                // Load permissions
                var permResp = await client.GetAsync($"api/role/{id}/permissions");
                var permJson = await permResp.Content.ReadAsStringAsync();
                var screens = SafeDeserializeList<ScreenPermissionViewModel>(permJson);

                var vm = new RolePermissionsViewModel
                {
                    RoleID = id,
                    RoleName = role?.RoleName ?? "",
                    RoleCode = role?.RoleCode ?? "",
                    Screens = screens
                };

                return View(vm);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading permissions");
                TempData["Error"] = $"Error: {ex.Message}";
                return RedirectToAction("List");
            }
        }

        // ─────────────────────────────────────────
        // POST /Role/SavePermissions  (AJAX)
        // ─────────────────────────────────────────
        [HttpPost]
        public async Task<IActionResult> SavePermissions([FromBody] SavePermissionsRequest req)
        {
            if (CurrentRoleId != 1)
                return Json(new { success = false, message = "Unauthorized" });

            try
            {
                var payload = new
                {
                    RoleID = req.RoleID,
                    ActionBy = CurrentUser,
                    Permissions = req.Permissions
                };

                using var client = CreateClient();
                var content = new StringContent(
                    JsonConvert.SerializeObject(payload),
                    Encoding.UTF8, "application/json");

                var response = await client.PostAsync("api/role/permissions/save", content);
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
        // GET /Role/AssignUsers/{id}
        // ─────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> AssignUsers(int id)
        {
            if (CurrentRoleId != 1)
                return RedirectToAction("List", "Courses");

            try
            {
                using var client = CreateClient();

                var roleResp = await client.GetAsync($"api/role/{id}");
                if (!roleResp.IsSuccessStatusCode)
                {
                    TempData["Error"] = "Role not found.";
                    return RedirectToAction("List");
                }

                var roleJson = await roleResp.Content.ReadAsStringAsync();
                var roleToken = JToken.Parse(roleJson);
                var dataToken = roleToken["data"] ?? roleToken["result"] ?? roleToken;
                var role = dataToken.ToObject<RoleFormViewModel>();

                var usersResp = await client.GetAsync($"api/role/{id}/users");
                var usersJson = await usersResp.Content.ReadAsStringAsync();
                var users = SafeDeserializeList<RoleUserViewModel>(usersJson);

                var allUsersResp = await client.GetAsync("api/employee/list");
                var allUsersJson = await allUsersResp.Content.ReadAsStringAsync();
                var allUsers = SafeDeserializeList<EmployeeDto>(allUsersJson);

                var vm = new RoleAssignUsersViewModel
                {
                    RoleID = id,
                    RoleName = role?.RoleName ?? "",
                    RoleCode = role?.RoleCode ?? "",
                    AssignedUsers = users,
                    AvailableUsers = allUsers
                };

                return View(vm);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading role users");
                TempData["Error"] = $"Error: {ex.Message}";
                return RedirectToAction("List");
            }
        }

        // ─────────────────────────────────────────
        // POST /Role/AssignUser  (AJAX)
        // ─────────────────────────────────────────
        [HttpPost]
        public async Task<IActionResult> AssignUser([FromBody] AssignUserRequest req)
        {
            if (CurrentRoleId != 1)
                return Json(new { success = false, message = "Unauthorized" });

            try
            {
                var payload = new { RoleID = req.RoleID, UserID = req.UserID, ActionBy = CurrentUser };
                using var client = CreateClient();
                var content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
                var response = await client.PostAsync("api/role/assign-user", content);
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
        // POST /Role/RemoveUser  (AJAX)
        // ─────────────────────────────────────────
        [HttpPost]
        public async Task<IActionResult> RemoveUser([FromBody] AssignUserRequest req)
        {
            if (CurrentRoleId != 1)
                return Json(new { success = false, message = "Unauthorized" });

            try
            {
                var payload = new { RoleID = req.RoleID, UserID = req.UserID, ActionBy = CurrentUser };
                using var client = CreateClient();
                var content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
                var response = await client.PostAsync("api/role/remove-user", content);
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
        // PRIVATE: Parse any API response shape
        // ─────────────────────────────────────────
        private (bool Success, string Message) ParseApiResponse(string json)
        {
            try
            {
                var token = JToken.Parse(json);

                // Try standard shape: { success: bool, message: "..." }
                bool success = token["success"]?.Value<bool>()
                            ?? token["Success"]?.Value<bool>()
                            ?? false;

                string message = token["message"]?.Value<string>()
                              ?? token["Message"]?.Value<string>()
                              ?? (success ? "Operation successful." : "Operation failed.");

                return (success, message);
            }
            catch
            {
                return (false, "Unexpected API response.");
            }
        }
    }

    // ══════════════════════════════════════════════
    // VIEW MODELS
    // ══════════════════════════════════════════════

    public class RoleViewModel
    {
        public int RoleID { get; set; }
        public string RoleCode { get; set; }
        public string RoleName { get; set; }
        public string Description { get; set; }
        public bool IsActive { get; set; }
        public bool IsSystem { get; set; }
        public string CreatedBy { get; set; }
        public string CreatedDate { get; set; }
        public int UserCount { get; set; }
        public int PermissionCount { get; set; }
    }

    public class RoleFormViewModel
    {
        public int RoleID { get; set; }

        [Required(ErrorMessage = "Role Code is required")]
        [StringLength(20, ErrorMessage = "Max 20 characters")]
        public string RoleCode { get; set; }

        [Required(ErrorMessage = "Role Name is required")]
        [StringLength(100, ErrorMessage = "Max 100 characters")]
        public string RoleName { get; set; }

        [StringLength(500, ErrorMessage = "Max 500 characters")]
        public string Description { get; set; }

        public bool IsActive { get; set; } = true;
        public bool IsSystem { get; set; }
    }

    public class ScreenPermissionViewModel
    {
        public int ScreenID { get; set; }
        public string ScreenCode { get; set; }
        public string ScreenName { get; set; }
        public string ScreenGroup { get; set; }
        public int SortOrder { get; set; }
        public bool CanView { get; set; }
        public bool CanCreate { get; set; }
        public bool CanEdit { get; set; }
        public bool CanDelete { get; set; }
    }

    public class RolePermissionsViewModel
    {
        public int RoleID { get; set; }
        public string RoleName { get; set; }
        public string RoleCode { get; set; }
        public List<ScreenPermissionViewModel> Screens { get; set; } = new();
    }

    public class RoleUserViewModel
    {
        public int UserID { get; set; }
        public string Name { get; set; }
        public string Email { get; set; }
        public string MobileNumber { get; set; }
        public string Gender { get; set; }
        public bool IsActive { get; set; }
        public string AssignedDate { get; set; }
    }

    public class RoleAssignUsersViewModel
    {
        public int RoleID { get; set; }
        public string RoleName { get; set; }
        public string RoleCode { get; set; }
        public List<RoleUserViewModel> AssignedUsers { get; set; } = new();
        public List<EmployeeDto> AvailableUsers { get; set; } = new();
    }

    // ── Request Models ────────────────────────────
    public class ToggleStatusRequest
    {
        public int RoleID { get; set; }
        public bool IsActive { get; set; }
    }

    public class DeleteRoleRequest
    {
        public int RoleID { get; set; }
    }

    public class AssignUserRequest
    {
        public int RoleID { get; set; }
        public int UserID { get; set; }
    }

    public class SavePermissionsRequest
    {
        public int RoleID { get; set; }
        public List<PermissionItemRequest> Permissions { get; set; } = new();
    }

    public class PermissionItemRequest
    {
        public int ScreenID { get; set; }
        public bool CanView { get; set; }
        public bool CanCreate { get; set; }
        public bool CanEdit { get; set; }
        public bool CanDelete { get; set; }
    }
}
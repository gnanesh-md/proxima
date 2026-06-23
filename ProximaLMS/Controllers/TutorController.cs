using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using System.Net.Http;

namespace ProximaLMS.Controllers
{
    public class TutorController : Controller
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _config;
        private readonly ILogger<TutorController> _logger;

        public TutorController(
            IHttpClientFactory httpClientFactory,
            IConfiguration config,
            ILogger<TutorController> logger)
        {
            _httpClientFactory = httpClientFactory;
            _config = config;
            _logger = logger;
        }

        [HttpGet]
        public IActionResult Create()
        {
            return View(new TutorViewModel());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(TutorViewModel model)
        {
            // 🔥 DEBUGGING: Log ModelState errors
            if (!ModelState.IsValid)
            {
                var errors = ModelState.Values
                    .SelectMany(v => v.Errors)
                    .Select(e => e.ErrorMessage)
                    .ToList();

                _logger.LogWarning("Model validation failed: {Errors}", string.Join(", ", errors));

                TempData["Error"] = "Please fix validation errors: " + string.Join(", ", errors);
                return View(model);
            }

            var client = _httpClientFactory.CreateClient();
            var apiBaseUrl = _config["ApiBaseUrl"];

            if (string.IsNullOrEmpty(apiBaseUrl))
            {
                TempData["Error"] = "API Base URL is not configured in appsettings.json";
                return View(model);
            }

            client.BaseAddress = new Uri(apiBaseUrl);

            try
            {
                // ===== FILE UPLOAD HANDLING =====
                string uploadPath = Path.Combine(
                    Directory.GetCurrentDirectory(),
                    "wwwroot", "uploads", "tutors");

                // Ensure directory exists
                if (!Directory.Exists(uploadPath))
                {
                    Directory.CreateDirectory(uploadPath);
                    _logger.LogInformation("Created upload directory: {Path}", uploadPath);
                }

                // ===== PROFILE PHOTO =====
                if (model.ProfilePhotoFile != null && model.ProfilePhotoFile.Length > 0)
                {
                    var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif" };
                    var extension = Path.GetExtension(model.ProfilePhotoFile.FileName).ToLowerInvariant();

                    if (!allowedExtensions.Contains(extension))
                    {
                        TempData["Error"] = "Invalid profile photo format. Only JPG, PNG, GIF allowed.";
                        return View(model);
                    }

                    var fileName = $"{Guid.NewGuid()}{extension}";
                    var fullPath = Path.Combine(uploadPath, fileName);

                    using (var stream = new FileStream(fullPath, FileMode.Create))
                    {
                        await model.ProfilePhotoFile.CopyToAsync(stream);
                    }

                    model.ProfilePhoto = fileName;
                    _logger.LogInformation("Profile photo saved: {FileName}", fileName);
                }

                // ===== RESUME FILE =====
                if (model.ResumeFileUpload != null && model.ResumeFileUpload.Length > 0)
                {
                    var allowedExtensions = new[] { ".pdf", ".doc", ".docx" };
                    var extension = Path.GetExtension(model.ResumeFileUpload.FileName).ToLowerInvariant();

                    if (!allowedExtensions.Contains(extension))
                    {
                        TempData["Error"] = "Invalid resume format. Only PDF, DOC, DOCX allowed.";
                        return View(model);
                    }

                    var fileName = $"{Guid.NewGuid()}{extension}";
                    var fullPath = Path.Combine(uploadPath, fileName);

                    using (var stream = new FileStream(fullPath, FileMode.Create))
                    {
                        await model.ResumeFileUpload.CopyToAsync(stream);
                    }

                    model.ResumeFile = fileName;
                    _logger.LogInformation("Resume saved: {FileName}", fileName);
                }

                // ===== GENERATE TUTOR CODE IF NULL =====
                if (string.IsNullOrEmpty(model.TutorCode))
                {
                    model.TutorCode = $"TUT-{DateTime.Now:yyyyMMddHHmmss}";
                }

                // ===== CREATE DTO FOR API =====
                var dto = new
                {
                    TutorID = model.TutorID ?? 0,
                    TutorCode = model.TutorCode,
                    FullName = model.FullName?.Trim(),
                    Gender = model.Gender,
                    DateOfBirth = model.DateOfBirth,
                    Email = model.Email?.Trim(),
                    MobileNumber = model.MobileNumber?.Trim(),
                    AlternateMobile = model.AlternateMobile?.Trim(),
                    Qualification = model.Qualification?.Trim(),
                    ExperienceYears = model.ExperienceYears ?? 0,
                    ExpertiseAreas = model.ExpertiseAreas?.Trim(),
                    ProfileSummary = model.ProfileSummary?.Trim(),
                    ProfilePhoto = model.ProfilePhoto,
                    ResumeFile = model.ResumeFile,
                    AddressLine1 = model.AddressLine1?.Trim(),
                    AddressLine2 = model.AddressLine2?.Trim(),
                    City = model.City?.Trim(),
                    State = model.State?.Trim(),
                    Country = model.Country?.Trim(),
                    Pincode = model.Pincode?.Trim(),
                    BankAccountNumber = model.BankAccountNumber?.Trim(),
                    IFSCCode = model.IFSCCode?.Trim(),
                    BankName = model.BankName?.Trim(),
                    UPIID = model.UPIID?.Trim(),
                    LoginUserID = 1,
                    IsActive = true,
                    CreatedBy = User.Identity?.Name ?? "Admin"
                };

                // ===== SERIALIZE AND LOG =====
                var json = JsonConvert.SerializeObject(dto, Formatting.Indented);
                _logger.LogInformation("Sending tutor data to API: {Json}", json);

                var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

                // ===== CALL API =====
                _logger.LogInformation("Calling API endpoint: {Endpoint}", "api/tutor/save");
                var response = await client.PostAsync("api/tutor/save", content);

                // ===== HANDLE RESPONSE =====
                var responseContent = await response.Content.ReadAsStringAsync();
                _logger.LogInformation("API Response Status: {Status}, Content: {Content}",
                    response.StatusCode, responseContent);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("API call failed. Status: {Status}, Response: {Response}",
                        response.StatusCode, responseContent);

                    TempData["Error"] = $"Failed to save tutor. Status: {response.StatusCode}. Error: {responseContent}";
                    return View(model);
                }

                // ===== SUCCESS =====
                _logger.LogInformation("Tutor saved successfully");
                TempData["Success"] = "Tutor saved successfully";
                return RedirectToAction("List");
            }
            catch (HttpRequestException httpEx)
            {
                _logger.LogError(httpEx, "HTTP Request error while saving tutor");
                TempData["Error"] = $"Connection error: {httpEx.Message}. Please check if API is running.";
                return View(model);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error while saving tutor");
                TempData["Error"] = $"Error: {ex.Message}";
                return View(model);
            }
        }

        [HttpGet]
        public async Task<IActionResult> List()
        {
            var client = _httpClientFactory.CreateClient();
            var apiBaseUrl = _config["ApiBaseUrl"];

            if (string.IsNullOrEmpty(apiBaseUrl))
            {
                TempData["Error"] = "API Base URL is not configured";
                return View(new List<TutorViewModel>());
            }

            client.BaseAddress = new Uri(apiBaseUrl);

            try
            {
                _logger.LogInformation("Fetching tutor list from API");
                var response = await client.GetAsync("api/tutor/list");

                if (!response.IsSuccessStatusCode)
                {
                    var error = await response.Content.ReadAsStringAsync();
                    _logger.LogError("Failed to fetch tutors. Status: {Status}, Error: {Error}",
                        response.StatusCode, error);

                    TempData["Error"] = "Failed to load tutors";
                    return View(new List<TutorViewModel>());
                }

                var json = await response.Content.ReadAsStringAsync();
                var data = JsonConvert.DeserializeObject<List<TutorViewModel>>(json)
                           ?? new List<TutorViewModel>();

                _logger.LogInformation("Loaded {Count} tutors", data.Count);
                return View(data);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading tutor list");
                TempData["Error"] = $"Error loading tutors: {ex.Message}";
                return View(new List<TutorViewModel>());
            }
        }

        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            var client = _httpClientFactory.CreateClient();
            var apiBaseUrl = _config["ApiBaseUrl"];

            if (string.IsNullOrEmpty(apiBaseUrl))
            {
                TempData["Error"] = "API Base URL is not configured";
                return RedirectToAction("List");
            }

            client.BaseAddress = new Uri(apiBaseUrl);

            try
            {
                _logger.LogInformation("Fetching tutor with ID: {Id}", id);
                var response = await client.GetAsync($"api/tutor/{id}");

                if (!response.IsSuccessStatusCode)
                {
                    var error = await response.Content.ReadAsStringAsync();
                    _logger.LogError("Failed to fetch tutor. Status: {Status}, Error: {Error}",
                        response.StatusCode, error);

                    TempData["Error"] = "Tutor not found";
                    return RedirectToAction("List");
                }

                var json = await response.Content.ReadAsStringAsync();
                var model = JsonConvert.DeserializeObject<TutorViewModel>(json);

                if (model == null)
                {
                    TempData["Error"] = "Invalid tutor data";
                    return RedirectToAction("List");
                }

                return View("Create", model);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading tutor for edit");
                TempData["Error"] = $"Error: {ex.Message}";
                return RedirectToAction("List");
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleStatus([FromBody] ToggleStatusModel model)
        {
            if (model == null)
                return BadRequest("Invalid data");

            var apiBaseUrl = _config["ApiBaseUrl"];
            if (string.IsNullOrEmpty(apiBaseUrl))
                return BadRequest("API not configured");

            var client = _httpClientFactory.CreateClient();
            client.BaseAddress = new Uri(apiBaseUrl);

            var json = JsonConvert.SerializeObject(model);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

            var response = await client.PostAsync("api/tutor/toggle-status", content);

            if (response.IsSuccessStatusCode)
                return Json(new { success = true });

            return StatusCode((int)response.StatusCode);
        }

    }
}
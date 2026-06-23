using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Newtonsoft.Json;
using ProximaLMS.Filters;
using ProximaLMS.Models;
using Newtonsoft.Json.Linq;
using System.Text;

namespace ProximaLMS.Controllers
{
    [RequestSizeLimit(5L * 1024L * 1024L * 1024L)]  // 5 GB
    [RequestFormLimits(MultipartBodyLengthLimit = 5L * 1024L * 1024L * 1024L)]
    [RequireJwt]
    public class CoursesController : Controller
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _config;
        private readonly ILogger<CoursesController> _logger;

        public CoursesController(IHttpClientFactory httpClientFactory, IConfiguration config, ILogger<CoursesController> logger)
        {
            _httpClientFactory = httpClientFactory;
            _config = config;
            _logger = logger;
        }

        // Resolve the logged-in user to their tutor profile id (0 if not a tutor).
        // Used to auto-assign a tutor's own courses to themselves on create/edit.
        private async Task<int> ResolveCurrentTutorIdAsync()
        {
            var userId = HttpContext.Session.GetString("UserID");
            if (!int.TryParse(userId, out var uid) || uid <= 0) return 0;
            try
            {
                var token = HttpContext.Session.GetString("JwtToken");
                var client = _httpClientFactory.CreateClient();
                client.BaseAddress = new Uri(_config["ApiBaseUrl"]);
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

                var resp = await client.GetAsync($"api/instructor/resolve/{uid}");
                if (!resp.IsSuccessStatusCode) return 0;
                var json = await resp.Content.ReadAsStringAsync();
                var t = JsonConvert.DeserializeObject<TutorResolveDto>(json);
                return t?.TutorID ?? 0;
            }
            catch { return 0; }
        }

        private class TutorResolveDto { public int TutorID { get; set; } }

        // ─────────────────────────────────────────────────────────
        //  Dropdown builder — pulls every master list from the API
        //  (Categories, Tutors, Tags). Levels & Languages stay
        //  hardcoded for now.
        // ─────────────────────────────────────────────────────────
        private async Task<CreateCourseViewModel> BuildDropdownsAsync()
        {
            var vm = new CreateCourseViewModel
            {
                CourseCode = $"CRS-{DateTime.UtcNow:yyyyMMddHHmmss}"
            };

            var token = HttpContext.Session.GetString("JwtToken");
            var client = _httpClientFactory.CreateClient();
            client.BaseAddress = new Uri(_config["ApiBaseUrl"]);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            // ── Tutors ──
            try
            {
                var tutorResponse = await client.GetAsync("api/course/tutors");
                var tutorList = new List<SelectListItem> { new SelectListItem("Select Tutor", "0") };

                if (tutorResponse.IsSuccessStatusCode)
                {
                    var json = await tutorResponse.Content.ReadAsStringAsync();
                    var tutors = JsonConvert.DeserializeObject<List<dynamic>>(json);
                    foreach (var t in tutors)
                        tutorList.Add(new SelectListItem((string)t.tutorName, t.tutorID.ToString()));
                }
                else
                {
                    tutorList.Add(new SelectListItem("⚠️ Failed to load tutors", "0"));
                }
                vm.Instructors = tutorList;
            }
            catch (Exception ex)
            {
                vm.Instructors = new List<SelectListItem>
                {
                    new SelectListItem($"-- Error loading tutors ({ex.Message}) --", "0")
                };
            }

            // ── Categories ──
            try
            {
                var catResp = await client.GetAsync("api/Category/active");
                var catList = new List<SelectListItem> { new SelectListItem("Select Category", "0") };

                if (catResp.IsSuccessStatusCode)
                {
                    var json = await catResp.Content.ReadAsStringAsync();
                    // The API returns PascalCase keys (CategoryID/CategoryName).
                    // Deserialize to a typed model so Newtonsoft's case-insensitive
                    // matching binds regardless of casing — the old dynamic read used
                    // camelCase (c.categoryID) and silently threw, leaving the
                    // dropdown empty.
                    var cats = JsonConvert.DeserializeObject<List<CategoryLookup>>(json)
                               ?? new List<CategoryLookup>();
                    foreach (var c in cats)
                        if (!string.IsNullOrWhiteSpace(c.CategoryName))
                            catList.Add(new SelectListItem(c.CategoryName, c.CategoryID.ToString()));
                }
                vm.Categories = catList;
            }
            catch
            {
                vm.Categories = new List<SelectListItem> { new SelectListItem("Select Category", "0") };
            }

            // ── Tags ──
            try
            {
                var tagResp = await client.GetAsync("api/Tag/active");
                if (tagResp.IsSuccessStatusCode)
                {
                    var json = await tagResp.Content.ReadAsStringAsync();
                    var tags = JsonConvert.DeserializeObject<List<dynamic>>(json);
                    foreach (var t in tags)
                    {
                        vm.AllTags.Add(new TagOption
                        {
                            TagId = (int)t.tagID,
                            TagName = (string)t.tagName,
                            ColorHex = (t.colorHex != null) ? (string)t.colorHex : "#7B2CBF"
                        });
                    }
                }
            }
            catch { /* non-fatal: empty tag list */ }

            // ── Skill Levels (from the Skill Levels master) ──
            // Was previously hardcoded (Beginner=1/Intermediate=2/Advanced=3),
            // which drifted from TblCourseLevel.LevelID and broke the level-name
            // join on the course list. Now sourced live from the master.
            var levelList = new List<SelectListItem> { new SelectListItem("Select Difficulty", "0") };
            try
            {
                var lvlResp = await client.GetAsync("api/SkillLevel/active");
                if (lvlResp.IsSuccessStatusCode)
                {
                    var json = await lvlResp.Content.ReadAsStringAsync();
                    foreach (var row in JArray.Parse(json))
                    {
                        // case-insensitive: works whether API emits camel or Pascal case
                        var id = (row["LevelID"] ?? row["levelID"] ?? row["levelId"])?.ToString();
                        var name = (row["LevelName"] ?? row["levelName"])?.ToString();
                        if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(name))
                            levelList.Add(new SelectListItem(name, id));
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed loading skill levels for course form");
            }
            vm.Levels = levelList;

            // ── Languages (from the Languages master) ──
            var langList = new List<SelectListItem> { new SelectListItem("Select Language", "0") };
            try
            {
                var langResp = await client.GetAsync("api/Language/list");
                if (langResp.IsSuccessStatusCode)
                {
                    var json = await langResp.Content.ReadAsStringAsync();
                    var langToken = JToken.Parse(json);
                    // /api/Language/list returns { success, data:[...] }; tolerate a bare array too
                    var arr = langToken["data"] as JArray ?? langToken as JArray;
                    if (arr != null)
                    {
                        foreach (var row in arr)
                        {
                            var id = (row["LanguageID"] ?? row["languageID"] ?? row["languageId"])?.ToString();
                            var name = (row["LanguageName"] ?? row["languageName"])?.ToString();
                            if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(name))
                                langList.Add(new SelectListItem(name, id));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed loading languages for course form");
            }
            vm.Languages = langList;

            return vm;
        }


        [HttpGet]
        public async Task<IActionResult> Create()
        {
            var token = HttpContext.Session.GetString("JwtToken");
            var userId = HttpContext.Session.GetString("UserID");

            if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(userId))
                return RedirectToAction("Index", "Home");

            var vm = await BuildDropdownsAsync();

            // A tutor authors only their own courses — pre-select & lock the instructor.
            var roleId = HttpContext.Session.GetInt32("RoleID") ?? 0;
            if (roleId != 1)
            {
                var myTutorId = await ResolveCurrentTutorIdAsync();
                if (myTutorId > 0)
                {
                    vm.InstructorId = myTutorId;
                    ViewBag.LockInstructor = true;
                }
            }

            return View(vm);
        }


        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            var token = HttpContext.Session.GetString("JwtToken");
            if (string.IsNullOrEmpty(token))
                return RedirectToAction("Index", "Home");

            var client = _httpClientFactory.CreateClient();
            client.BaseAddress = new Uri(_config["ApiBaseUrl"]);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            try
            {
                _logger.LogInformation("Fetching course for edit: CourseID={CourseID}", id);

                var response = await client.GetAsync($"api/course/edit/{id}");
                var json = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("Failed to load course. Status: {Status}, Response: {Response}",
                        response.StatusCode, json);
                    TempData["Error"] = "Failed to load course for editing";
                    return RedirectToAction("List");
                }

                var data = JsonConvert.DeserializeObject<CourseApiResponse>(json);

                var course = data.Course;
                var contents = data.Contents;

                var vm = await BuildDropdownsAsync();
                vm.CourseID = id;
                vm.CourseCode = $"CRS-{id}";
                vm.CourseTitle = course.CourseTitle?.ToString() ?? "";

                if (!string.IsNullOrEmpty(course.Instructor) &&
                    int.TryParse(course.Instructor, out int instructorId))
                    vm.InstructorId = instructorId;

                if (!string.IsNullOrEmpty(course.CourseLevel) &&
                    int.TryParse(course.CourseLevel, out int levelId))
                    vm.CourseLevel = levelId;

                if (!string.IsNullOrEmpty(course.Language) &&
                    int.TryParse(course.Language, out int languageId))
                    vm.LanguageId = languageId;

                // Restore saved category
                if (!string.IsNullOrEmpty(course.Category) &&
                    int.TryParse(course.Category, out int categoryId))
                    vm.CategoryId = categoryId;

                // Restore saved tag IDs (try the for-course endpoint, falls back to embedded ids)
                try
                {
                    var tagResp = await client.GetAsync($"api/Tag/for-course/{id}");
                    if (tagResp.IsSuccessStatusCode)
                    {
                        var tjson = await tagResp.Content.ReadAsStringAsync();
                        var rows = JsonConvert.DeserializeObject<List<dynamic>>(tjson);
                        if (rows != null)
                            vm.SelectedTagIds = rows.Select(r => (int)r.tagID).ToList();
                    }
                    else
                    {
                        var raw = JToken.Parse(json);
                        var tagsToken = raw["tagIds"] ?? raw["TagIds"] ?? raw["selectedTagIds"];
                        if (tagsToken is JArray tarr)
                            foreach (var t in tarr)
                                if (int.TryParse(t.ToString(), out var tid))
                                    vm.SelectedTagIds.Add(tid);
                    }
                }
                catch (Exception tex)
                {
                    _logger.LogError(tex, "Failed loading course tags");
                }

                vm.CourseLevelName = course.CourseLevelName?.ToString() ?? "";
                vm.TutorName = course.TutorName?.ToString() ?? "";

                if (course.StartDate != null &&
                    DateTime.TryParse(course.StartDate.ToString(), out DateTime startDate))
                    vm.CourseStartDate = startDate;

                if (course.EndDate != null &&
                    DateTime.TryParse(course.EndDate.ToString(), out DateTime endDate))
                    vm.CourseEndDate = endDate;

                string priceStr = Convert.ToString(course.Price);
                if (!string.IsNullOrEmpty(priceStr) &&
                    decimal.TryParse(priceStr, out decimal price))
                    vm.Price = price;

                string enrollStr = Convert.ToString(course.EnrollmentLimit);
                if (!string.IsNullOrEmpty(enrollStr) &&
                    int.TryParse(enrollStr, out int enrollLimit))
                    vm.EnrollmentLimit = enrollLimit;

                string durationStr = Convert.ToString(course.DurationHrs);
                if (!string.IsNullOrEmpty(durationStr) &&
                    decimal.TryParse(durationStr, out decimal duration))
                    vm.CourseDurationHours = (int)duration;

                vm.SmallDescription = course.OneLineDescription?.ToString() ?? "";
                vm.ExistingLogoFileName = course.CourseLogo?.ToString() ?? "";
                vm.ExistingCoverFileName = course.CoverImage?.ToString() ?? "";
                vm.ExistingPromoFileName = course.PromoVideo?.ToString() ?? "";

                vm.Videos = new List<CourseVideoRow>();
                vm.ExistingContents = new List<ExistingContentViewModel>();

                if (contents != null)
                {
                    foreach (var content in contents)
                    {
                        var existingContent = new ExistingContentViewModel
                        {
                            ContentID = Convert.ToInt32(content.ContentID),
                            ContentTitle = content.ContentTitle?.ToString() ?? "",
                            Description = content.Description?.ToString() ?? "",
                            VideoThumbnail = content.VideoThumbnail?.ToString() ?? "",
                            ContentFile = content.ContentFile?.ToString() ?? "",
                            SortOrder = content.SortOrder != null ? Convert.ToInt32(content.SortOrder) : 0,
                            FileType = SafeFileType(content)
                        };

                        vm.ExistingContents.Add(existingContent);
                    }
                }

                ViewBag.IsEditMode = true;
                ViewBag.CourseID = id;

                return View("Create", vm);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading course for edit");
                TempData["Error"] = "Error: " + ex.Message;
                return RedirectToAction("List");
            }
        }

        // Tiny helper — dynamic objects from the API may not always have FileType
        private static string SafeFileType(dynamic content)
        {
            try
            {
                var raw = content.FileType?.ToString();
                if (string.IsNullOrWhiteSpace(raw)) return "Video";
                return raw;
            }
            catch { return "Video"; }
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(CreateCourseViewModel vm)
        {
            int editCourseId = vm.CourseID ?? 0;
            bool isEdit = editCourseId > 0;

            _logger.LogInformation("Saving course. IsEdit: {IsEdit}, CourseID: {CourseID}", isEdit, editCourseId);

            // A tutor's course is always authored under their own profile,
            // regardless of what the (possibly hidden) instructor field posted.
            var roleIdPost = HttpContext.Session.GetInt32("RoleID") ?? 0;
            if (roleIdPost != 1)
            {
                var myTutorId = await ResolveCurrentTutorIdAsync();
                if (myTutorId > 0) vm.InstructorId = myTutorId;
            }

            if (!isEdit)
            {
                if (vm.CourseLogo == null)
                    ModelState.AddModelError("CourseLogo", "Course Logo is required.");

                if (vm.CoverImage == null)
                    ModelState.AddModelError("CoverImage", "Cover Image is required.");

                if (vm.PromotionalVideo == null)
                    ModelState.AddModelError("PromotionalVideo", "Promotional Video is required.");
            }

            if (!ModelState.IsValid)
            {
                var errors = string.Join("; ", ModelState.Values
                    .SelectMany(v => v.Errors)
                    .Select(e => e.ErrorMessage));

                TempData["Error"] = "Validation failed: " + errors;
                await RefreshDropdownsAsync(vm);
                return View(vm);
            }

            var token = HttpContext.Session.GetString("JwtToken");
            if (string.IsNullOrEmpty(token))
                return RedirectToAction("Index", "Home");

            var client = _httpClientFactory.CreateClient();
            client.BaseAddress = new Uri(_config["ApiBaseUrl"]);
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", token);

            try
            {
                string uploadDir = Path.Combine(
                    Directory.GetCurrentDirectory(), "wwwroot", "uploads");
                Directory.CreateDirectory(uploadDir);

                string logoFileName = vm.ExistingLogoFileName ?? "";
                string coverFileName = vm.ExistingCoverFileName ?? "";
                string promoFileName = vm.ExistingPromoFileName ?? "";

                if (vm.CourseLogo != null && vm.CourseLogo.Length > 0)
                {
                    logoFileName = Guid.NewGuid() + Path.GetExtension(vm.CourseLogo.FileName);
                    using var stream = new FileStream(Path.Combine(uploadDir, logoFileName), FileMode.Create);
                    await vm.CourseLogo.CopyToAsync(stream);
                }

                if (vm.CoverImage != null && vm.CoverImage.Length > 0)
                {
                    coverFileName = Guid.NewGuid() + Path.GetExtension(vm.CoverImage.FileName);
                    using var stream = new FileStream(Path.Combine(uploadDir, coverFileName), FileMode.Create);
                    await vm.CoverImage.CopyToAsync(stream);
                }

                if (vm.PromotionalVideo != null && vm.PromotionalVideo.Length > 0)
                {
                    promoFileName = Guid.NewGuid() + Path.GetExtension(vm.PromotionalVideo.FileName);
                    using var stream = new FileStream(Path.Combine(uploadDir, promoFileName), FileMode.Create);
                    await vm.PromotionalVideo.CopyToAsync(stream);
                }

                var payload = new
                {
                    CourseID = editCourseId,
                    CourseTitle = vm.CourseTitle,
                    CourseLevel = vm.CourseLevel.ToString(),
                    Language = vm.LanguageId.ToString(),
                    Instructor = vm.InstructorId.ToString(),
                    Category = vm.CategoryId.ToString(),
                    CourseLogo = logoFileName,
                    CoverImage = coverFileName,
                    PromoVideo = promoFileName,
                    StartDate = vm.CourseStartDate ?? DateTime.UtcNow,
                    EndDate = vm.CourseEndDate ?? DateTime.UtcNow.AddDays(30),
                    Price = vm.Price ?? 0,
                    EnrollmentLimit = vm.EnrollmentLimit ?? 0,
                    DurationHrs = vm.CourseDurationHours ?? 0,
                    OneLineDescription = vm.SmallDescription ?? "",
                    CreatedBy = User.Identity?.Name ?? "Admin"
                };

                var json = JsonConvert.SerializeObject(payload);
                var httpContent = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await client.PostAsync("api/course/savecourse", httpContent);
                var responseBody = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    TempData["Error"] = "Course save failed: " + responseBody;
                    await RefreshDropdownsAsync(vm);
                    return View(vm);
                }

                var resultObj = JObject.Parse(responseBody);
                int resultCourseId = 0;

                if (resultObj["CourseID"] != null)
                    resultCourseId = resultObj["CourseID"].Value<int>();
                else if (resultObj["courseID"] != null)
                    resultCourseId = resultObj["courseID"].Value<int>();
                else if (resultObj["courseId"] != null)
                    resultCourseId = resultObj["courseId"].Value<int>();

                if (resultCourseId <= 0)
                {
                    TempData["Error"] = "Invalid CourseID returned.";
                    await RefreshDropdownsAsync(vm);
                    return View(vm);
                }

                // ── content rows (lesson-type aware) ──
                if (vm.Videos != null)
                {
                    int sortOrder = 1;

                    foreach (var item in vm.Videos)
                    {
                        var type = (item.LessonType ?? "Video").Trim();

                        bool hasMedia = item.CourseVideo != null
                                        || item.LessonFile != null
                                        || !string.IsNullOrWhiteSpace(item.TextBody);

                        if (string.IsNullOrWhiteSpace(item.ContentTitle) && !hasMedia)
                            continue;

                        string courseFolder = Path.Combine(
                            Directory.GetCurrentDirectory(), "wwwroot", "Coursecontent", $"Course_{resultCourseId}");
                        Directory.CreateDirectory(courseFolder);

                        string thumbFileName = "";
                        string contentFileName = "";
                        string fileType = "Video";
                        string description = item.Description ?? "";

                        if (type.Equals("PDF", StringComparison.OrdinalIgnoreCase))
                        {
                            fileType = "PDF";
                            if (item.LessonFile != null && item.LessonFile.Length > 0)
                            {
                                contentFileName = Guid.NewGuid() + Path.GetExtension(item.LessonFile.FileName);
                                using var s = new FileStream(Path.Combine(courseFolder, contentFileName), FileMode.Create);
                                await item.LessonFile.CopyToAsync(s);
                            }
                            else continue;
                        }
                        else if (type.Equals("Text", StringComparison.OrdinalIgnoreCase))
                        {
                            fileType = "Text";
                            if (!string.IsNullOrWhiteSpace(item.TextBody))
                                description = item.TextBody;
                            else if (string.IsNullOrWhiteSpace(description))
                                continue;
                        }
                        else
                        {
                            fileType = "Video";
                            if (item.VideoThumbnail != null && item.VideoThumbnail.Length > 0)
                            {
                                thumbFileName = Guid.NewGuid() + Path.GetExtension(item.VideoThumbnail.FileName);
                                using var s = new FileStream(Path.Combine(courseFolder, thumbFileName), FileMode.Create);
                                await item.VideoThumbnail.CopyToAsync(s);
                            }
                            if (item.CourseVideo != null && item.CourseVideo.Length > 0)
                            {
                                contentFileName = Guid.NewGuid() + Path.GetExtension(item.CourseVideo.FileName);
                                using var s = new FileStream(Path.Combine(courseFolder, contentFileName), FileMode.Create);
                                await item.CourseVideo.CopyToAsync(s);
                            }
                            else continue;
                        }

                        var contentPayload = new
                        {
                            ContentID = 0,
                            CourseID = resultCourseId,
                            ContentTitle = item.ContentTitle,
                            Description = description,
                            FileType = fileType,
                            VideoThumbnail = thumbFileName,
                            ContentFile = contentFileName,
                            SortOrder = sortOrder++,
                            CreatedBy = User.Identity?.Name ?? "Admin"
                        };

                        var jsonContent = JsonConvert.SerializeObject(contentPayload);
                        var contentHttp = new StringContent(jsonContent, Encoding.UTF8, "application/json");
                        await client.PostAsync("api/course/save-content", contentHttp);
                    }
                }

                // ── persist tag selections ──
                await SetCourseTagsAsync(client, resultCourseId, vm.SelectedTagIds);

                TempData["Success"] = isEdit
                    ? "Course updated successfully!"
                    : "Course created successfully!";

                return RedirectToAction("List");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving course");
                TempData["Error"] = ex.Message;
                await RefreshDropdownsAsync(vm);
                return View(vm);
            }
        }

        // helper — re-fetch dropdowns onto an existing vm (preserves user input)
        private async Task RefreshDropdownsAsync(CreateCourseViewModel vm)
        {
            var fresh = await BuildDropdownsAsync();
            vm.Levels = fresh.Levels;
            vm.Languages = fresh.Languages;
            vm.Instructors = fresh.Instructors;
            vm.Categories = fresh.Categories;
            vm.AllTags = fresh.AllTags;
            if (string.IsNullOrEmpty(vm.CourseCode)) vm.CourseCode = fresh.CourseCode;
        }

        private async Task SetCourseTagsAsync(HttpClient client, int courseId, List<int> tagIds)
        {
            try
            {
                var payload = new { CourseID = courseId, TagIDs = tagIds ?? new List<int>() };
                var http = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
                await client.PostAsync("api/Tag/set-for-course", http);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to set tags for course {Course}", courseId);
            }
        }

        [HttpPost]
        public async Task<IActionResult> DeleteContent(int contentId)
        {
            var token = HttpContext.Session.GetString("JwtToken");
            if (string.IsNullOrEmpty(token))
                return Json(new { success = false, message = "Unauthorized" });

            var client = _httpClientFactory.CreateClient();
            client.BaseAddress = new Uri(_config["ApiBaseUrl"]);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            try
            {
                var response = await client.DeleteAsync($"api/course/content/{contentId}");

                if (response.IsSuccessStatusCode)
                    return Json(new { success = true });

                var error = await response.Content.ReadAsStringAsync();
                _logger.LogError("Failed to delete content: {Error}", error);
                return Json(new { success = false, message = error });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting content");
                return Json(new { success = false, message = ex.Message });
            }
        }

        // ─── Wishlist helpers / endpoints / browse / preview / details ───
        // (everything below this point is your existing code, unchanged)
        // ────────────────────────────────────────────────────────────────

        // Lookup model for the category dropdown. Newtonsoft matches property
        // names case-insensitively, so this binds whether the API emits
        // CategoryID/CategoryName or categoryID/categoryName.
        private class CategoryLookup
        {
            public int CategoryID { get; set; }
            public string CategoryName { get; set; } = "";
        }

        // Returns the set of CourseIDs the student is enrolled in
        // (TblStudentCourses — covers paid, free, and assigned), or null if the
        // lookup failed so callers can fail-open and never lock out a paid student.
        private async Task<HashSet<int>?> GetEnrolledCourseIdsAsync(HttpClient client, int studentId)
        {
            try
            {
                var resp = await client.GetAsync($"api/courseassignment/student-courses/{studentId}");
                if (!resp.IsSuccessStatusCode) return null;

                var json = await resp.Content.ReadAsStringAsync();
                var parsed = JToken.Parse(json);
                var arr = parsed.Type == JTokenType.Array
                            ? (JArray)parsed
                            : parsed["data"] as JArray;

                var set = new HashSet<int>();
                if (arr != null)
                    foreach (var t in arr)
                    {
                        var idTok = t["CourseID"] ?? t["courseID"] ?? t["courseId"];
                        if (idTok != null && int.TryParse(idTok.ToString(), out var cid))
                            set.Add(cid);
                    }
                return set;
            }
            catch { return null; }
        }

        private async Task<HashSet<int>> GetWishlistIdsAsync(HttpClient client, int studentId)
        {
            try
            {
                var resp = await client.GetAsync($"api/wishlist/ids/{studentId}");
                if (!resp.IsSuccessStatusCode) return new HashSet<int>();
                var json = await resp.Content.ReadAsStringAsync();
                var ids = JsonConvert.DeserializeObject<List<int>>(json) ?? new List<int>();
                return ids.ToHashSet();
            }
            catch { return new HashSet<int>(); }
        }

        [HttpGet]
        public async Task<IActionResult> WishlistCount()
        {
            var token = HttpContext.Session.GetString("JwtToken");
            var userId = HttpContext.Session.GetString("UserID");
            var roleId = HttpContext.Session.GetInt32("RoleID") ?? 0;

            if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(userId) || roleId != 3)
                return Json(new { success = false, count = 0 });

            try
            {
                var client = _httpClientFactory.CreateClient();
                client.BaseAddress = new Uri(_config["ApiBaseUrl"]);
                client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", token);

                var ids = await GetWishlistIdsAsync(client, int.Parse(userId));
                return Json(new { success = true, count = ids.Count });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading wishlist count");
                return Json(new { success = false, count = 0 });
            }
        }

        private async Task<Dictionary<int, CourseProgressRow>> GetCourseProgressAsync(
            HttpClient client, int studentId, int courseId)
        {
            var map = new Dictionary<int, CourseProgressRow>();
            try
            {
                var resp = await client.GetAsync($"api/progress/course/{studentId}/{courseId}");
                if (!resp.IsSuccessStatusCode) return map;

                var json = await resp.Content.ReadAsStringAsync();
                var rows = JsonConvert.DeserializeObject<List<CourseProgressRow>>(json)
                           ?? new List<CourseProgressRow>();

                foreach (var r in rows) map[r.ContentID] = r;
            }
            catch { }
            return map;
        }

        private async Task<Dictionary<int, int>> GetProgressSummaryAsync(HttpClient client, int studentId)
        {
            var map = new Dictionary<int, int>();
            try
            {
                var resp = await client.GetAsync($"api/progress/summary/{studentId}");
                if (!resp.IsSuccessStatusCode) return map;

                var json = await resp.Content.ReadAsStringAsync();
                var rows = JsonConvert.DeserializeObject<List<ProgressSummaryRow>>(json)
                           ?? new List<ProgressSummaryRow>();

                foreach (var r in rows)
                {
                    int pct = r.TotalContents > 0
                        ? (int)Math.Round(r.CompletedContents * 100.0 / r.TotalContents)
                        : 0;
                    map[r.CourseID] = Math.Min(pct, 100);
                }
            }
            catch { }
            return map;
        }

        private async Task<Dictionary<int, ReviewSummaryRow>> GetReviewSummaryAsync(HttpClient client)
        {
            var map = new Dictionary<int, ReviewSummaryRow>();
            try
            {
                var resp = await client.GetAsync("api/review/summary");
                if (!resp.IsSuccessStatusCode) return map;

                var json = await resp.Content.ReadAsStringAsync();
                var rows = JsonConvert.DeserializeObject<List<ReviewSummaryRow>>(json)
                           ?? new List<ReviewSummaryRow>();

                foreach (var r in rows) map[r.CourseID] = r;
            }
            catch { }
            return map;
        }

        [HttpGet]
        public async Task<IActionResult> List()
        {
            var token = HttpContext.Session.GetString("JwtToken");
            var userId = HttpContext.Session.GetString("UserID");
            var roleId = HttpContext.Session.GetInt32("RoleID") ?? 0;
            var email = HttpContext.Session.GetString("Email") ?? "Student";

            if (string.IsNullOrEmpty(token))
                return RedirectToAction("Index", "Home");

            var client = _httpClientFactory.CreateClient();
            client.BaseAddress = new Uri(_config["ApiBaseUrl"]);
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", token);

            // ── STUDENT → MyCourses ──
            if (roleId == 3)
            {
                var sid = int.TryParse(userId, out var s) ? s : 0;
                var vm = new StudentDashboardViewModel
                {
                    StudentName = email,
                    StudentEmail = email
                };

                try
                {
                    var resp = await client.GetAsync($"api/courseassignment/student-courses/{sid}");
                    var json = await resp.Content.ReadAsStringAsync();

                    if (resp.IsSuccessStatusCode)
                    {
                        var parsed = JToken.Parse(json);
                        List<CourseSummaryViewModel> courses;

                        if (parsed.Type == JTokenType.Array)
                            courses = JsonConvert.DeserializeObject<List<CourseSummaryViewModel>>(json)
                                      ?? new List<CourseSummaryViewModel>();
                        else
                            courses = parsed["data"]?.ToObject<List<CourseSummaryViewModel>>()
                                      ?? new List<CourseSummaryViewModel>();

                        vm.AssignedCourses = courses.Select(c => new StudentCourseCardViewModel
                        {
                            CourseID = c.CourseID,
                            CourseTitle = c.CourseTitle,
                            TutorName = c.TutorName,
                            CourseLevelName = c.CourseLevelName ?? c.CourseLevel,
                            CoverImage = c.CoverImage,
                            Price = c.Price,
                            Language = c.Language
                        }).ToList();

                        var summary = await GetProgressSummaryAsync(client, sid);
                        foreach (var card in vm.AssignedCourses)
                            card.ProgressPercent = summary.TryGetValue(card.CourseID, out var p) ? p : 0;

                        var reviews = await GetReviewSummaryAsync(client);
                        foreach (var card in vm.AssignedCourses)
                            if (reviews.TryGetValue(card.CourseID, out var rv))
                            {
                                card.AvgRating = rv.AvgRating;
                                card.ReviewCount = rv.ReviewCount;
                            }

                        vm.TotalAssignedCourses = vm.AssignedCourses.Count;
                        vm.CompletedCourses = vm.AssignedCourses.Count(c => c.ProgressPercent >= 100);
                        vm.InProgressCourses = vm.AssignedCourses.Count - vm.CompletedCourses;
                    }
                    else
                    {
                        TempData["Error"] = $"Failed to load your courses: {resp.StatusCode}";
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error loading my courses");
                    TempData["Error"] = "Error: " + ex.Message;
                }

                return View("MyCourses", vm);
            }

            // ── ADMIN / TUTOR → catalog ──
            try
            {
                var response = await client.GetAsync("api/course/list");
                var json = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    TempData["Error"] = $"❌ Failed to load courses: {response.StatusCode}";
                    return View(new List<CourseSummaryViewModel>());
                }

                var courses = JsonConvert.DeserializeObject<List<CourseSummaryViewModel>>(json)
                              ?? new List<CourseSummaryViewModel>();

                return View(courses);
            }
            catch (Exception ex)
            {
                TempData["Error"] = "Error: " + ex.Message;
                return View(new List<CourseSummaryViewModel>());
            }
        }

        [HttpGet]
        public IActionResult StudentDashboard()
            => RedirectToAction("Index", "StudentDashboard");

        [HttpGet]
        [System.Obsolete]
        private async Task<IActionResult> StudentDashboardLegacy()
        {
            var token = HttpContext.Session.GetString("JwtToken");
            var userId = HttpContext.Session.GetString("UserID");
            var roleId = HttpContext.Session.GetInt32("RoleID") ?? 0;
            var studentEmail = HttpContext.Session.GetString("Email") ?? "Student";

            if (string.IsNullOrEmpty(token))
                return RedirectToAction("Index", "Home");
            if (roleId != 3)
                return RedirectToAction("Index", "Dashboard");

            var client = _httpClientFactory.CreateClient();
            client.BaseAddress = new Uri(_config["ApiBaseUrl"]);
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", token);

            var vm = new StudentDashboardViewModel
            {
                StudentName = studentEmail,
                StudentEmail = studentEmail
            };

            try
            {
                var response = await client.GetAsync($"api/courseassignment/student-courses/{userId}");
                var json = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    var parsed = JToken.Parse(json);
                    List<CourseSummaryViewModel> courses;

                    if (parsed.Type == JTokenType.Array)
                        courses = JsonConvert.DeserializeObject<List<CourseSummaryViewModel>>(json)
                                  ?? new List<CourseSummaryViewModel>();
                    else
                        courses = parsed["data"]?.ToObject<List<CourseSummaryViewModel>>()
                                  ?? new List<CourseSummaryViewModel>();

                    vm.TotalAssignedCourses = courses.Count;

                    var sidDash = int.TryParse(userId, out var sd) ? sd : 0;
                    var summaryDash = await GetProgressSummaryAsync(client, sidDash);
                    vm.CompletedCourses = courses.Count(c =>
                        summaryDash.TryGetValue(c.CourseID, out var p) && p >= 100);
                    vm.InProgressCourses = courses.Count - vm.CompletedCourses;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading student dashboard");
                TempData["Error"] = "Error loading dashboard: " + ex.Message;
            }

            return View(vm);
        }

        [HttpGet]
        public async Task<IActionResult> Browse()
        {
            var token = HttpContext.Session.GetString("JwtToken");
            var userId = HttpContext.Session.GetString("UserID");
            var roleId = HttpContext.Session.GetInt32("RoleID") ?? 0;
            var email = HttpContext.Session.GetString("Email") ?? "Student";

            if (string.IsNullOrEmpty(token))
                return RedirectToAction("Index", "Home");
            // Issue 4 hardening: a broken/empty session role must go to Login,
            // never the admin dashboard. Students (RoleID 3 after the
            // registration fix) proceed; real admins/tutors get their own
            // dashboard.
            if (roleId == 0)
                return RedirectToAction("Index", "Login");
            if (roleId != 3)
                return RedirectToAction("Index", "Dashboard");

            var sid = int.TryParse(userId, out var s) ? s : 0;
            var vm = new BrowseCoursesViewModel
            {
                StudentName = email,
                StudentID = sid,
                RazorpayKey = _config["Razorpay:KeyId"] ?? ""
            };

            var client = _httpClientFactory.CreateClient();
            client.BaseAddress = new Uri(_config["ApiBaseUrl"]);
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", token);

            try
            {
                var resp = await client.GetAsync($"api/courseassignment/courses/{sid}");
                var json = await resp.Content.ReadAsStringAsync();

                if (resp.IsSuccessStatusCode)
                {
                    var parsed = JToken.Parse(json);
                    List<BrowseCourseCard> all;

                    if (parsed.Type == JTokenType.Array)
                        all = JsonConvert.DeserializeObject<List<BrowseCourseCard>>(json)
                              ?? new List<BrowseCourseCard>();
                    else
                        all = parsed["data"]?.ToObject<List<BrowseCourseCard>>()
                              ?? new List<BrowseCourseCard>();

                    var wishIds = await GetWishlistIdsAsync(client, sid);
                    foreach (var c in all) c.IsWishlisted = wishIds.Contains(c.CourseID);

                    var reviewMap = await GetReviewSummaryAsync(client);
                    foreach (var c in all)
                        if (reviewMap.TryGetValue(c.CourseID, out var rv))
                        {
                            c.AvgRating = rv.AvgRating;
                            c.ReviewCount = rv.ReviewCount;
                        }

                    vm.AssignedCourses = all.Where(c => c.IsAssigned).ToList();
                    vm.AllCourses = all.Where(c => !c.IsAssigned).ToList();
                }
                else
                {
                    TempData["Error"] = $"Failed to load courses: {resp.StatusCode}";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading browse courses");
                TempData["Error"] = "Error loading courses: " + ex.Message;
            }

            return View(vm);
        }

        [HttpGet("Courses/Preview/{id:int}")]
        [HttpGet("Courses/Preview/{id:int}/{slug?}")]
        public async Task<IActionResult> Preview(int id, string slug = null)
        {
            var token = HttpContext.Session.GetString("JwtToken");
            var userId = HttpContext.Session.GetString("UserID");
            var roleId = HttpContext.Session.GetInt32("RoleID") ?? 0;

            if (string.IsNullOrEmpty(token))
                return RedirectToAction("Index", "Home");
            if (roleId != 3)
                return RedirectToAction("Details", new { id });

            var sid = int.TryParse(userId, out var s) ? s : 0;

            var client = _httpClientFactory.CreateClient();
            client.BaseAddress = new Uri(_config["ApiBaseUrl"]);
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", token);

            try
            {
                var resp = await client.GetAsync($"api/course/details/{id}");
                var json = await resp.Content.ReadAsStringAsync();

                if (!resp.IsSuccessStatusCode || string.IsNullOrWhiteSpace(json))
                {
                    TempData["Error"] = "Failed to load course preview.";
                    return RedirectToAction("Browse");
                }

                var data = JsonConvert.DeserializeObject<CourseApiResponse>(json);
                if (data?.Course == null)
                {
                    TempData["Error"] = "Course not found.";
                    return RedirectToAction("Browse");
                }

                bool isAssigned = false;
                try
                {
                    var aResp = await client.GetAsync($"api/courseassignment/courses/{sid}");
                    if (aResp.IsSuccessStatusCode)
                    {
                        var aJson = await aResp.Content.ReadAsStringAsync();
                        var aToken = JToken.Parse(aJson);
                        var list = (aToken.Type == JTokenType.Array
                                        ? aToken.ToObject<List<BrowseCourseCard>>()
                                        : aToken["data"]?.ToObject<List<BrowseCourseCard>>())
                                      ?? new List<BrowseCourseCard>();

                        isAssigned = list.Any(c => c.CourseID == id && c.IsAssigned);
                    }
                }
                catch { }

                if (isAssigned)
                    return RedirectToAction("Details", new { id });

                var wishIds = await GetWishlistIdsAsync(client, sid);

                var vm = new BrowsePreviewViewModel
                {
                    CourseID = data.Course.CourseID,
                    CourseTitle = data.Course.CourseTitle ?? "Untitled",
                    TutorName = data.Course.TutorName ?? "-",
                    CourseLevelName = data.Course.CourseLevelName ?? "-",
                    Language = data.Course.Language ?? "-",
                    CoverImage = data.Course.CoverImage ?? "",
                    PromoVideo = data.Course.PromoVideo ?? "",
                    OneLineDescription = data.Course.OneLineDescription ?? "",
                    Price = data.Course.Price,
                    DurationHrs = data.Course.DurationHrs,
                    StudentID = sid,
                    IsAssigned = false,
                    IsWishlisted = wishIds.Contains(data.Course.CourseID),
                    RazorpayKey = _config["Razorpay:KeyId"] ?? "",
                    Contents = (data.Contents ?? new()).Select(c => new BrowseContentCard
                    {
                        ContentTitle = c.ContentTitle,
                        Description = c.Description,
                        VideoThumbnail = c.VideoThumbnail
                    }).ToList()
                };

                return View(vm);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading preview {Id}", id);
                TempData["Error"] = "Error: " + ex.Message;
                return RedirectToAction("Browse");
            }
        }

        [HttpGet]
        public async Task<IActionResult> Wishlist()
        {
            var token = HttpContext.Session.GetString("JwtToken");
            var userId = HttpContext.Session.GetString("UserID");
            var roleId = HttpContext.Session.GetInt32("RoleID") ?? 0;
            var email = HttpContext.Session.GetString("Email") ?? "Student";

            if (string.IsNullOrEmpty(token))
                return RedirectToAction("Index", "Home");
            if (roleId != 3)
                return RedirectToAction("Index", "Dashboard");

            var sid = int.TryParse(userId, out var s) ? s : 0;
            var vm = new WishlistViewModel
            {
                StudentName = email,
                StudentID = sid,
                RazorpayKey = _config["Razorpay:KeyId"] ?? ""
            };

            var client = _httpClientFactory.CreateClient();
            client.BaseAddress = new Uri(_config["ApiBaseUrl"]);
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", token);

            try
            {
                var resp = await client.GetAsync($"api/wishlist/{sid}");
                var json = await resp.Content.ReadAsStringAsync();

                if (resp.IsSuccessStatusCode)
                {
                    var parsed = JToken.Parse(json);
                    List<WishlistCourseCard> courses;

                    if (parsed.Type == JTokenType.Array)
                        courses = JsonConvert.DeserializeObject<List<WishlistCourseCard>>(json)
                                  ?? new List<WishlistCourseCard>();
                    else
                        courses = parsed["data"]?.ToObject<List<WishlistCourseCard>>()
                                  ?? new List<WishlistCourseCard>();

                    var reviewMap = await GetReviewSummaryAsync(client);
                    foreach (var c in courses)
                        if (reviewMap.TryGetValue(c.CourseID, out var rv))
                        {
                            c.AvgRating = rv.AvgRating;
                            c.ReviewCount = rv.ReviewCount;
                        }

                    vm.Courses = courses;
                }
                else
                {
                    TempData["Error"] = $"Failed to load wishlist: {resp.StatusCode}";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading wishlist");
                TempData["Error"] = "Error loading wishlist: " + ex.Message;
            }

            return View(vm);
        }

        [HttpPost]
        public async Task<IActionResult> ToggleWishlist([FromBody] WishlistToggleRequest req)
        {
            var token = HttpContext.Session.GetString("JwtToken");
            var userId = HttpContext.Session.GetString("UserID");
            var roleId = HttpContext.Session.GetInt32("RoleID") ?? 0;

            if (string.IsNullOrEmpty(token) || roleId != 3)
                return Json(new { success = false, message = "Unauthorized" });
            if (req == null || req.CourseID <= 0)
                return Json(new { success = false, message = "Invalid course." });

            try
            {
                var client = _httpClientFactory.CreateClient();
                client.BaseAddress = new Uri(_config["ApiBaseUrl"]);
                client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", token);

                var payload = new { StudentID = int.Parse(userId), CourseID = req.CourseID };
                var content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");

                var resp = await client.PostAsync("api/wishlist/toggle", content);
                var json = await resp.Content.ReadAsStringAsync();
                return Content(json, "application/json");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error toggling wishlist");
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> EnrollFree([FromBody] CreatePaymentOrderRequest req)
        {
            var token = HttpContext.Session.GetString("JwtToken");
            var userId = HttpContext.Session.GetString("UserID");
            var email = HttpContext.Session.GetString("Email") ?? "Student";
            var roleId = HttpContext.Session.GetInt32("RoleID") ?? 0;

            if (string.IsNullOrEmpty(token) || roleId != 3)
                return Json(new { success = false, message = "Unauthorized" });
            if (req == null || req.CourseID <= 0)
                return Json(new { success = false, message = "Invalid course." });

            try
            {
                var client = _httpClientFactory.CreateClient();
                client.BaseAddress = new Uri(_config["ApiBaseUrl"]);
                client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", token);

                var payload = new
                {
                    StudentID = int.Parse(userId),
                    CourseID = req.CourseID,
                    AssignedBy = email
                };

                var content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
                var resp = await client.PostAsync("api/payment/enroll-free", content);
                var json = await resp.Content.ReadAsStringAsync();
                return Content(json, "application/json");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error enrolling free");
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> CreatePaymentOrder([FromBody] CreatePaymentOrderRequest req)
        {
            var token = HttpContext.Session.GetString("JwtToken");
            var userId = HttpContext.Session.GetString("UserID");
            var roleId = HttpContext.Session.GetInt32("RoleID") ?? 0;

            if (string.IsNullOrEmpty(token) || roleId != 3)
                return Json(new { success = false, message = "Unauthorized" });
            if (req == null || req.CourseID <= 0)
                return Json(new { success = false, message = "Invalid course." });

            try
            {
                var client = _httpClientFactory.CreateClient();
                client.BaseAddress = new Uri(_config["ApiBaseUrl"]);
                client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", token);

                var payload = new
                {
                    StudentID = int.Parse(userId),
                    CourseID = req.CourseID,
                    CouponCode = string.IsNullOrWhiteSpace(req.CouponCode)
                                    ? null
                                    : req.CouponCode.Trim().ToUpper()
                };

                var content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
                var resp = await client.PostAsync("api/payment/create-order", content);
                var json = await resp.Content.ReadAsStringAsync();
                return Content(json, "application/json");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating order");
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> VerifyPayment([FromBody] VerifyPaymentRequest req)
        {
            var token = HttpContext.Session.GetString("JwtToken");
            var userId = HttpContext.Session.GetString("UserID");
            var email = HttpContext.Session.GetString("Email") ?? "Student";
            var roleId = HttpContext.Session.GetInt32("RoleID") ?? 0;

            if (string.IsNullOrEmpty(token) || roleId != 3)
                return Json(new { success = false, message = "Unauthorized" });

            if (req == null
                || req.CourseID <= 0
                || string.IsNullOrEmpty(req.RazorpayOrderId)
                || string.IsNullOrEmpty(req.RazorpayPaymentId)
                || string.IsNullOrEmpty(req.RazorpaySignature))
            {
                return Json(new { success = false, message = "Missing payment fields." });
            }

            try
            {
                var client = _httpClientFactory.CreateClient();
                client.BaseAddress = new Uri(_config["ApiBaseUrl"]);
                client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", token);

                var payload = new
                {
                    StudentID = int.Parse(userId),
                    CourseID = req.CourseID,
                    RazorpayOrderId = req.RazorpayOrderId,
                    RazorpayPaymentId = req.RazorpayPaymentId,
                    RazorpaySignature = req.RazorpaySignature,
                    AssignedBy = email,
                    CouponID = req.CouponID,
                    DiscountAmount = req.DiscountAmount,
                    OriginalAmount = req.OriginalAmount
                };

                var content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
                var resp = await client.PostAsync("api/payment/verify", content);
                var json = await resp.Content.ReadAsStringAsync();
                return Content(json, "application/json");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error verifying payment");
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> Details(int id)
        {
            var token = HttpContext.Session.GetString("JwtToken");
            var userId = HttpContext.Session.GetString("UserID");
            var roleId = HttpContext.Session.GetInt32("RoleID") ?? 0;

            if (string.IsNullOrEmpty(token))
                return RedirectToAction("Index", "Home");

            var client = _httpClientFactory.CreateClient();
            client.BaseAddress = new Uri(_config["ApiBaseUrl"]);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            try
            {
                var response = await client.GetAsync($"api/course/details/{id}");
                var json = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    TempData["Error"] = $"Failed to load course details → {response.StatusCode}";
                    return RedirectToAction("List");
                }

                if (string.IsNullOrWhiteSpace(json))
                {
                    TempData["Error"] = "Empty response received from API.";
                    return RedirectToAction("List");
                }

                var data = JsonConvert.DeserializeObject<CourseApiResponse>(json);

                if (data == null || data.Course == null)
                {
                    TempData["Error"] = "Course not found.";
                    return RedirectToAction("List");
                }

                var vm = new CourseDetailsViewModel
                {
                    CourseID = data.Course.CourseID,
                    CourseTitle = data.Course.CourseTitle ?? "Untitled",
                    CourseLevel = data.Course.CourseLevel ?? "-",
                    CourseLevelName = data.Course.CourseLevelName ?? "-",
                    Language = data.Course.Language ?? "-",
                    Category = data.Course.Category ?? "-",
                    TutorName = data.Course.TutorName ?? "-",
                    Price = data.Course.Price,
                    StartDate = data.Course.StartDate,
                    EndDate = data.Course.EndDate,
                    DurationHrs = data.Course.DurationHrs,
                    OneLineDescription = data.Course.OneLineDescription ?? "",
                    CourseLogo = data.Course.CourseLogo ?? "",
                    CoverImage = data.Course.CoverImage ?? "",
                    PromoVideo = data.Course.PromoVideo ?? "",
                    IsStudent = roleId == 3,
                    Contents = data.Contents?.Select(c => new CourseContentViewModel
                    {
                        ContentID = c.ContentID,
                        ContentTitle = c.ContentTitle,
                        Description = c.Description,
                        VideoThumbnail = c.VideoThumbnail,
                        ContentFile = c.ContentFile,
                        FileType = (string)(c.FileType ?? "Video")   // ← NEW: default to Video for legacy rows
                    }).ToList() ?? new List<CourseContentViewModel>()
                };

                if (roleId == 3)
                {
                    var sid = int.TryParse(userId, out var s) ? s : 0;

                    // ── Issue 6: a student may only open the player for a course
                    //    they are actually enrolled in (free, paid, or assigned).
                    //    Otherwise send them to the Preview (sales) page to
                    //    enroll/pay — this closes the "direct access without
                    //    payment" hole. Fail-open on lookup error so a transient
                    //    blip never locks out a paying student.
                    var enrolledIds = await GetEnrolledCourseIdsAsync(client, sid);
                    if (enrolledIds != null && !enrolledIds.Contains(id))
                        return RedirectToAction("Preview", new { id });
                    vm.IsEnrolled = true;

                    var prog = await GetCourseProgressAsync(client, sid, id);

                    foreach (var c in vm.Contents)
                    {
                        if (prog.TryGetValue(c.ContentID, out var pr))
                        {
                            c.IsCompleted = pr.IsCompleted;
                            c.LastPositionSeconds = pr.LastPositionSeconds;
                        }
                    }

                    vm.TotalContents = vm.Contents.Count;
                    vm.CompletedContents = vm.Contents.Count(c => c.IsCompleted);
                    vm.ProgressPercent = vm.TotalContents > 0
                        ? (int)Math.Round(vm.CompletedContents * 100.0 / vm.TotalContents)
                        : 0;
                }

                try
                {
                    var rResp = await client.GetAsync($"api/review/course/{id}");
                    if (rResp.IsSuccessStatusCode)
                    {
                        var rJson = await rResp.Content.ReadAsStringAsync();
                        var reviews = JsonConvert.DeserializeObject<List<CourseReviewItem>>(rJson)
                                      ?? new List<CourseReviewItem>();

                        vm.Reviews = reviews;
                        vm.ReviewCount = reviews.Count;
                        vm.AvgRating = reviews.Count > 0
                            ? Math.Round(reviews.Average(r => r.Rating), 2)
                            : 0;

                        var breakdown = new int[5];
                        foreach (var r in reviews)
                            if (r.Rating >= 1 && r.Rating <= 5)
                                breakdown[5 - r.Rating]++;
                        vm.RatingBreakdown = breakdown;

                        if (roleId == 3 && int.TryParse(userId, out var myId))
                            vm.MyReview = reviews.FirstOrDefault(r => r.StudentID == myId);
                    }
                }
                catch (Exception rex)
                {
                    _logger.LogError(rex, "Error loading reviews for course {Id}", id);
                }

                return View(vm);
            }
            catch (Exception ex)
            {
                TempData["Error"] = "Error: " + ex.Message;
                return RedirectToAction("List");
            }
        }

        [HttpPost]
        public async Task<IActionResult> MarkContentComplete([FromBody] MarkContentRequest req)
        {
            var token = HttpContext.Session.GetString("JwtToken");
            var userId = HttpContext.Session.GetString("UserID");
            var roleId = HttpContext.Session.GetInt32("RoleID") ?? 0;

            if (string.IsNullOrEmpty(token) || roleId != 3)
                return Json(new { success = false, message = "Unauthorized" });
            if (req == null || req.CourseID <= 0 || req.ContentID <= 0)
                return Json(new { success = false, message = "Invalid request." });

            try
            {
                var client = _httpClientFactory.CreateClient();
                client.BaseAddress = new Uri(_config["ApiBaseUrl"]);
                client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", token);

                var payload = new
                {
                    StudentID = int.Parse(userId),
                    CourseID = req.CourseID,
                    ContentID = req.ContentID,
                    Completed = req.Completed
                };

                var content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
                var resp = await client.PostAsync("api/progress/mark", content);
                var json = await resp.Content.ReadAsStringAsync();
                return Content(json, "application/json");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error marking content complete");
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> SaveVideoPosition([FromBody] SaveVideoPositionRequest req)
        {
            var token = HttpContext.Session.GetString("JwtToken");
            var userId = HttpContext.Session.GetString("UserID");
            var roleId = HttpContext.Session.GetInt32("RoleID") ?? 0;

            if (string.IsNullOrEmpty(token) || roleId != 3)
                return Json(new { success = false, message = "Unauthorized" });
            if (req == null || req.CourseID <= 0 || req.ContentID <= 0)
                return Json(new { success = false, message = "Invalid request." });

            try
            {
                var client = _httpClientFactory.CreateClient();
                client.BaseAddress = new Uri(_config["ApiBaseUrl"]);
                client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", token);

                var payload = new
                {
                    StudentID = int.Parse(userId),
                    CourseID = req.CourseID,
                    ContentID = req.ContentID,
                    Position = req.Position,
                    Duration = req.Duration
                };

                var content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
                var resp = await client.PostAsync("api/progress/position", content);
                var json = await resp.Content.ReadAsStringAsync();
                return Content(json, "application/json");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving video position");
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> SubmitReview([FromBody] SubmitReviewRequest req)
        {
            var token = HttpContext.Session.GetString("JwtToken");
            var userId = HttpContext.Session.GetString("UserID");
            var roleId = HttpContext.Session.GetInt32("RoleID") ?? 0;

            if (string.IsNullOrEmpty(token) || roleId != 3)
                return Json(new { success = false, message = "Unauthorized" });
            if (req == null || req.CourseID <= 0)
                return Json(new { success = false, message = "Invalid course." });
            if (req.Rating < 1 || req.Rating > 5)
                return Json(new { success = false, message = "Please select a 1-5 star rating." });

            try
            {
                var client = _httpClientFactory.CreateClient();
                client.BaseAddress = new Uri(_config["ApiBaseUrl"]);
                client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", token);

                var payload = new
                {
                    CourseID = req.CourseID,
                    StudentID = int.Parse(userId),
                    Rating = req.Rating,
                    ReviewText = req.ReviewText ?? ""
                };

                var content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
                var resp = await client.PostAsync("api/review/save", content);
                var json = await resp.Content.ReadAsStringAsync();
                return Content(json, "application/json");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error submitting review");
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpGet]
        public IActionResult StreamVideo(int courseId, string contentTitle, string fileName)
        {
            var filePath = Path.Combine(
                Directory.GetCurrentDirectory(),
                "wwwroot", "Coursecontent",
                $"Course_{courseId}",
                fileName);

            if (!System.IO.File.Exists(filePath))
                return NotFound();

            var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
            return File(stream, "video/mp4", enableRangeProcessing: true);
        }

        [HttpPost]
        public async Task<IActionResult> ReorderContent([FromBody] ReorderContentMvcRequest req)
        {
            var token = HttpContext.Session.GetString("JwtToken");
            if (string.IsNullOrEmpty(token))
                return Json(new { success = false, message = "Unauthorized" });

            if (req == null || req.CourseID <= 0 || req.ContentIDs == null || req.ContentIDs.Count == 0)
                return Json(new { success = false, message = "Invalid request" });

            try
            {
                var client = _httpClientFactory.CreateClient();
                client.BaseAddress = new Uri(_config["ApiBaseUrl"]);
                client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", token);

                var payload = new { CourseID = req.CourseID, ContentIDs = req.ContentIDs };
                var http = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");

                var resp = await client.PostAsync("api/course/reorder-content", http);
                return Json(new { success = resp.IsSuccessStatusCode });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reordering content");
                return Json(new { success = false, message = ex.Message });
            }
        }
        // GET /Courses/Recommended?limit=N  (AJAX – student dashboard)
        [HttpGet]
        public async Task<IActionResult> Recommended(int limit = 6)
        {
            var token = HttpContext.Session.GetString("JwtToken");
            var userId = HttpContext.Session.GetString("UserID");
            try
            {
                var client = _httpClientFactory.CreateClient();
                client.BaseAddress = new Uri(_config["ApiBaseUrl"]);
                if (!string.IsNullOrEmpty(token))
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

                var resp = await client.GetAsync($"api/course/recommended?userId={userId}&limit={limit}");
                if (resp.IsSuccessStatusCode)
                {
                    var json = await resp.Content.ReadAsStringAsync();
                    var root = Newtonsoft.Json.Linq.JToken.Parse(json);
                    if (root is Newtonsoft.Json.Linq.JArray) return Content(json, "application/json");
                    var data = root["data"] ?? root["Data"] ?? root["courses"] ?? root;
                    return Content(data.ToString(), "application/json");
                }
                // Fallback: return first 6 Browse courses
                var browseResp = await client.GetAsync($"api/course/list?pageSize={limit}&page=1");
                if (browseResp.IsSuccessStatusCode)
                {
                    var json2 = await browseResp.Content.ReadAsStringAsync();
                    var root2 = Newtonsoft.Json.Linq.JToken.Parse(json2);
                    if (root2 is Newtonsoft.Json.Linq.JArray) return Content(json2, "application/json");
                    var data2 = root2["data"] ?? root2["Data"] ?? root2;
                    return Content(data2.ToString(), "application/json");
                }
            }
            catch { }
            return Json(new object[0]);
        }
    }
}
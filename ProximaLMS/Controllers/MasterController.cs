using Microsoft.AspNetCore.Mvc;
using ProximaLMS.Models;
using ClosedXML.Excel;
using System.Net.Http.Headers;
using System.Net.Http;
using System.Text.Json;
using System.Text;
namespace ProximaLMS.Controllers
{
    public class MasterController : Controller
    {
        private readonly IConfiguration _configuration;

        public MasterController(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        private HttpClient CreateClient()
        {
            return new HttpClient
            {
                BaseAddress = new Uri(_configuration["ApiBaseUrl"])
            };
        }

        // ==============================
        // STUDENT LIST (API CALL)
        // ==============================
        public async Task<IActionResult> StudentList()
        {
            using var client = CreateClient();

            var response = await client.GetAsync("/api/master/students");

            if (!response.IsSuccessStatusCode)
            {
                TempData["Error"] = "Unable to load students";
                return View(new List<StudentViewModel>());
            }

            // ✅ READ WRAPPED RESPONSE
            var apiResponse =
                await response.Content.ReadFromJsonAsync<ApiResponse<List<StudentViewModel>>>();

            return View(apiResponse?.Data ?? new List<StudentViewModel>());
        }

        // ==============================
        // EDIT (Redirect only)
        // ==============================
        [HttpPost]
        public async Task<IActionResult> UpdateStudentStatus([FromBody] StudentStatusInput input)
        {
            // The toggle posts a JSON body { id, isActive }. Without [FromBody]
            // the simple params never bound (id was always null) so the update
            // silently did nothing — that was the broken toggle.
            if (input == null || string.IsNullOrWhiteSpace(input.Id))
                return Json(new { success = false, message = "Missing student id." });

            using var client = CreateClient();

            var payload = new
            {
                Id = input.Id,
                IsActive = input.IsActive
            };

            var response = await client.PostAsJsonAsync(
                "/api/master/student/status",
                payload
            );

            if (!response.IsSuccessStatusCode)
            {
                return Json(new
                {
                    success = false,
                    message = "Unable to update status"
                });
            }

            var apiResult = await response.Content.ReadFromJsonAsync<dynamic>();
            return Json(apiResult);
        }

        public async Task<IActionResult> CourseList()
        {
            using var client = CreateClient();

            var response = await client.GetAsync("/api/master/courselist");

            if (!response.IsSuccessStatusCode)
            {
                TempData["Error"] = "Unable to load Courses";
                return View(new List<CourseViewModel>());
            }

            var apiResponse =
                await response.Content.ReadFromJsonAsync<ApiResponse<List<CourseViewModel>>>();

            return View(apiResponse?.Data ?? new List<CourseViewModel>());
        }

        [HttpPost]
        public async Task<IActionResult> UpdateCourseStatus([FromBody] JsonElement body)
        {
            using var client = CreateClient();

            var token = HttpContext.Session.GetString("JwtToken");
            if (!string.IsNullOrEmpty(token))
                client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", token);

            var req = new HttpRequestMessage(HttpMethod.Post, "/api/master/course/status")
            {
                Content = new StringContent(body.GetRawText(), Encoding.UTF8, "application/json")
            };

            var resp = await client.SendAsync(req);
            Response.StatusCode = (int)resp.StatusCode;
            return Content(await resp.Content.ReadAsStringAsync(), "application/json");
        }

        [HttpGet]
        public async Task<IActionResult> DownloadStudentsExcel()
        {
            using var client = CreateClient();

            var response = await client.GetAsync("/api/master/students");

            if (!response.IsSuccessStatusCode)
            {
                TempData["Error"] = "Unable to download students";
                return RedirectToAction("StudentList");
            }

            var apiResponse =
                await response.Content.ReadFromJsonAsync<ApiResponse<List<StudentViewModel>>>();

            var students = apiResponse?.Data ?? new List<StudentViewModel>();

            using (var workbook = new XLWorkbook())
            {
                var worksheet = workbook.Worksheets.Add("Students");

                // ===== HEADER =====
                worksheet.Cell(1, 1).Value = "Name";
                worksheet.Cell(1, 2).Value = "Gender";
                worksheet.Cell(1, 3).Value = "Email";
                worksheet.Cell(1, 4).Value = "Mobile";
                worksheet.Cell(1, 5).Value = "Created Date";
                worksheet.Cell(1, 6).Value = "Status";

                // Make header bold
                worksheet.Range("A1:F1").Style.Font.Bold = true;
                worksheet.Range("A1:F1").Style.Fill.BackgroundColor = XLColor.LightGray;

                int row = 2;

                foreach (var s in students)
                {
                    worksheet.Cell(row, 1).Value = s.Name;
                    worksheet.Cell(row, 2).Value = s.Gender;
                    worksheet.Cell(row, 3).Value = s.Email;
                    worksheet.Cell(row, 4).Value = s.MobileNumber;
                    worksheet.Cell(row, 5).Value = s.CreatedDate;
                    worksheet.Cell(row, 6).Value = s.IsActive ? "Active" : "Inactive";
                    row++;
                }

                worksheet.Columns().AdjustToContents();

                using var stream = new MemoryStream();
                workbook.SaveAs(stream);
                var content = stream.ToArray();

                return File(content,
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    $"Students_{DateTime.Now:yyyyMMddHHmmss}.xlsx");
            }
        }

        // Body posted by the Student List status toggle: { id, isActive }.
        public class StudentStatusInput
        {
            public string Id { get; set; } = "";
            public bool IsActive { get; set; }
        }
    }
}

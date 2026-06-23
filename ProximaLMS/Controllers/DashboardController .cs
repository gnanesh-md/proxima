using Microsoft.AspNetCore.Mvc;
using ProximaLMS.Models;
using ProximaLMS.Filters;
using System.Net.Http.Json;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ProximaLMS.Controllers
{
    [RequireJwt]
    public class DashboardController : Controller
    {
        private readonly IConfiguration _configuration;
        private readonly IHttpClientFactory _httpClientFactory;

        public DashboardController(IConfiguration configuration, IHttpClientFactory httpClientFactory)
        {
            _configuration = configuration;
            _httpClientFactory = httpClientFactory;
        }

        private HttpClient CreateClient()
        {
            var client = _httpClientFactory.CreateClient();
            client.BaseAddress = new Uri(_configuration["ApiBaseUrl"]);
            return client;
        }

        public async Task<IActionResult> Index()
        {
            using var client = CreateClient();

            var vm = new DashboardViewModel();

            // ── STUDENTS ────────────────────────────────────────────────
            try
            {
                var studentRes = await client.GetAsync("/api/master/students");
                if (studentRes.IsSuccessStatusCode)
                {
                    var json = await studentRes.Content.ReadAsStringAsync();
                    var root = JObject.Parse(json);

                    // API returns { Success, Count, Data: [...] }
                    var dataArray = root["Data"] ?? root["data"];
                    if (dataArray != null && dataArray.Type == JTokenType.Array)
                    {
                        var students = dataArray.ToObject<List<StudentDashItem>>();
                        vm.TotalStudents = students?.Count ?? 0;
                        vm.RecentStudents = students?
                            .OrderByDescending(s => s.CreatedDate)
                            .Take(8)
                            .ToList() ?? new();
                    }
                }
            }
            catch { /* non-fatal */ }

            // ── MONTHLY CHART ────────────────────────────────────────────
            try
            {
                var chartRes = await client.GetAsync("/api/master/students/monthly");
                if (chartRes.IsSuccessStatusCode)
                {
                    var json = await chartRes.Content.ReadAsStringAsync();
                    var root = JObject.Parse(json);
                    var dataArr = root["Data"] ?? root["data"];
                    if (dataArr != null)
                    {
                        var chartData = dataArr.ToObject<List<MonthlyStudentChartDto>>();
                        vm.ChartMonths = chartData?.Select(c => c.Month).ToList() ?? new();
                        vm.ChartCounts = chartData?.Select(c => c.Count).ToList() ?? new();
                    }
                }
            }
            catch { /* non-fatal */ }

            // Fallback chart data if API returns nothing
            if (!vm.ChartMonths.Any())
            {
                vm.ChartMonths = new() { "Jan", "Feb", "Mar", "Apr", "May", "Jun" };
                vm.ChartCounts = new() { 0, 0, 0, 0, 0, vm.TotalStudents };
            }

            // ── COURSES ──────────────────────────────────────────────────
            try
            {
                var courseRes = await client.GetAsync("/api/course/list");
                if (courseRes.IsSuccessStatusCode)
                {
                    var json = await courseRes.Content.ReadAsStringAsync();
                    var courses = JsonConvert.DeserializeObject<List<object>>(json);
                    vm.TotalCourses = courses?.Count ?? 0;
                }
            }
            catch { /* non-fatal */ }

            // ── TUTORS ───────────────────────────────────────────────────
            try
            {
                var tutorRes = await client.GetAsync("/api/tutor/list");
                if (tutorRes.IsSuccessStatusCode)
                {
                    var json = await tutorRes.Content.ReadAsStringAsync();
                    var arr = JArray.Parse(json);
                    vm.TotalTutors = arr.Count;

                    // The Tutors card binds to Model.Tutors (TutorName/Email).
                    // SP returns FullName — map it (handle camel/Pascal casing).
                    vm.Tutors = arr.Select(t => new Tutor
                    {
                        TutorID   = (int?)(t["TutorID"] ?? t["tutorID"]) ?? 0,
                        TutorName = (string?)(t["FullName"] ?? t["fullName"]
                                              ?? t["TutorName"] ?? t["tutorName"]) ?? "",
                        Email     = (string?)(t["Email"] ?? t["email"]) ?? ""
                    }).ToList();
                }
            }
            catch { /* non-fatal */ }

            // ── EMPLOYEES ────────────────────────────────────────────────
            try
            {
                var empRes = await client.GetAsync("/api/employee/list");
                if (empRes.IsSuccessStatusCode)
                {
                    var json = await empRes.Content.ReadAsStringAsync();
                    var emps = JsonConvert.DeserializeObject<List<object>>(json);
                    vm.TotalEmployees = emps?.Count ?? 0;
                }
            }
            catch { /* non-fatal */ }

            // ── REVENUE ──────────────────────────────────────────────────
            // Total platform revenue comes from SP_Admin_PlatformHealth,
            // exposed via the admin overview endpoint (health.TotalRevenue).
            try
            {
                var ovRes = await client.GetAsync("/api/reports/admin/overview");
                if (ovRes.IsSuccessStatusCode)
                {
                    var json = await ovRes.Content.ReadAsStringAsync();
                    var health = JObject.Parse(json)["health"] as JObject;
                    decimal revenue = (decimal?)(health?["TotalRevenue"] ?? health?["totalRevenue"]) ?? 0m;
                    vm.TotalIncome = "₹" + revenue.ToString("N0");
                }
            }
            catch { /* non-fatal */ }
            if (string.IsNullOrWhiteSpace(vm.TotalIncome)) vm.TotalIncome = "₹0";

            // ── PROGRESS PERCENTAGES ─────────────────────────────────────
            int studentTarget = 5000;
            int courseTarget = 100;
            int tutorTarget = 500;
            int employeeTarget = 200;

            vm.AllStudentsPercent = vm.TotalStudents == 0 ? 1 : Math.Clamp((vm.TotalStudents * 100) / studentTarget, 1, 100);
            vm.AllCoursesPercent = vm.TotalCourses == 0 ? 1 : Math.Clamp((vm.TotalCourses * 100) / courseTarget, 1, 100);
            vm.AllTutorsPercent = vm.TotalTutors == 0 ? 1 : Math.Clamp((vm.TotalTutors * 100) / tutorTarget, 1, 100);
            vm.AllEmployeesPercent = vm.TotalEmployees == 0 ? 1 : Math.Clamp((vm.TotalEmployees * 100) / employeeTarget, 1, 100);

            ViewBag.AdminName = HttpContext.Session.GetString("FullName")
                             ?? HttpContext.Session.GetString("Email")
                             ?? "Admin";

            return View(vm);
        }
    }

    /// <summary>Matches the JSON shape returned by /api/master/students</summary>
    public class StudentDashItem
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Email { get; set; }
        public string Gender { get; set; }
        public string MobileNumber { get; set; }
        public bool IsActive { get; set; }
        public string CreatedDate { get; set; }
    }
}

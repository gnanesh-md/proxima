using System.Text.Json;

namespace ProximaLMS.Services
{
    public interface IPermissionService
    {
        bool CanView(string screenCode);
        bool CanCreate(string screenCode);
        bool CanEdit(string screenCode);
        bool CanDelete(string screenCode);
        bool HasAny(string screenCode);
    }

    public class PermissionService : IPermissionService
    {
        private readonly IHttpContextAccessor _http;
        private Dictionary<string, ScreenPerm>? _cache;

        public PermissionService(IHttpContextAccessor http)
        {
            _http = http;
        }

        public bool CanView(string screenCode) => Get(screenCode).CanView;
        public bool CanCreate(string screenCode) => Get(screenCode).CanCreate;
        public bool CanEdit(string screenCode) => Get(screenCode).CanEdit;
        public bool CanDelete(string screenCode) => Get(screenCode).CanDelete;
        public bool HasAny(string screenCode)
        {
            var p = Get(screenCode);
            return p.CanView || p.CanCreate || p.CanEdit || p.CanDelete;
        }

        private ScreenPerm Get(string screenCode)
        {
            EnsureCache();
            return _cache!.TryGetValue(screenCode.ToUpper(), out var p)
                ? p
                : new ScreenPerm(); // default: all false
        }

        private void EnsureCache()
        {
            if (_cache != null) return;
            _cache = new Dictionary<string, ScreenPerm>(StringComparer.OrdinalIgnoreCase);

            var session = _http.HttpContext?.Session;
            if (session == null) return;

            // ── IMPORTANT: ALL roles including Admin read from session permissions ──
            // Admin is NO LONGER auto-granted full access here.
            // Full access for Admin is granted at LOGIN TIME via LoadPermissionsIntoSession().
            // This means if Admin's DASHBOARD permission is unchecked in DB,
            // it will NOT appear in the sidebar.
            var json = session.GetString("Permissions");
            if (string.IsNullOrEmpty(json)) return;

            try
            {
                var list = JsonSerializer.Deserialize<List<ScreenPermJson>>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (list == null) return;

                foreach (var item in list)
                {
                    if (string.IsNullOrEmpty(item.ScreenCode)) continue;
                    _cache[item.ScreenCode.ToUpper()] = new ScreenPerm
                    {
                        CanView = item.CanView,
                        CanCreate = item.CanCreate,
                        CanEdit = item.CanEdit,
                        CanDelete = item.CanDelete
                    };
                }
            }
            catch { /* bad JSON = no permissions */ }
        }

        private class ScreenPermJson
        {
            public string ScreenCode { get; set; } = "";
            public bool CanView { get; set; }
            public bool CanCreate { get; set; }
            public bool CanEdit { get; set; }
            public bool CanDelete { get; set; }
        }
    }

    public class ScreenPerm
    {
        public bool CanView { get; set; }
        public bool CanCreate { get; set; }
        public bool CanEdit { get; set; }
        public bool CanDelete { get; set; }

        public static ScreenPerm Full() => new()
        {
            CanView = true,
            CanCreate = true,
            CanEdit = true,
            CanDelete = true
        };
    }

    public static class ScreenCodes
    {
        public const string Dashboard = "DASHBOARD";
        public const string StudentList = "STUDENT_LIST";
        public const string EmployeeList = "EMPLOYEE_LIST";
        public const string EmployeeCreate = "EMPLOYEE_CREATE";
        public const string TutorList = "TUTOR_LIST";
        public const string TutorCreate = "TUTOR_CREATE";
        public const string CourseList = "COURSE_LIST";
        public const string CourseCreate = "COURSE_CREATE";
        public const string CourseDetails = "COURSE_DETAILS";
        public const string RoleList = "ROLE_LIST";
        public const string RoleCreate = "ROLE_CREATE";
        public const string RolePermissions = "ROLE_PERMISSIONS";
        public const string RoleAssignUsers = "ROLE_ASSIGN_USERS";
        public const string Reports = "REPORTS";
        public const string COUPON_LIST = "COUPON_LIST";
        public const string COUPON_CREATE = "COUPON_CREATE";
        public const string COUPON_ANALYTICS = "COUPON_ANALYTICS";
        public const string REFERRAL_MANAGE = "REFERRAL_MANAGE";

        public static readonly string[] All =
        {
            Dashboard, StudentList, EmployeeList, EmployeeCreate,
            TutorList, TutorCreate, CourseList, CourseCreate,
            CourseDetails, RoleList, RoleCreate, RolePermissions,
            RoleAssignUsers, Reports,COUPON_LIST, COUPON_CREATE, COUPON_ANALYTICS, REFERRAL_MANAGE
        };
    }
}
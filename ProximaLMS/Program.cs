using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using ProximaLMS.Data;
using ProximaLMS.Middleware;   // ← ADD: for PermissionRefreshMiddleware
using ProximaLMS.Services;     // ← already added previously

var builder = WebApplication.CreateBuilder(args);

// =====================================================
// ✅ 1. Increase Upload Limits
// =====================================================
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 5L * 1024L * 1024L * 1024L;
    options.ValueLengthLimit = int.MaxValue;
    options.MemoryBufferThreshold = int.MaxValue;
});

builder.WebHost.ConfigureKestrel(serverOptions =>
{
    serverOptions.Limits.MaxRequestBodySize = 5L * 1024L * 1024L * 1024L;
    serverOptions.Limits.MaxRequestHeadersTotalSize = 1024 * 1024; // 1 MB — fixes HTTP 431
    serverOptions.Limits.MaxRequestHeaderCount = 200;
});

// =====================================================
// ✅ MVC + HttpClient
// =====================================================
builder.Services.AddControllersWithViews();
builder.Services.AddHttpClient();
builder.Services.AddScoped<ITokenRefreshService, TokenRefreshService>();
builder.Services.AddSingleton<IEmailService, SmtpEmailService>();
// =====================================================
// ✅ Authentication (Cookie)
// =====================================================
builder.Services.AddAuthentication("CookieAuth")
    .AddCookie("CookieAuth", options =>
    {
        options.LoginPath = "/Login/Index";
        options.AccessDeniedPath = "/Home/AccessDenied";
    });

// =====================================================
// ✅ Database Context (MySQL)
// =====================================================
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseMySql(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        new MySqlServerVersion(new Version(8, 0, 36))
    ));

// =====================================================
// ✅ Session
// =====================================================
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

// =====================================================
// ✅ HttpContext Accessor
// =====================================================
builder.Services.AddHttpContextAccessor();

// =====================================================
// ✅ Permission Service
// =====================================================
builder.Services.AddScoped<IPermissionService, PermissionService>();

var app = builder.Build();

// =====================================================
// 🔥 Error handling
// =====================================================
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler("/Home/Error");
    app.UseStatusCodePagesWithReExecute("/Home/StatusCode", "?code={0}");
    app.UseHsts();
}

// =====================================================
// 🔥 Middleware Pipeline
// =====================================================
app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseSession();
app.UseAuthentication();
app.UseAuthorization();

// =====================================================
// ✅ Permission Refresh Middleware
// MUST be after UseSession() and UseAuthentication()
// Refreshes permissions from API every 30 seconds
// so changes take effect without re-login
// =====================================================
app.UsePermissionRefresh();

// =====================================================
// 🔥 Routing
// =====================================================
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();

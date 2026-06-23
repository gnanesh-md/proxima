using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using ProximaLMSAPI.Filters;
using ProximaLMSAPI.Hubs;
using ProximaLMSAPI.Services;
using QuestPDF.Infrastructure;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Fix HTTP 431 — Chrome sends large headers after many sessions
builder.WebHost.ConfigureKestrel(serverOptions =>
{
    serverOptions.Limits.MaxRequestHeadersTotalSize = 1024 * 1024; // 1 MB
    serverOptions.Limits.MaxRequestHeaderCount = 200;
});

// =====================================================
// QuestPDF licence — MUST be set before any PDF is built.
// InvoiceService and CertificateService call GeneratePdf();
// without this line QuestPDF throws at the first render.
// =====================================================
QuestPDF.Settings.License = LicenseType.Community;

// =====================================================
// Controllers + global filters
// =====================================================
builder.Services.AddControllers(options =>
{
    // Audit every mutating request. The filter itself decides
    // which controllers to skip (Audit*, AuthToken, Auth).
    options.Filters.Add<AuditLogFilter>();
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    // Fix #1: prevents 500 if two endpoints have same verb+route
    c.ResolveConflictingActions(apiDescriptions => apiDescriptions.First());

    // Fix #2: prevents 500 if two DTOs share a class name in different namespaces
    c.CustomSchemaIds(type => type.FullName);
});

// =====================================================
// HttpClient + MemoryCache + SignalR
// =====================================================
builder.Services.AddHttpClient();
builder.Services.AddMemoryCache();
builder.Services.AddSignalR();

// =====================================================
// CORS — the MVC site runs on a different origin/port, so the
// browser-side notification bell (fetch calls + SignalR hub
// handshake) is cross-origin. Reflect the calling origin and
// allow credentials (required by the SignalR handshake).
// =====================================================
builder.Services.AddCors(options =>
{
    options.AddPolicy("ProximaCors", policy =>
        policy.SetIsOriginAllowed(_ => true)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials());
});

// =====================================================
// Application services (DI)
// =====================================================
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<IAuditService, AuditService>();

// Notification stack
builder.Services.AddScoped<ISmsSender, SmsSender>();
builder.Services.AddScoped<INotificationPush, NotificationPush>();
builder.Services.AddScoped<INotificationService, NotificationService>();

// Gamification engine (points / streak / badge + badge notifications)
builder.Services.AddScoped<IGamificationService, GamificationService>();

// PDF / document services
builder.Services.AddScoped<IInvoiceService, InvoiceService>();
builder.Services.AddScoped<ICertificateService, CertificateService>();
builder.Services.AddScoped<ICertificateIssuer, CertificateIssuer>();

// =====================================================
// JWT configuration
// =====================================================
var jwtSettings = builder.Configuration.GetSection("Jwt");
var jwtKey = jwtSettings["Key"]
                  ?? throw new InvalidOperationException("Jwt:Key is missing.");

// HARD STOP if the key looks like a placeholder or is too short.
// HS256 needs at least 32 bytes / 256 bits of entropy.
if (jwtKey.Length < 32
    || jwtKey.Contains("REPLACE", StringComparison.OrdinalIgnoreCase)
    || jwtKey.Contains("CHANGE", StringComparison.OrdinalIgnoreCase))
{
    throw new InvalidOperationException(
        "Jwt:Key is too short or looks like a placeholder. " +
        "Generate one with: openssl rand -base64 64");
}

var keyBytes = Encoding.UTF8.GetBytes(jwtKey);

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtSettings["Issuer"] ?? "ProximaLMS",
        ValidAudience = jwtSettings["Audience"] ?? "ProximaLMSUsers",
        IssuerSigningKey = new SymmetricSecurityKey(keyBytes),
        ClockSkew = TimeSpan.FromSeconds(30)
    };

    // Allow the SignalR client to pass the JWT via query string
    // (?access_token=...) since browsers cannot set Authorization
    // headers on the WebSocket handshake.
    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            var accessToken = context.Request.Query["access_token"];
            var path = context.HttpContext.Request.Path;
            if (!string.IsNullOrEmpty(accessToken) &&
                path.StartsWithSegments("/hubs"))
            {
                context.Token = accessToken;
            }
            return Task.CompletedTask;
        }
    };
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    // Serve swagger.json using the request's own scheme/host so it works
    // on both http://localhost:5167 and https://localhost:7044
    app.UseSwagger(c =>
    {
        c.PreSerializeFilters.Add((swaggerDoc, httpReq) =>
        {
            swaggerDoc.Servers = new System.Collections.Generic.List<Microsoft.OpenApi.Models.OpenApiServer>
            {
                new() { Url = $"{httpReq.Scheme}://{httpReq.Host.Value}" }
            };
        });
    });
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "ProximaLMS API v1");
    });
}

app.UseHttpsRedirection();
app.UseCors("ProximaCors");
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// Real-time notification hub — the bell client connects here.
app.MapHub<NotificationHub>("/hubs/notifications");

app.Run();
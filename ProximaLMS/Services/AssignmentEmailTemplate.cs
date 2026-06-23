// ============================================================
// ProximaLMS/Services/AssignmentEmailTemplate.cs
// ------------------------------------------------------------
// Builds all assignment-related email templates (subject + plain + HTML).
// All HTML uses a shared base layout: logo header → hero band → content → footer.
// ============================================================
using System;
using System.Web;

namespace ProximaLMS.Services
{
    public static class AssignmentEmailTemplate
    {
        public class Built
        {
            public string Subject   { get; set; } = "";
            public string PlainBody { get; set; } = "";
            public string HtmlBody  { get; set; } = "";
        }

        // ── shared base ────────────────────────────────────────
        private static string Base(string portalUrl, string heroIcon, string heroTitle, string heroSub,
                                    string contentHtml, string previewText = "")
        {
            var safePortal = HttpUtility.HtmlEncode(portalUrl ?? "#");
            var logoUrl    = $"{portalUrl?.TrimEnd('/')}/assets/images/emaillogo.png";
            var year       = DateTime.Now.Year;

            return $@"<!DOCTYPE html>
<html lang=""en"">
<head>
<meta charset=""UTF-8"" />
<meta name=""viewport"" content=""width=device-width,initial-scale=1"" />
<meta http-equiv=""X-UA-Compatible"" content=""IE=edge"" />
<title>{HttpUtility.HtmlEncode(heroTitle)}</title>
{(string.IsNullOrEmpty(previewText) ? "" : $@"<div style=""display:none;max-height:0;overflow:hidden;mso-hide:all;"">{HttpUtility.HtmlEncode(previewText)}&zwnj;&nbsp;&zwnj;&nbsp;&zwnj;&nbsp;&zwnj;&nbsp;&zwnj;&nbsp;</div>")}
</head>
<body style=""margin:0;padding:0;background:#f0ebf8;font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,'Helvetica Neue',Arial,sans-serif;color:#1a1523;"">

<table role=""presentation"" width=""100%"" cellspacing=""0"" cellpadding=""0"" border=""0"" style=""background:#f0ebf8;padding:32px 16px;"">
<tr><td align=""center"">

<!-- email card 600px -->
<table role=""presentation"" width=""600"" cellspacing=""0"" cellpadding=""0"" border=""0""
       style=""width:600px;max-width:100%;background:#ffffff;border-radius:20px;overflow:hidden;box-shadow:0 12px 40px rgba(76,24,100,0.14);"">

  <!-- ═══ LOGO HEADER ═══ -->
  <tr>
    <td style=""background:linear-gradient(135deg,#1e0438 0%,#4C1864 45%,#7B2CBF 100%);padding:22px 32px;text-align:center;"">
      <img src=""{logoUrl}"" alt=""ProximaLMS"" height=""38""
           style=""height:38px;width:auto;display:block;margin:0 auto;""
           onerror=""this.style.display='none'"" />
    </td>
  </tr>

  <!-- ═══ HERO BAND ═══ -->
  <tr>
    <td style=""background:linear-gradient(135deg,#4C1864 0%,#7B2CBF 60%,#9D4EDD 100%);padding:28px 32px 24px;text-align:center;"">
      <div style=""font-size:36px;margin-bottom:10px;line-height:1;"">{heroIcon}</div>
      <div style=""font-size:22px;font-weight:900;color:#ffffff;letter-spacing:-0.3px;margin-bottom:6px;"">{HttpUtility.HtmlEncode(heroTitle)}</div>
      <div style=""font-size:14px;color:rgba(255,255,255,0.82);"">{HttpUtility.HtmlEncode(heroSub)}</div>
    </td>
  </tr>

  <!-- ═══ CONTENT ═══ -->
  <tr>
    <td style=""padding:32px;"">
      {contentHtml}
    </td>
  </tr>

  <!-- ═══ DIVIDER ═══ -->
  <tr><td style=""padding:0 32px;""><div style=""height:1px;background:#ede9fe;""></div></td></tr>

  <!-- ═══ FOOTER ═══ -->
  <tr>
    <td style=""background:#faf8ff;padding:22px 32px;text-align:center;"">
      <div style=""margin-bottom:12px;"">
        <a href=""{safePortal}"" style=""display:inline-block;background:linear-gradient(135deg,#4C1864,#7B2CBF);color:#fff;text-decoration:none;font-weight:700;font-size:13px;padding:11px 24px;border-radius:10px;letter-spacing:0.02em;"">
          Go to ProximaLMS →
        </a>
      </div>
      <div style=""font-size:11px;color:#94a3b8;line-height:1.6;"">
        You're receiving this email because of activity on your ProximaLMS account.<br/>
        © {year} ProximaLMS · AI Driven Education Personalized For You
      </div>
    </td>
  </tr>

</table>
<!-- /email card -->

</td></tr>
</table>
</body>
</html>";
        }

        // ── helper: info row ───────────────────────────────────
        private static string InfoRow(string icon, string label, string value) =>
            $@"<tr>
              <td style=""padding:9px 0;border-bottom:1px solid #f3f0fa;"">
                <span style=""font-size:12px;color:#94a3b8;font-weight:600;width:110px;display:inline-block;"">{icon} {HttpUtility.HtmlEncode(label)}</span>
                <span style=""font-size:13px;font-weight:700;color:#1a1523;"">{HttpUtility.HtmlEncode(value)}</span>
              </td>
            </tr>";

        // ── helper: primary CTA button ─────────────────────────
        private static string Cta(string url, string label) =>
            $@"<div style=""text-align:center;margin-top:26px;"">
              <a href=""{HttpUtility.HtmlEncode(url)}""
                 style=""display:inline-block;background:linear-gradient(135deg,#4C1864,#7B2CBF);color:#fff;text-decoration:none;font-weight:800;font-size:15px;padding:14px 32px;border-radius:12px;letter-spacing:0.02em;box-shadow:0 6px 20px rgba(76,24,100,0.28);"">
                {HttpUtility.HtmlEncode(label)}
              </a>
            </div>";

        // ══════════════════════════════════════════════════════
        // 1. COURSE ASSIGNED
        // ══════════════════════════════════════════════════════
        public static Built CourseAssigned(
            string studentEmail,
            string courseTitle,
            DateTime? dueDate,
            bool isMandatory,
            string assignedBy,
            string portalUrl)
        {
            var safeTitle    = HttpUtility.HtmlEncode(courseTitle ?? "(course)");
            var safeAssigner = HttpUtility.HtmlEncode(string.IsNullOrEmpty(assignedBy) ? "your administrator" : assignedBy);
            var subjectTag   = isMandatory ? "[Mandatory] " : "";
            var subject      = $"{subjectTag}New course assigned: {courseTitle}";

            var dueLine = dueDate.HasValue ? $"Due by {dueDate.Value:dd MMM yyyy}" : "No due date";

            var plain = $@"Hello,

{(string.IsNullOrEmpty(assignedBy) ? "Your administrator" : assignedBy)} has assigned you a new course on ProximaLMS:

  Course  : {courseTitle}
  Due     : {dueLine}
  Type    : {(isMandatory ? "Mandatory" : "Optional")}

Open ProximaLMS to start: {portalUrl}

— ProximaLMS Team";

            var dueChip = dueDate.HasValue
                ? $@"<span style=""display:inline-block;background:#fef3c7;color:#92400e;font-weight:700;font-size:11px;padding:4px 12px;border-radius:999px;"">📅 Due {dueDate.Value:dd MMM yyyy}</span>"
                : $@"<span style=""display:inline-block;background:#f1f5f9;color:#475569;font-weight:600;font-size:11px;padding:4px 12px;border-radius:999px;"">📅 No due date</span>";

            var mandChip = isMandatory
                ? $@"<span style=""display:inline-block;background:#fee2e2;color:#b91c1c;font-weight:700;font-size:11px;padding:4px 12px;border-radius:999px;margin-left:6px;"">⚑ Mandatory</span>"
                : $@"<span style=""display:inline-block;background:#f0fdf4;color:#166534;font-weight:700;font-size:11px;padding:4px 12px;border-radius:999px;margin-left:6px;"">✓ Optional</span>";

            var content = $@"
<p style=""margin:0 0 18px;font-size:15px;color:#4b5563;line-height:1.6;"">
  Hi there! <strong style=""color:#1a1523;"">{safeAssigner}</strong> has assigned you a new course on ProximaLMS.
  Time to level up your skills!
</p>

<!-- course card -->
<div style=""background:linear-gradient(135deg,#f8f5ff,#ede9fe);border:1px solid #ddd6fe;border-radius:16px;padding:20px 22px;margin-bottom:22px;"">
  <div style=""font-size:10px;font-weight:800;text-transform:uppercase;letter-spacing:0.08em;color:#7B2CBF;margin-bottom:8px;"">Course Assigned</div>
  <div style=""font-size:18px;font-weight:900;color:#1a1523;margin-bottom:14px;line-height:1.3;"">{safeTitle}</div>
  <div style=""display:flex;gap:8px;flex-wrap:wrap;"">
    {dueChip}{mandChip}
  </div>
</div>

<!-- what to do -->
<div style=""background:#fff;border:1px solid #ede9fe;border-radius:12px;padding:16px 18px;margin-bottom:6px;"">
  <div style=""font-size:12px;font-weight:800;color:#4b5563;margin-bottom:10px;"">🚀 HOW TO GET STARTED</div>
  <table role=""presentation"" width=""100%"" cellspacing=""0"" cellpadding=""0"">
    <tr><td style=""padding:5px 0;font-size:13px;color:#4b5563;""><span style=""color:#7B2CBF;font-weight:700;margin-right:8px;"">1.</span>Log in to ProximaLMS</td></tr>
    <tr><td style=""padding:5px 0;font-size:13px;color:#4b5563;""><span style=""color:#7B2CBF;font-weight:700;margin-right:8px;"">2.</span>Navigate to <strong>My Courses</strong></td></tr>
    <tr><td style=""padding:5px 0;font-size:13px;color:#4b5563;""><span style=""color:#7B2CBF;font-weight:700;margin-right:8px;"">3.</span>Click <strong>{safeTitle}</strong> and hit Start</td></tr>
  </table>
</div>
{Cta(portalUrl, "Start Learning Now")}
<p style=""margin:18px 0 0;font-size:12px;color:#94a3b8;text-align:center;"">
  Questions? Reply to this email or contact your administrator.
</p>";

            var html = Base(portalUrl, "📚", "New Course Assigned!", safeAssigner + " has a new course for you", content,
                            $"You've been assigned: {courseTitle}");

            return new Built { Subject = subject, PlainBody = plain, HtmlBody = html };
        }

        // ══════════════════════════════════════════════════════
        // 2. COURSE COMPLETION CONGRATULATIONS
        // ══════════════════════════════════════════════════════
        public static Built CourseCompleted(string courseName, string studentName, string portalUrl)
        {
            var safeTitle = HttpUtility.HtmlEncode(courseName);
            var safeName  = HttpUtility.HtmlEncode(studentName);
            var subject   = $"🎉 Congratulations! You completed: {courseName}";

            var plain = $@"Hi {studentName},

Congratulations! You have successfully completed the course:
  {courseName}

Your certificate will be available in your ProximaLMS account shortly.
Visit: {portalUrl}

Well done!
— ProximaLMS Team";

            var content = $@"
<p style=""margin:0 0 20px;font-size:15px;color:#4b5563;line-height:1.6;"">
  Amazing work, <strong style=""color:#1a1523;"">{safeName}</strong>! 🎊 You've successfully completed the course below.
  Your certificate will be ready in your account shortly.
</p>

<div style=""background:linear-gradient(135deg,#f0fdf4,#dcfce7);border:1px solid #bbf7d0;border-radius:16px;padding:20px 22px;margin-bottom:22px;text-align:center;"">
  <div style=""font-size:40px;margin-bottom:10px;"">🏆</div>
  <div style=""font-size:10px;font-weight:800;text-transform:uppercase;letter-spacing:0.08em;color:#16a34a;margin-bottom:6px;"">Course Completed</div>
  <div style=""font-size:18px;font-weight:900;color:#1a1523;line-height:1.3;"">{safeTitle}</div>
</div>

<div style=""display:flex;gap:12px;margin-bottom:6px;"">
  <div style=""flex:1;background:#f8f5ff;border:1px solid #ede9fe;border-radius:12px;padding:14px;text-align:center;"">
    <div style=""font-size:24px;margin-bottom:4px;"">📜</div>
    <div style=""font-size:12px;font-weight:700;color:#4C1864;"">Certificate Ready</div>
    <div style=""font-size:11px;color:#94a3b8;margin-top:3px;"">Download from My Certificates</div>
  </div>
  <div style=""flex:1;background:#f8f5ff;border:1px solid #ede9fe;border-radius:12px;padding:14px;text-align:center;"">
    <div style=""font-size:24px;margin-bottom:4px;"">⭐</div>
    <div style=""font-size:12px;font-weight:700;color:#4C1864;"">Points Earned</div>
    <div style=""font-size:11px;color:#94a3b8;margin-top:3px;"">Check your rewards dashboard</div>
  </div>
</div>
{Cta(portalUrl, "View My Certificate")}";

            return new Built { Subject = subject, PlainBody = plain,
                HtmlBody = Base(portalUrl, "🎓", "Course Completed!", "You did it — keep going!", content,
                    $"Congrats! You completed {courseName}") };
        }
    }
}

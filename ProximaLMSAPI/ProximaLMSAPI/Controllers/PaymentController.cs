using Dapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using MySql.Data.MySqlClient;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ProximaLMSAPI.Services;
using System.Data;
using System.IO;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;

namespace ProximaLMSAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class PaymentController : ControllerBase
    {
        private readonly IConfiguration _config;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<PaymentController> _logger;
        private readonly IServiceScopeFactory _scopeFactory;

        public PaymentController(
            IConfiguration config,
            IHttpClientFactory httpClientFactory,
            ILogger<PaymentController> logger,
            IServiceScopeFactory scopeFactory)
        {
            _config = config;
            _httpClientFactory = httpClientFactory;
            _logger = logger;
            _scopeFactory = scopeFactory;
        }

        private IDbConnection CreateConn()
            => new MySqlConnection(_config.GetConnectionString("ConnectionString"));


        // ════════════════════════════════════════════════════════
        // POST  api/payment/create-order            (PAID courses)
        // Body: { StudentID, CourseID, CouponCode? }
        // ════════════════════════════════════════════════════════
        [HttpPost("create-order")]
        public async Task<IActionResult> CreateOrder([FromBody] CreateOrderApiRequest req)
        {
            if (req == null || req.StudentID <= 0 || req.CourseID <= 0)
                return BadRequest(new { success = false, message = "Invalid request." });

            try
            {
                using var conn = CreateConn();

                // ── 1. fetch course ────────────────────────────
                var course = await conn.QuerySingleOrDefaultAsync(@"
                    SELECT CourseID, CourseTitle, Price
                      FROM TblCourseMaster
                     WHERE CourseID = @cid AND IsActive = 1;",
                    new { cid = req.CourseID });

                if (course == null)
                    return NotFound(new { success = false, message = "Course not found." });

                decimal originalPrice = Convert.ToDecimal(course.Price);
                if (originalPrice <= 0)
                    return BadRequest(new
                    {
                        success = false,
                        message = "This course is free — use the Enroll Free button."
                    });

                // ── 2. block double-purchase ────────────────────
                var already = await conn.ExecuteScalarAsync<int>(@"
                    SELECT COUNT(*) FROM TblStudentCourses
                     WHERE StudentID = @s AND CourseID = @c AND IsActive = 1;",
                    new { s = req.StudentID, c = req.CourseID });

                if (already > 0)
                    return BadRequest(new { success = false, message = "You already own this course." });

                // ── 3. validate coupon (server-side) ───────────
                decimal discountAmount = 0m;
                decimal finalAmount = originalPrice;
                int couponId = 0;
                string couponMessage = "";

                if (!string.IsNullOrWhiteSpace(req.CouponCode))
                {
                    var cvResult = await ValidateCouponAsync(
                        conn,
                        req.CouponCode.Trim().ToUpper(),
                        req.StudentID,
                        req.CourseID,
                        originalPrice);

                    if (cvResult.IsValid)
                    {
                        couponId = cvResult.CouponId;
                        discountAmount = cvResult.DiscountAmount;
                        finalAmount = cvResult.FinalAmount;
                        couponMessage = cvResult.Message;
                    }
                    else
                    {
                        return BadRequest(new { success = false, message = cvResult.Message });
                    }
                }

                // ── 4. Razorpay keys ───────────────────────────
                string keyId = _config["Razorpay:KeyId"];
                string keySecret = _config["Razorpay:KeySecret"];

                if (string.IsNullOrWhiteSpace(keyId) || string.IsNullOrWhiteSpace(keySecret))
                    return StatusCode(500, new { success = false, message = "Razorpay keys not configured on the server." });

                // ── 5. create Razorpay order ───────────────────
                long amountPaise = (long)Math.Round(finalAmount * 100m);
                amountPaise = Math.Max(amountPaise, 100L);

                string receipt = $"rcpt_{req.StudentID}_{req.CourseID}_{DateTime.UtcNow:yyyyMMddHHmmss}";

                var orderPayload = new
                {
                    amount = amountPaise,
                    currency = "INR",
                    receipt = receipt,
                    notes = new
                    {
                        StudentID = req.StudentID.ToString(),
                        CourseID = req.CourseID.ToString(),
                        Title = (string)course.CourseTitle,
                        CouponCode = req.CouponCode ?? ""
                    }
                };

                var http = _httpClientFactory.CreateClient();
                http.BaseAddress = new Uri("https://api.razorpay.com/");
                http.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Basic",
                        Convert.ToBase64String(Encoding.UTF8.GetBytes($"{keyId}:{keySecret}")));

                var body = new StringContent(JsonConvert.SerializeObject(orderPayload), Encoding.UTF8, "application/json");
                var rzResp = await http.PostAsync("v1/orders", body);
                var rzJson = await rzResp.Content.ReadAsStringAsync();

                if (!rzResp.IsSuccessStatusCode)
                {
                    _logger.LogError("Razorpay create-order failed: {Status} {Body}", rzResp.StatusCode, rzJson);
                    return StatusCode(500, new
                    {
                        success = false,
                        message = "Could not create payment order.",
                        details = rzJson
                    });
                }

                var rz = JObject.Parse(rzJson);
                string orderId = rz["id"]?.ToString() ?? "";

                if (string.IsNullOrEmpty(orderId))
                    return StatusCode(500, new { success = false, message = "Razorpay returned no order id." });

                // ── 6. log PENDING order (with discount info) ──
                await conn.ExecuteAsync("SP_Payment_CreateOrder",
                    new
                    {
                        p_StudentID = req.StudentID,
                        p_CourseID = req.CourseID,
                        p_OrderID = orderId,
                        p_Amount = originalPrice,
                        p_Receipt = receipt,
                        p_CouponID = couponId,
                        p_CouponCode = req.CouponCode?.Trim().ToUpper() ?? "",
                        p_DiscountAmount = discountAmount,
                        p_FinalAmount = finalAmount
                    },
                    commandType: CommandType.StoredProcedure);

                return Ok(new
                {
                    success = true,
                    orderId = orderId,
                    amount = amountPaise,
                    currency = "INR",
                    keyId = keyId,
                    courseID = req.CourseID,
                    courseName = (string)course.CourseTitle,
                    originalAmount = originalPrice,
                    discountAmount = discountAmount,
                    finalAmount = finalAmount,
                    couponId = couponId,
                    couponMessage = couponMessage
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating Razorpay order");
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }


        // ════════════════════════════════════════════════════════
        // POST  api/payment/verify                   (PAID courses)
        // ════════════════════════════════════════════════════════
        [HttpPost("verify")]
        public async Task<IActionResult> VerifyPayment([FromBody] VerifyOrderApiRequest req)
        {
            if (req == null
                || req.StudentID <= 0
                || req.CourseID <= 0
                || string.IsNullOrEmpty(req.RazorpayOrderId)
                || string.IsNullOrEmpty(req.RazorpayPaymentId)
                || string.IsNullOrEmpty(req.RazorpaySignature))
            {
                return BadRequest(new { success = false, message = "Missing Razorpay fields." });
            }

            try
            {
                string keySecret = _config["Razorpay:KeySecret"];
                if (string.IsNullOrWhiteSpace(keySecret))
                    return StatusCode(500, new { success = false, message = "Razorpay secret not configured." });

                // ── 1. HMAC-SHA256 verification ─────────────────
                string data = $"{req.RazorpayOrderId}|{req.RazorpayPaymentId}";
                string expected;
                using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(keySecret)))
                {
                    var bytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
                    var sb = new StringBuilder(bytes.Length * 2);
                    foreach (var b in bytes) sb.Append(b.ToString("x2"));
                    expected = sb.ToString();
                }

                if (!string.Equals(expected, req.RazorpaySignature, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning("Bad Razorpay signature. Order={Order}", req.RazorpayOrderId);
                    return BadRequest(new { success = false, message = "Invalid payment signature." });
                }

                // ── 2. enroll student (transactional SP) ────────
                using var conn = CreateConn();
                conn.Open();

                var p = new DynamicParameters();
                p.Add("p_StudentID", req.StudentID);
                p.Add("p_CourseID", req.CourseID);
                p.Add("p_OrderID", req.RazorpayOrderId);
                p.Add("p_PaymentID", req.RazorpayPaymentId);
                p.Add("p_Signature", req.RazorpaySignature);
                p.Add("p_AssignedBy", req.AssignedBy ?? "Student");
                p.Add("p_DiscountAmount", req.DiscountAmount);
                p.Add("p_CouponID", req.CouponID);
                p.Add("p_ResultCode", dbType: DbType.Int32, direction: ParameterDirection.Output);
                p.Add("p_Message", dbType: DbType.String, size: 500, direction: ParameterDirection.Output);

                await conn.ExecuteAsync("SP_Payment_MarkPaidAndAssign", p,
                    commandType: CommandType.StoredProcedure);

                int code = p.Get<int>("p_ResultCode");
                string msg = p.Get<string>("p_Message") ?? "";

                if (code != 1)
                    return BadRequest(new { success = false, message = msg });

                // Only run the one-time side-effects when THIS call actually
                // enrolled the student. If the Razorpay webhook already
                // processed this order, the SP returns "already recorded" and
                // we must not double-record coupon usage or re-send emails.
                bool freshlyEnrolled =
                    msg.IndexOf("granted", StringComparison.OrdinalIgnoreCase) >= 0;

                if (freshlyEnrolled)
                {
                // ── 3. record coupon usage (fire & forget) ──────
                if (req.CouponID > 0)
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            using var c2 = CreateConn();
                            c2.Open();
                            var cp = new DynamicParameters();
                            cp.Add("p_CouponID", req.CouponID);
                            cp.Add("p_UserID", req.StudentID);
                            cp.Add("p_CourseID", req.CourseID);
                            cp.Add("p_OrderID", req.RazorpayOrderId);
                            cp.Add("p_OrderAmount", req.OriginalAmount);
                            cp.Add("p_DiscountApplied", req.DiscountAmount);
                            cp.Add("p_ResultCode", dbType: DbType.Int32, direction: ParameterDirection.Output);
                            cp.Add("p_Message", dbType: DbType.String, size: 500, direction: ParameterDirection.Output);
                            await c2.ExecuteAsync("SP_Coupon_RecordUsage", cp,
                                commandType: CommandType.StoredProcedure);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "SP_Coupon_RecordUsage failed (non-fatal). CouponID={Id}", req.CouponID);
                        }
                    });
                }

                // ── 4. grant referral rewards (fire & forget) ───
                _ = Task.Run(async () =>
                {
                    try
                    {
                        using var c3 = CreateConn();
                        c3.Open();
                        var rp = new DynamicParameters();
                        rp.Add("p_RefereeUserID", req.StudentID);
                        rp.Add("p_ResultCode", dbType: DbType.Int32, direction: ParameterDirection.Output);
                        rp.Add("p_Message", dbType: DbType.String, size: 500, direction: ParameterDirection.Output);
                        await c3.ExecuteAsync("SP_Referral_GrantRewards", rp,
                            commandType: CommandType.StoredProcedure);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "SP_Referral_GrantRewards failed (non-fatal). StudentID={Id}", req.StudentID);
                    }
                });

                // ── 5. invoice + enrollment/payment notifications ─
                // Runs in its own DI scope (NotificationService is scoped) so it
                // is safe after the response returns. SP_Invoice_Generate is
                // idempotent (unique on OrderID); we generate it here first so the
                // PAYMENT_SUCCESS email/SMS can carry the real invoice number.
                decimal amountPaid = req.OriginalAmount - req.DiscountAmount;
                if (amountPaid < 0) amountPaid = 0;

                FireEnrollmentNotifications(
                    req.StudentID, req.CourseID, amountPaid,
                    isPaid: true, razorpayOrderId: req.RazorpayOrderId);
                }

                return Ok(new { success = true, message = msg });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error verifying payment");
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }


        // ════════════════════════════════════════════════════════
        // POST  api/payment/webhook        (Razorpay server-to-server)
        // ── Reliable enrollment fallback ──
        // The browser-side Razorpay `handler` callback frequently never
        // fires for UPI (the learner pays in a separate app, the tab loses
        // focus, or the network blips), so /verify is never called and the
        // student is left unenrolled despite a successful payment. This
        // webhook enrolls server-side, independent of the browser.
        //
        // Setup: in the Razorpay Dashboard add a webhook pointing at
        //   {API}/api/payment/webhook  for events `payment.captured` and
        //   `order.paid`, with a secret that matches Razorpay:WebhookSecret.
        // The enrollment SP is idempotent, so it is safe even when the
        // browser handler ALSO succeeds.
        // ════════════════════════════════════════════════════════
        [HttpPost("webhook")]
        public async Task<IActionResult> RazorpayWebhook()
        {
            // 1. Read the RAW body — the signature is computed over the exact bytes.
            string rawBody;
            using (var reader = new StreamReader(Request.Body, Encoding.UTF8))
                rawBody = await reader.ReadToEndAsync();

            string webhookSecret = _config["Razorpay:WebhookSecret"];
            if (string.IsNullOrWhiteSpace(webhookSecret))
            {
                _logger.LogError("Razorpay webhook secret (Razorpay:WebhookSecret) not configured.");
                // 500 so Razorpay retries after the secret is configured.
                return StatusCode(500, new { success = false, message = "Webhook not configured." });
            }

            // 2. Verify X-Razorpay-Signature = HMAC-SHA256(rawBody, secret) hex.
            string sentSig = Request.Headers["X-Razorpay-Signature"].ToString();
            string expectedSig;
            using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(webhookSecret)))
            {
                var bytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(rawBody));
                var sb = new StringBuilder(bytes.Length * 2);
                foreach (var b in bytes) sb.Append(b.ToString("x2"));
                expectedSig = sb.ToString();
            }

            if (string.IsNullOrEmpty(sentSig) ||
                !string.Equals(expectedSig, sentSig, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Razorpay webhook: invalid signature.");
                return Unauthorized(new { success = false, message = "Invalid signature." });
            }

            try
            {
                var evt = JObject.Parse(rawBody);
                string eventType = evt["event"]?.ToString() ?? "";

                // Act only on terminal "money received" events.
                if (eventType != "payment.captured" && eventType != "order.paid")
                    return Ok(new { success = true, message = "Ignored event." });

                var payment = evt["payload"]?["payment"]?["entity"];
                var order = evt["payload"]?["order"]?["entity"];

                string orderId = payment?["order_id"]?.ToString()
                                 ?? order?["id"]?.ToString() ?? "";
                string paymentId = payment?["id"]?.ToString() ?? "";

                if (string.IsNullOrEmpty(orderId))
                    return Ok(new { success = true, message = "No order id in payload." });

                using var conn = CreateConn();
                conn.Open();

                // Resolve the PENDING order we logged at create-order time.
                var ord = await conn.QuerySingleOrDefaultAsync(@"
                    SELECT StudentID, CourseID, Status,
                           IFNULL(CouponID,0)             AS CouponID,
                           IFNULL(DiscountAmount,0)       AS DiscountAmount,
                           IFNULL(OriginalAmount, Amount) AS OriginalAmount
                      FROM TblPaymentOrders
                     WHERE OrderID = @oid
                     LIMIT 1;", new { oid = orderId });

                if (ord == null)
                {
                    _logger.LogWarning("Razorpay webhook: order {Order} not found.", orderId);
                    return Ok(new { success = true, message = "Order not found." });
                }

                // Already enrolled by the browser handler — nothing to do.
                if (string.Equals((string)ord.Status, "PAID", StringComparison.OrdinalIgnoreCase))
                    return Ok(new { success = true, message = "Already processed." });

                int studentId = Convert.ToInt32(ord.StudentID);
                int courseId = Convert.ToInt32(ord.CourseID);
                int couponId = Convert.ToInt32(ord.CouponID);
                decimal discount = Convert.ToDecimal(ord.DiscountAmount);
                decimal original = Convert.ToDecimal(ord.OriginalAmount);

                var p = new DynamicParameters();
                p.Add("p_StudentID", studentId);
                p.Add("p_CourseID", courseId);
                p.Add("p_OrderID", orderId);
                p.Add("p_PaymentID", paymentId);
                p.Add("p_Signature", sentSig);   // store the webhook signature
                p.Add("p_AssignedBy", "Razorpay Webhook");
                p.Add("p_DiscountAmount", discount);
                p.Add("p_CouponID", couponId);
                p.Add("p_ResultCode", dbType: DbType.Int32, direction: ParameterDirection.Output);
                p.Add("p_Message", dbType: DbType.String, size: 500, direction: ParameterDirection.Output);

                await conn.ExecuteAsync("SP_Payment_MarkPaidAndAssign", p,
                    commandType: CommandType.StoredProcedure);

                int code = p.Get<int>("p_ResultCode");
                string msg = p.Get<string>("p_Message") ?? "";

                // Fire invoice + notifications only when WE actually enrolled
                // (not when a racing /verify already marked it "already recorded").
                bool freshlyEnrolled =
                    code == 1 && msg.IndexOf("granted", StringComparison.OrdinalIgnoreCase) >= 0;

                if (freshlyEnrolled)
                {
                    decimal amountPaid = original - discount;
                    if (amountPaid < 0) amountPaid = 0;
                    FireEnrollmentNotifications(studentId, courseId, amountPaid,
                        isPaid: true, razorpayOrderId: orderId);
                }

                return Ok(new { success = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Razorpay webhook processing failed.");
                // 500 → Razorpay will retry the delivery.
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }


        // ════════════════════════════════════════════════════════
        // POST  api/payment/enroll-free               (FREE courses)
        // Body: { StudentID, CourseID, AssignedBy }
        // ════════════════════════════════════════════════════════
        [HttpPost("enroll-free")]
        public async Task<IActionResult> EnrollFree([FromBody] EnrollFreeApiRequest req)
        {
            if (req == null || req.StudentID <= 0 || req.CourseID <= 0)
                return BadRequest(new { success = false, message = "Invalid request." });

            try
            {
                using var conn = CreateConn();

                var course = await conn.QuerySingleOrDefaultAsync(@"
                    SELECT CourseID, CourseTitle, Price
                      FROM TblCourseMaster
                     WHERE CourseID = @cid AND IsActive = 1;",
                    new { cid = req.CourseID });

                if (course == null)
                    return NotFound(new { success = false, message = "Course not found." });

                decimal price = Convert.ToDecimal(course.Price);
                if (price > 0)
                    return BadRequest(new
                    {
                        success = false,
                        message = "This is a paid course — please use the Buy button."
                    });

                var already = await conn.ExecuteScalarAsync<int>(@"
                    SELECT COUNT(*) FROM TblStudentCourses
                     WHERE StudentID = @s AND CourseID = @c AND IsActive = 1;",
                    new { s = req.StudentID, c = req.CourseID });

                if (already > 0)
                    return BadRequest(new { success = false, message = "You're already enrolled in this course." });

                conn.Open();

                var p = new DynamicParameters();
                p.Add("p_StudentID", req.StudentID);
                p.Add("p_CourseID", req.CourseID);
                p.Add("p_AssignedBy", req.AssignedBy ?? "Student (Free)");
                // SP_Course_AssignToStudent also declares these IN params; a free
                // enrolment has no due date / mandatory flag / note, so pass neutrals.
                p.Add("p_DueDate", null, dbType: DbType.Date);
                p.Add("p_IsMandatory", 0, dbType: DbType.Byte);
                p.Add("p_Note", null, dbType: DbType.String, size: 500);
                p.Add("p_ResultCode", dbType: DbType.Int32, direction: ParameterDirection.Output);
                p.Add("p_Message", dbType: DbType.String, size: 500, direction: ParameterDirection.Output);

                await conn.ExecuteAsync("SP_Course_AssignToStudent", p,
                    commandType: CommandType.StoredProcedure);

                int code = p.Get<int>("p_ResultCode");
                string msg = p.Get<string>("p_Message") ?? "";

                if (code != 1)
                    return BadRequest(new { success = false, message = msg });

                // free enrolment is still an enrolment — notify (no payment/invoice)
                FireEnrollmentNotifications(
                    req.StudentID, req.CourseID, 0m,
                    isPaid: false, razorpayOrderId: "");

                return Ok(new { success = true, message = "🎉 Enrolled! The course has been added to your library." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error enrolling free");
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }


        // ────────────────────────────────────────────────────────
        // Fire enrollment (and, for paid courses, payment) notifications
        // in the background using a fresh DI scope. Never blocks the
        // caller and never throws into the request pipeline.
        // ────────────────────────────────────────────────────────
        private void FireEnrollmentNotifications(
            int studentId, int courseId, decimal amountPaid, bool isPaid, string razorpayOrderId)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var notifier = scope.ServiceProvider.GetRequiredService<INotificationService>();
                    var gamif = scope.ServiceProvider.GetRequiredService<IGamificationService>();

                    string courseTitle = "your course";
                    string userName = "Learner";
                    string invoiceNo = "";

                    using (var c = CreateConn())
                    {
                        c.Open();

                        var ct = await c.ExecuteScalarAsync<string>(
                            "SELECT CourseTitle FROM TblCourseMaster WHERE CourseID = @c",
                            new { c = courseId });
                        if (!string.IsNullOrWhiteSpace(ct)) courseTitle = ct;

                        var un = await c.ExecuteScalarAsync<string>(
                            "SELECT Name FROM TblUserMasters WHERE ID = @s",
                            new { s = studentId });
                        if (!string.IsNullOrWhiteSpace(un)) userName = un;

                        // generate the GST invoice first (idempotent) so the
                        // PAYMENT_SUCCESS message can include the real number.
                        if (isPaid && !string.IsNullOrEmpty(razorpayOrderId))
                        {
                            try
                            {
                                var ip = new DynamicParameters();
                                ip.Add("p_OrderID", razorpayOrderId);
                                ip.Add("p_InvoiceID", dbType: DbType.Int32, direction: ParameterDirection.Output);
                                ip.Add("p_InvoiceNo", dbType: DbType.String, size: 40, direction: ParameterDirection.Output);
                                await c.ExecuteAsync("SP_Invoice_Generate", ip,
                                    commandType: CommandType.StoredProcedure);
                                invoiceNo = ip.Get<string>("p_InvoiceNo") ?? "";
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "SP_Invoice_Generate failed (non-fatal). Order={Id}", razorpayOrderId);
                            }
                        }
                    }

                    string baseUrl = (_config["PublicBaseUrl"] ?? _config["ApiBaseUrl"] ?? "").TrimEnd('/');
                    string courseUrl = string.IsNullOrEmpty(baseUrl) ? "#" : $"{baseUrl}/Courses/Details/{courseId}";
                    string year = DateTime.UtcNow.Year.ToString();

                    // ── enrollment confirmation (in-app + email) ──
                    await notifier.NotifyAsync(new NotifyRequest
                    {
                        UserID = studentId,
                        EventCode = "ENROLLMENT",
                        Title = "You're enrolled!",
                        Body = $"You now have access to <strong>{courseTitle}</strong>.",
                        LinkUrl = courseUrl,
                        Icon = "fa-solid fa-graduation-cap",
                        SendInApp = true,
                        SendEmail = true,
                        EmailTemplateCode = "COURSE_ENROLLMENT",
                        Vars = new Dictionary<string, string>
                        {
                            ["UserName"] = userName,
                            ["CourseName"] = courseTitle,
                            ["CourseUrl"] = courseUrl,
                            ["Year"] = year
                        }
                    });

                    // ── payment receipt (in-app + email + SMS) ──
                    if (isPaid)
                    {
                        await notifier.NotifyAsync(new NotifyRequest
                        {
                            UserID = studentId,
                            EventCode = "PAYMENT",
                            Title = "Payment received",
                            Body = $"We received your payment for <strong>{courseTitle}</strong>." +
                                                (string.IsNullOrEmpty(invoiceNo) ? "" : $" Invoice {invoiceNo}."),
                            LinkUrl = string.IsNullOrEmpty(baseUrl) ? "#" : $"{baseUrl}/PaymentHistory",
                            Icon = "fa-solid fa-receipt",
                            SendInApp = true,
                            SendEmail = true,
                            SendSms = true,
                            EmailTemplateCode = "PAYMENT_SUCCESS",
                            SmsText = $"ProximaLMS: Payment received for {courseTitle}." +
                                                (string.IsNullOrEmpty(invoiceNo) ? "" : $" Invoice {invoiceNo}."),
                            Vars = new Dictionary<string, string>
                            {
                                ["Name"] = userName,
                                ["UserName"] = userName,
                                ["CourseTitle"] = courseTitle,
                                ["Amount"] = amountPaid.ToString("0.00"),
                                ["InvoiceNo"] = invoiceNo
                            }
                        });
                    }

                    // ── gamification: enrollment points + badges ──
                    // dedup-safe per (student, ENROLL_COURSE, courseId)
                    await gamif.AwardAsync(studentId, "ENROLL_COURSE",
                        "Course", courseId.ToString(), "Course enrollment");
                    await gamif.EvaluateAndNotifyBadgesAsync(studentId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Enrollment notifications failed (non-fatal). Student={S} Course={C}",
                        studentId, courseId);
                }
            });
        }


        // ────────────────────────────────────────────────────────
        // PRIVATE HELPER — server-side coupon validation
        // ────────────────────────────────────────────────────────
        private async Task<CouponValidationResult> ValidateCouponAsync(
            IDbConnection conn,
            string code,
            int userId,
            int courseId,
            decimal orderAmount)
        {
            var p = new DynamicParameters();
            p.Add("p_Code", code);
            p.Add("p_UserID", userId);
            p.Add("p_CourseID", courseId);
            p.Add("p_OrderAmount", orderAmount);
            p.Add("p_CouponID", dbType: DbType.Int32, direction: ParameterDirection.Output);
            p.Add("p_DiscountType", dbType: DbType.String, size: 10, direction: ParameterDirection.Output);
            p.Add("p_DiscountValue", dbType: DbType.Decimal, direction: ParameterDirection.Output);
            p.Add("p_DiscountAmount", dbType: DbType.Decimal, direction: ParameterDirection.Output);
            p.Add("p_FinalAmount", dbType: DbType.Decimal, direction: ParameterDirection.Output);
            p.Add("p_ResultCode", dbType: DbType.Int32, direction: ParameterDirection.Output);
            p.Add("p_Message", dbType: DbType.String, size: 500, direction: ParameterDirection.Output);

            await conn.ExecuteAsync("SP_Coupon_Validate", p, commandType: CommandType.StoredProcedure);

            int resultCode = p.Get<int>("p_ResultCode");
            return new CouponValidationResult
            {
                IsValid = resultCode == 1,
                CouponId = p.Get<int?>("p_CouponID") ?? 0,
                DiscountAmount = p.Get<decimal?>("p_DiscountAmount") ?? 0m,
                FinalAmount = p.Get<decimal?>("p_FinalAmount") ?? orderAmount,
                Message = p.Get<string>("p_Message") ?? ""
            };
        }

        private sealed class CouponValidationResult
        {
            public bool IsValid { get; set; }
            public int CouponId { get; set; }
            public decimal DiscountAmount { get; set; }
            public decimal FinalAmount { get; set; }
            public string Message { get; set; } = "";
        }
    }


    // ── Request DTOs ──────────────────────────────────────────
    public class CreateOrderApiRequest
    {
        public int StudentID { get; set; }
        public int CourseID { get; set; }
        public string? CouponCode { get; set; }
    }

    public class VerifyOrderApiRequest
    {
        public int StudentID { get; set; }
        public int CourseID { get; set; }
        public string RazorpayOrderId { get; set; } = "";
        public string RazorpayPaymentId { get; set; } = "";
        public string RazorpaySignature { get; set; } = "";
        public string AssignedBy { get; set; } = "Student";
        public int CouponID { get; set; }
        public decimal DiscountAmount { get; set; }
        public decimal OriginalAmount { get; set; }
    }

    public class EnrollFreeApiRequest
    {
        public int StudentID { get; set; }
        public int CourseID { get; set; }
        public string AssignedBy { get; set; } = "Student";
    }
}
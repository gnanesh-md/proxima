using Dapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using MySql.Data.MySqlClient;
using ProximaLMSAPI.Services;
using System.Data;

namespace ProximaLMSAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class CourseController : ControllerBase
    {
        private readonly IConfiguration _config;
        private readonly ILogger<CourseController> _logger;
        private readonly IServiceScopeFactory _scopeFactory;

        public CourseController(IConfiguration config, ILogger<CourseController> logger,
                                IServiceScopeFactory scopeFactory)
        {
            _config = config;
            _logger = logger;
            _scopeFactory = scopeFactory;
        }

        // ✅ Save Course (handles both insert and update)
        [HttpPost("savecourse")]
        public IActionResult SaveCourse([FromBody] CourseRequest req)
        {
            try
            {
                // capture the existing price BEFORE saving so we can detect a
                // price drop for wishlisted users (only relevant on update).
                bool isUpdate = req.CourseID > 0;
                decimal oldPrice = 0m;
                if (isUpdate)
                {
                    using var pre = new MySqlConnection(_config.GetConnectionString("ConnectionString"));
                    using var preCmd = new MySqlCommand(
                        "SELECT Price FROM TblCourseMaster WHERE CourseID=@c", pre);
                    preCmd.Parameters.AddWithValue("@c", req.CourseID);
                    pre.Open();
                    var pv = preCmd.ExecuteScalar();
                    if (pv != null && pv != DBNull.Value) oldPrice = Convert.ToDecimal(pv);
                }

                using var con = new MySqlConnection(_config.GetConnectionString("ConnectionString"));
                using var cmd = new MySqlCommand("SP_SaveCourse", con);
                cmd.CommandType = CommandType.StoredProcedure;

                cmd.Parameters.AddWithValue("p_CourseID", req.CourseID);
                cmd.Parameters.AddWithValue("p_CourseTitle", (object)req.CourseTitle ?? DBNull.Value);
                cmd.Parameters.AddWithValue("p_CourseLevel", (object)req.CourseLevel ?? DBNull.Value);
                cmd.Parameters.AddWithValue("p_Language", (object)req.Language ?? DBNull.Value);
                cmd.Parameters.AddWithValue("p_Category", (object)req.Category ?? DBNull.Value);
                cmd.Parameters.AddWithValue("p_Instructor", (object)req.Instructor ?? DBNull.Value);
                cmd.Parameters.AddWithValue("p_StartDate", req.StartDate);
                cmd.Parameters.AddWithValue("p_EndDate", req.EndDate);
                cmd.Parameters.AddWithValue("p_Price", (object)req.Price ?? DBNull.Value);
                cmd.Parameters.AddWithValue("p_EnrollmentLimit", (object)req.EnrollmentLimit ?? DBNull.Value);
                cmd.Parameters.AddWithValue("p_DurationHrs", (object)req.DurationHrs ?? DBNull.Value);
                cmd.Parameters.AddWithValue("p_OneLineDescription", (object)req.OneLineDescription ?? DBNull.Value);
                cmd.Parameters.AddWithValue("p_CourseLogo", (object)req.CourseLogo ?? DBNull.Value);
                cmd.Parameters.AddWithValue("p_CoverImage", (object)req.CoverImage ?? DBNull.Value);
                cmd.Parameters.AddWithValue("p_PromoVideo", (object)req.PromoVideo ?? DBNull.Value);
                cmd.Parameters.AddWithValue("p_CreatedBy", (object)req.CreatedBy ?? DBNull.Value);

                var outParam = new MySqlParameter("p_NewCourseID", MySqlDbType.Int32)
                { Direction = ParameterDirection.Output };
                cmd.Parameters.Add(outParam);

                con.Open();
                cmd.ExecuteNonQuery();

                int newCourseID = 0;

                if (outParam.Value != DBNull.Value)
                    newCourseID = Convert.ToInt32(outParam.Value);

                if (newCourseID == 0)
                    newCourseID = req.CourseID; // For update


                _logger.LogInformation("Course saved: ID={CourseID}, IsUpdate={IsUpdate}",
                    newCourseID, req.CourseID > 0);

                // notify wishlisted students if the price was reduced
                if (isUpdate && req.Price.HasValue)
                    FirePriceDropNotifications(newCourseID, oldPrice, req.Price.Value);

                return Ok(new { Status = "Success", CourseID = newCourseID });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving course");
                return StatusCode(500, new { Status = "Error", Message = ex.Message });
            }
        }


        // ────────────────────────────────────────────────────────
        // Wishlist price-drop notifications.
        // Fires only when an existing course's price is REDUCED. Notifies
        // every student who has the course wishlisted and is NOT already
        // enrolled. Runs in its own DI scope; never blocks the save.
        // ────────────────────────────────────────────────────────
        private void FirePriceDropNotifications(int courseId, decimal oldPrice, decimal newPrice)
        {
            if (courseId <= 0 || oldPrice <= 0 || newPrice >= oldPrice) return;

            _ = Task.Run(async () =>
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var notifier = scope.ServiceProvider.GetRequiredService<INotificationService>();

                    using var conn = new MySqlConnection(_config.GetConnectionString("ConnectionString"));

                    string title = await conn.ExecuteScalarAsync<string>(
                        "SELECT CourseTitle FROM TblCourseMaster WHERE CourseID = @c",
                        new { c = courseId }) ?? "a course";

                    // wishlisters who haven't already enrolled
                    var students = (await conn.QueryAsync<int>(
                        @"SELECT w.StudentID
                            FROM TblCourseWishlist w
                           WHERE w.CourseID = @c
                             AND NOT EXISTS (
                                   SELECT 1 FROM TblStudentCourses sc
                                    WHERE sc.StudentID = w.StudentID
                                      AND sc.CourseID  = @c
                                      AND sc.IsActive  = 1)",
                        new { c = courseId })).ToList();

                    if (students.Count == 0) return;

                    decimal drop = oldPrice - newPrice;
                    int pct = (int)System.Math.Round(drop * 100m / oldPrice);

                    string baseUrl = (_config["PublicBaseUrl"] ?? _config["ApiBaseUrl"] ?? "").TrimEnd('/');
                    string link = string.IsNullOrEmpty(baseUrl) ? "#" : $"{baseUrl}/Courses/Details/{courseId}";

                    foreach (var sid in students)
                    {
                        await notifier.NotifyAsync(new NotifyRequest
                        {
                            UserID = sid,
                            EventCode = "PRICE_DROP",
                            Title = "Price drop on your wishlist!",
                            Body = $"<strong>{title}</strong> is now ₹{newPrice:0.00} " +
                                        $"(was ₹{oldPrice:0.00} — {pct}% off). Grab it before it's gone!",
                            LinkUrl = link,
                            Icon = "fa-solid fa-tags",
                            SendInApp = true,
                            SendEmail = true
                            // no dedicated DB template: NotifyAsync uses Title/Body as the email
                        });
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Price-drop notifications failed (non-fatal). Course={C}", courseId);
                }
            });
        }

        // ✅ Add/Update Course Content
        [HttpPost("save-content")]
        public IActionResult SaveCourseContent([FromBody] CourseContentRequest content)
        {
            if (content == null)
                return BadRequest(new { Status = "Error", Message = "Content payload is missing" });

            if (content.CourseID <= 0)
                return BadRequest(new { Status = "Error", Message = "Invalid CourseID" });

            try
            {
                using var con = new MySqlConnection(_config.GetConnectionString("ConnectionString"));
                con.Open();

                // Check if content exists (for update)
                if (content.ContentID > 0)
                {
                    using var cmdUpdate = new MySqlCommand("SP_UpdateCourseContent", con);
                    cmdUpdate.CommandType = CommandType.StoredProcedure;

                    cmdUpdate.Parameters.AddWithValue("p_ContentID", content.ContentID);
                    cmdUpdate.Parameters.AddWithValue("p_CourseID", content.CourseID);
                    cmdUpdate.Parameters.AddWithValue("p_ContentTitle", content.ContentTitle);
                    cmdUpdate.Parameters.AddWithValue("p_VideoThumbnail", content.VideoThumbnail ?? (object)DBNull.Value);
                    cmdUpdate.Parameters.AddWithValue("p_ContentFile", content.ContentFile ?? (object)DBNull.Value);
                    cmdUpdate.Parameters.AddWithValue("p_FileType", content.FileType);
                    cmdUpdate.Parameters.AddWithValue("p_Description", content.Description);
                    cmdUpdate.Parameters.AddWithValue("p_SortOrder", content.SortOrder);
                    cmdUpdate.Parameters.AddWithValue("p_UpdatedBy", content.CreatedBy);

                    cmdUpdate.ExecuteNonQuery();

                    _logger.LogInformation("Content updated: ID={ContentID}", content.ContentID);
                    return Ok(new { Status = "Success", Message = "Content updated successfully" });
                }
                else
                {
                    // Insert new content
                    using var cmdInsert = new MySqlCommand("SP_SaveCourseContent", con);
                    cmdInsert.CommandType = CommandType.StoredProcedure;

                    cmdInsert.Parameters.AddWithValue("p_CourseID", content.CourseID);
                    cmdInsert.Parameters.AddWithValue("p_ContentTitle", (object)content.ContentTitle ?? DBNull.Value);
                    cmdInsert.Parameters.AddWithValue("p_VideoThumbnail", (object)content.VideoThumbnail ?? DBNull.Value);
                    cmdInsert.Parameters.AddWithValue("p_ContentFile", (object)content.ContentFile ?? DBNull.Value);
                    cmdInsert.Parameters.AddWithValue("p_FileType", (object)content.FileType ?? DBNull.Value);
                    cmdInsert.Parameters.AddWithValue("p_Description", (object)content.Description ?? DBNull.Value);
                    cmdInsert.Parameters.AddWithValue("p_SortOrder", content.SortOrder);
                    cmdInsert.Parameters.AddWithValue("p_CreatedBy", (object)content.CreatedBy ?? DBNull.Value);

                    cmdInsert.ExecuteNonQuery();

                    _logger.LogInformation("Content added: CourseID={CourseID}", content.CourseID);
                    return Ok(new { Status = "Success", Message = "Content added successfully" });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving content");
                return StatusCode(500, new { Status = "Error", Message = ex.Message });
            }
        }

        // ✅ Delete Course Content
        [HttpDelete("content/{contentId}")]
        public IActionResult DeleteContent(int contentId)
        {
            try
            {
                using var con = new MySqlConnection(_config.GetConnectionString("ConnectionString"));
                using var cmd = new MySqlCommand("SP_DeleteCourseContent", con);
                cmd.CommandType = CommandType.StoredProcedure;
                cmd.Parameters.AddWithValue("p_ContentID", contentId);

                con.Open();
                cmd.ExecuteNonQuery();

                _logger.LogInformation("Content deleted: ID={ContentID}", contentId);
                return Ok(new { Status = "Success", Message = "Content deleted successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting content");
                return StatusCode(500, new { Status = "Error", Message = ex.Message });
            }
        }

        // ✅ Get Course For Edit (includes content)
        [HttpGet("edit/{courseId}")]
        public IActionResult GetCourseForEdit(int courseId)
        {
            try
            {
                using var con = new MySqlConnection(_config.GetConnectionString("ConnectionString"));
                con.Open();

                object course = null;
                var contents = new List<object>();

                using (var cmd = new MySqlCommand("SP_GetCourseForEdit", con))
                {
                    cmd.CommandType = CommandType.StoredProcedure;
                    cmd.Parameters.AddWithValue("p_CourseID", courseId);

                    using var reader = cmd.ExecuteReader();

                    // Read course details
                    if (reader.Read())
                    {
                        course = new
                        {
                            CourseID = Convert.ToInt32(reader["CourseID"]),
                            CourseTitle = reader["CourseTitle"].ToString(),
                            CourseLevel = reader["CourseLevel"].ToString(),
                            CourseLevelName = reader["CourseLevelName"]?.ToString() ?? "",
                            Language = reader["Language"].ToString(),
                            Category = reader["Category"].ToString(),
                            Instructor = reader["Instructor"].ToString(),
                            TutorName = reader["TutorName"]?.ToString() ?? "",
                            StartDate = reader["StartDate"] != DBNull.Value ? Convert.ToDateTime(reader["StartDate"]) : (DateTime?)null,
                            EndDate = reader["EndDate"] != DBNull.Value ? Convert.ToDateTime(reader["EndDate"]) : (DateTime?)null,
                            Price = reader["Price"] != DBNull.Value ? Convert.ToDecimal(reader["Price"]) : 0,
                            EnrollmentLimit = reader["EnrollmentLimit"] != DBNull.Value ? Convert.ToInt32(reader["EnrollmentLimit"]) : 0,
                            DurationHrs = reader["DurationHrs"] != DBNull.Value ? Convert.ToDecimal(reader["DurationHrs"]) : 0,
                            OneLineDescription = reader["OneLineDescription"]?.ToString() ?? "",
                            CourseLogo = reader["CourseLogo"]?.ToString() ?? "",
                            CoverImage = reader["CoverImage"]?.ToString() ?? "",
                            PromoVideo = reader["PromoVideo"]?.ToString() ?? "",
                            IsActive = reader["IsActive"] != DBNull.Value && Convert.ToBoolean(reader["IsActive"])
                        };
                    }

                    // Move to next result set (contents)
                    if (reader.NextResult())
                    {
                        while (reader.Read())
                        {
                            contents.Add(new
                            {
                                ContentID = Convert.ToInt32(reader["ContentID"]),
                                CourseID = Convert.ToInt32(reader["CourseID"]),
                                ContentTitle = reader["ContentTitle"].ToString(),
                                Description = reader["Description"].ToString(),
                                FileType = reader["FileType"].ToString(),
                                SortOrder = reader["SortOrder"] != DBNull.Value ? Convert.ToInt32(reader["SortOrder"]) : 0,
                                VideoThumbnail = reader["VideoThumbnail"]?.ToString() ?? "",
                                ContentFile = reader["ContentFile"]?.ToString() ?? ""
                            });
                        }
                    }
                }

                if (course == null)
                    return NotFound(new { Status = "Error", Message = "Course not found" });

                return Ok(new { course, contents });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting course for edit");
                return StatusCode(500, new { Status = "Error", Message = ex.Message });
            }
        }

        // ✅ GET: List all courses
        [HttpGet("list")]
        public IActionResult GetAllCourses()
        {
            try
            {
                using var con = new MySqlConnection(_config.GetConnectionString("ConnectionString"));
                using var cmd = new MySqlCommand(@"
                    SELECT 
                        c.CourseID,
                        c.CourseTitle,
                        c.CourseLevel,
                        l.LevelName AS CourseLevelName,
                        c.Language,
                        c.Category,
                        c.Instructor,
                        t.FullName AS TutorName,
                        c.StartDate,
                        c.EndDate,
                        c.Price,
                        c.EnrollmentLimit,
                        c.DurationHrs,
                        c.OneLineDescription,
                        c.CourseLogo,
                        c.CoverImage,
                        c.PromoVideo,
                        c.IsActive,
                        c.CreatedBy,
                        c.CreatedDate
                    FROM TblCourseMaster c
                    LEFT JOIN TblTutorRegistration t ON c.Instructor = t.TutorID
                    LEFT JOIN TblCourseLevel l ON c.CourseLevel = l.LevelID 
                    WHERE c.IsActive = 1
                    ORDER BY c.CourseID DESC;", con);

                con.Open();
                using var reader = cmd.ExecuteReader();
                var list = new List<object>();

                while (reader.Read())
                {
                    list.Add(new
                    {
                        CourseID = Convert.ToInt32(reader["CourseID"]),
                        CourseTitle = reader["CourseTitle"]?.ToString() ?? "",
                        CourseLevel = reader["CourseLevel"]?.ToString() ?? "",
                        CourseLevelName = reader["CourseLevelName"]?.ToString() ?? "-",
                        Language = reader["Language"]?.ToString() ?? "",
                        Category = reader["Category"]?.ToString() ?? "",
                        Instructor = reader["Instructor"]?.ToString() ?? "",
                        TutorName = reader["TutorName"]?.ToString() ?? "Unknown Tutor",
                        StartDate = reader["StartDate"] == DBNull.Value ? null : Convert.ToDateTime(reader["StartDate"]).ToString("yyyy-MM-dd"),
                        EndDate = reader["EndDate"] == DBNull.Value ? null : Convert.ToDateTime(reader["EndDate"]).ToString("yyyy-MM-dd"),
                        Price = reader["Price"] == DBNull.Value ? 0 : Convert.ToDecimal(reader["Price"]),
                        EnrollmentLimit = reader["EnrollmentLimit"] == DBNull.Value ? 0 : Convert.ToInt32(reader["EnrollmentLimit"]),
                        DurationHrs = reader["DurationHrs"] == DBNull.Value ? 0 : Convert.ToDecimal(reader["DurationHrs"]),
                        OneLineDescription = reader["OneLineDescription"]?.ToString() ?? "",
                        CourseLogo = reader["CourseLogo"]?.ToString() ?? "",
                        CoverImage = reader["CoverImage"]?.ToString() ?? "",
                        PromoVideo = reader["PromoVideo"]?.ToString() ?? "",
                        IsActive = reader["IsActive"] != DBNull.Value && Convert.ToBoolean(reader["IsActive"]),
                        CreatedBy = reader["CreatedBy"]?.ToString() ?? "",
                        CreatedDate = reader["CreatedDate"] == DBNull.Value ? "" : Convert.ToDateTime(reader["CreatedDate"]).ToString("yyyy-MM-dd HH:mm")
                    });
                }

                return Ok(list);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting courses");
                return StatusCode(500, new { Error = ex.Message });
            }
        }

        // ✅ GET: Course details
        [HttpGet("details/{courseId}")]
        public IActionResult GetCourseDetails(int courseId)
        {
            try
            {
                using var con = new MySqlConnection(_config.GetConnectionString("ConnectionString"));
                con.Open();

                object course = null;
                using (var cmdCourse = new MySqlCommand(@"
                    SELECT 
                        c.CourseID, c.CourseTitle, c.CourseLevel, l.LevelName AS CourseLevelName,
                        c.Language, c.Category, c.Instructor, t.FullName AS TutorName,
                        c.StartDate, c.EndDate, c.Price, c.EnrollmentLimit, c.DurationHrs,
                        c.OneLineDescription, c.CourseLogo, c.CoverImage, c.PromoVideo,
                        c.IsActive, c.CreatedBy, c.CreatedDate
                    FROM TblCourseMaster c
                    LEFT JOIN TblTutorRegistration t ON c.Instructor = t.TutorID
                    LEFT JOIN TblCourseLevel l ON c.CourseLevel = l.LevelID
                    WHERE c.CourseID = @id;", con))
                {
                    cmdCourse.Parameters.AddWithValue("@id", courseId);
                    using var reader = cmdCourse.ExecuteReader();
                    if (reader.Read())
                    {
                        course = new
                        {
                            CourseID = Convert.ToInt32(reader["CourseID"]),
                            CourseTitle = reader["CourseTitle"].ToString(),
                            CourseLevel = reader["CourseLevel"].ToString(),
                            CourseLevelName = reader["CourseLevelName"].ToString(),
                            Language = reader["Language"].ToString(),
                            Category = reader["Category"].ToString(),
                            Instructor = reader["Instructor"].ToString(),
                            TutorName = reader["TutorName"].ToString(),
                            StartDate = reader["StartDate"] != DBNull.Value ? Convert.ToDateTime(reader["StartDate"]) : (DateTime?)null,
                            EndDate = reader["EndDate"] != DBNull.Value ? Convert.ToDateTime(reader["EndDate"]) : (DateTime?)null,
                            Price = reader["Price"] != DBNull.Value ? Convert.ToDecimal(reader["Price"]) : 0,
                            EnrollmentLimit = reader["EnrollmentLimit"] != DBNull.Value ? Convert.ToInt32(reader["EnrollmentLimit"]) : 0,
                            DurationHrs = reader["DurationHrs"] != DBNull.Value ? Convert.ToDecimal(reader["DurationHrs"]) : 0,
                            OneLineDescription = reader["OneLineDescription"].ToString(),
                            CourseLogo = reader["CourseLogo"].ToString(),
                            CoverImage = reader["CoverImage"].ToString(),
                            PromoVideo = reader["PromoVideo"].ToString(),
                            IsActive = reader["IsActive"] != DBNull.Value && Convert.ToBoolean(reader["IsActive"]),
                            CreatedBy = reader["CreatedBy"].ToString(),
                            CreatedDate = reader["CreatedDate"] != DBNull.Value ? Convert.ToDateTime(reader["CreatedDate"]) : (DateTime?)null
                        };
                    }
                }

                if (course == null)
                    return NotFound(new { Status = "Error", Message = "Course not found" });

                var contents = new List<object>();
                using (var cmdContent = new MySqlCommand(@"
                    SELECT ContentID, CourseID, ContentTitle, Description, FileType, SortOrder,
                           VideoThumbnail, ContentFile, CreatedBy, CreatedDate
                    FROM TblCourseContent 
                    WHERE CourseID = @id
                    ORDER BY SortOrder;", con))
                {
                    cmdContent.Parameters.AddWithValue("@id", courseId);
                    using var reader2 = cmdContent.ExecuteReader();
                    while (reader2.Read())
                    {
                        contents.Add(new
                        {
                            ContentID = Convert.ToInt32(reader2["ContentID"]),
                            CourseID = Convert.ToInt32(reader2["CourseID"]),
                            ContentTitle = reader2["ContentTitle"].ToString(),
                            Description = reader2["Description"].ToString(),
                            FileType = reader2["FileType"].ToString(),
                            SortOrder = reader2["SortOrder"] != DBNull.Value ? Convert.ToInt32(reader2["SortOrder"]) : 0,
                            VideoThumbnail = reader2["VideoThumbnail"].ToString(),
                            ContentFile = reader2["ContentFile"].ToString(),
                            CreatedBy = reader2["CreatedBy"].ToString(),
                            CreatedDate = reader2["CreatedDate"] != DBNull.Value ? Convert.ToDateTime(reader2["CreatedDate"]) : (DateTime?)null
                        });
                    }
                }

                return Ok(new { course, contents });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting course details");
                return StatusCode(500, new { Status = "Error", Message = ex.Message });
            }
        }

        // ✅ GET: Tutors list
        [HttpGet("tutors")]
        public IActionResult GetTutors()
        {
            try
            {
                using var con = new MySqlConnection(_config.GetConnectionString("ConnectionString"));
                con.Open();

                var cmd = new MySqlCommand("SELECT TutorID, FullName as TutorName FROM TblTutorRegistration WHERE IsActive = 1 ORDER BY FullName;", con);
                using var reader = cmd.ExecuteReader();

                var tutors = new List<object>();
                while (reader.Read())
                {
                    tutors.Add(new
                    {
                        TutorID = reader["TutorID"],
                        TutorName = reader["TutorName"]
                    });
                }

                return Ok(tutors);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting tutors");
                return StatusCode(500, new { Error = ex.Message });
            }
        }
        [HttpPost("reorder-content")]
        public IActionResult ReorderContent([FromBody] ReorderContentRequest req)
        {
            if (req == null || req.CourseID <= 0 || req.ContentIDs == null || req.ContentIDs.Count == 0)
                return BadRequest(new { Status = "Error", Message = "Invalid request" });

            try
            {
                using var con = new MySqlConnection(_config.GetConnectionString("ConnectionString"));
                using var cmd = new MySqlCommand("SP_Course_ReorderContent", con);
                cmd.CommandType = CommandType.StoredProcedure;
                cmd.Parameters.AddWithValue("p_CourseID", req.CourseID);
                cmd.Parameters.AddWithValue("p_ContentIDs", string.Join(",", req.ContentIDs));

                con.Open();
                cmd.ExecuteNonQuery();

                _logger.LogInformation("Reordered {Count} lessons for course {Course}",
                    req.ContentIDs.Count, req.CourseID);
                return Ok(new { Status = "Success" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reordering content for course {Course}", req.CourseID);
                return StatusCode(500, new { Status = "Error", Message = ex.Message });
            }
        }


        // ── Add next to CourseRequest / CourseContentRequest ──


    }
    public class ReorderContentRequest
    {
        public int CourseID { get; set; }
        public List<int> ContentIDs { get; set; }
    }
    // ✅ Model classes
    public class CourseRequest
    {
        public int CourseID { get; set; }
        public string CourseTitle { get; set; }
        public string CourseLevel { get; set; }
        public string? CourseLevelName { get; set; }
        public string Language { get; set; }
        public string Category { get; set; }
        public string Instructor { get; set; }
        public string? TutorName { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public decimal? Price { get; set; }
        public int? EnrollmentLimit { get; set; }
        public decimal? DurationHrs { get; set; }
        public string? OneLineDescription { get; set; }
        public string CourseLogo { get; set; }
        public string CoverImage { get; set; }
        public string PromoVideo { get; set; }
        public string CreatedBy { get; set; }
    }

    public class CourseContentRequest
    {
        public int ContentID { get; set; }  // Add this for updates
        public int CourseID { get; set; }
        public string ContentTitle { get; set; }
        public string VideoThumbnail { get; set; }
        public string ContentFile { get; set; }
        public string FileType { get; set; }
        public string Description { get; set; }
        public int SortOrder { get; set; }
        public string CreatedBy { get; set; }
    }
}
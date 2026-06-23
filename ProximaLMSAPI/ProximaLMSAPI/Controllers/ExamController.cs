using Dapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using MySql.Data.MySqlClient;
using Newtonsoft.Json;
using ProximaLMSAPI.Services;
using System.Data;

namespace ProximaLMSAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ExamController : ControllerBase
    {
        private readonly IConfiguration _config;
        private readonly ILogger<ExamController> _logger;
        private readonly IServiceScopeFactory _scopeFactory;

        public ExamController(IConfiguration config, ILogger<ExamController> logger,
                              IServiceScopeFactory scopeFactory)
        {
            _config = config;
            _logger = logger;
            _scopeFactory = scopeFactory;
        }

        private MySqlConnection CreateConn()
            => new MySqlConnection(_config.GetConnectionString("ConnectionString"));

        // ── safe readers for Dapper dynamic rows ─────────────────────────────────────
        // MySQL integer columns can arrive as long, and tinyint(1) as bool. A direct
        // (int) unbox through dynamic throws at runtime for those. These helpers read
        // by column name and convert defensively.
        private static string DynStr(dynamic row, string key)
        {
            var d = (IDictionary<string, object>)row;
            return d.TryGetValue(key, out var v) && v != null ? v.ToString() : null;
        }
        private static int DynInt(dynamic row, string key)
        {
            var d = (IDictionary<string, object>)row;
            return d.TryGetValue(key, out var v) && v != null ? Convert.ToInt32(v) : 0;
        }

        // Converts a Dapper DapperRow (dynamic / IDictionary<string,object>) to a plain
        // Dictionary so that System.Text.Json can serialize it correctly.
        // Keys are camelCased so the JS front-end can access them without dual-case checks.
        // DapperRow does NOT serialize with System.Text.Json out-of-the-box (renders as {}).
        private static string CamelKey(string s)
            => string.IsNullOrEmpty(s) ? s : char.ToLowerInvariant(s[0]) + s.Substring(1);

        private static Dictionary<string, object> ToDict(dynamic row)
            => ((IDictionary<string, object>)row)
                .ToDictionary(kv => CamelKey(kv.Key), kv => kv.Value);

        private static List<Dictionary<string, object>> ToDictList(IEnumerable<dynamic> rows)
            => rows.Select(r => ((IDictionary<string, object>)r)
                .ToDictionary(kv => CamelKey(kv.Key), kv => kv.Value)).ToList();


        // ── EXAM HEADER ───────────────────────────────────────────
        [HttpPost("save")]
        public async Task<IActionResult> SaveExam([FromBody] ExamSaveRequest req)
        {
            if (req == null || req.CourseID <= 0 || string.IsNullOrWhiteSpace(req.Title))
                return BadRequest(new { Status = "Error", Message = "Course and title are required." });

            try
            {
                using var conn = CreateConn();
                var p = new DynamicParameters();
                p.Add("p_ExamID", req.ExamID);
                p.Add("p_CourseID", req.CourseID);
                p.Add("p_Title", req.Title.Trim());
                p.Add("p_Instructions", req.Instructions ?? "");
                p.Add("p_PassPercentage", req.PassPercentage);
                p.Add("p_DurationMinutes", req.DurationMinutes);
                p.Add("p_MaxAttempts", req.MaxAttempts);
                p.Add("p_ShuffleQuestions", req.ShuffleQuestions ? 1 : 0);
                p.Add("p_ShuffleOptions", req.ShuffleOptions ? 1 : 0);
                p.Add("p_RevealAnswers", req.RevealAnswers ? 1 : 0);
                p.Add("p_Actor", req.Actor ?? "Admin");
                p.Add("p_OutExamID", dbType: DbType.Int32, direction: ParameterDirection.Output);

                await conn.ExecuteAsync("SP_Exam_Save", p, commandType: CommandType.StoredProcedure);
                int examId = p.Get<int>("p_OutExamID");

                return Ok(new { Status = "Success", ExamID = examId });
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "Error saving exam");
                return StatusCode(500, new { Status = "Error", Message = ex.Message });
            }
        }

        [HttpGet("by-course/{courseId:int}")]
        public async Task<IActionResult> GetByCourse(int courseId, [FromQuery] int studentId = 0)
        {
            try
            {
                using var conn = CreateConn();
                var rows = await conn.QueryAsync(
                    "SP_Exam_GetByCourse",
                    new { p_CourseID = courseId, p_StudentID = studentId },
                    commandType: CommandType.StoredProcedure);
                return Ok(ToDictList(rows));
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "Error listing exams for course {Course}", courseId);
                return StatusCode(500, new { Status = "Error", Message = ex.Message });
            }
        }

        [HttpGet("full/{examId:int}")]
        public async Task<IActionResult> GetFull(int examId)
        {
            try
            {
                using var conn = CreateConn();
                using var multi = await conn.QueryMultipleAsync(
                    "SP_Exam_GetFull",
                    new { p_ExamID = examId },
                    commandType: CommandType.StoredProcedure);

                var header = await multi.ReadFirstOrDefaultAsync<ExamHeaderDto>();
                if (header == null)
                    return NotFound(new { Status = "Error", Message = "Exam not found." });

                var questions = (await multi.ReadAsync<ExamQuestionDto>()).ToList();
                var options = (await multi.ReadAsync<ExamOptionDto>()).ToList();

                foreach (var q in questions)
                    q.Options = options.Where(o => o.QuestionID == q.QuestionID).ToList();

                return Ok(new { exam = header, questions }); // header/questions are typed DTOs — serialize fine
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "Error loading exam {Exam}", examId);
                return StatusCode(500, new { Status = "Error", Message = ex.Message });
            }
        }

        [HttpPost("toggle-status")]
        public async Task<IActionResult> ToggleStatus([FromBody] ExamToggleRequest req)
        {
            if (req == null || req.ExamID <= 0)
                return BadRequest(new { Status = "Error", Message = "Invalid request." });

            try
            {
                using var conn = CreateConn();
                await conn.ExecuteAsync("SP_Exam_ToggleStatus",
                    new { p_ExamID = req.ExamID, p_IsActive = req.IsActive ? 1 : 0 },
                    commandType: CommandType.StoredProcedure);
                return Ok(new { Status = "Success", IsActive = req.IsActive });
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "Error toggling exam {Exam}", req.ExamID);
                return StatusCode(500, new { Status = "Error", Message = ex.Message });
            }
        }

        [HttpPost("delete")]
        public async Task<IActionResult> DeleteExam([FromBody] ExamToggleRequest req)
        {
            if (req == null || req.ExamID <= 0)
                return BadRequest(new { Status = "Error", Message = "Invalid request." });

            try
            {
                using var conn = CreateConn();
                await conn.ExecuteAsync("SP_Exam_Delete",
                    new { p_ExamID = req.ExamID },
                    commandType: CommandType.StoredProcedure);
                return Ok(new { Status = "Success" });
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "Error deleting exam {Exam}", req.ExamID);
                return StatusCode(500, new { Status = "Error", Message = ex.Message });
            }
        }


        // ── QUESTION (+ options, atomic) ──────────────────────────
        [HttpPost("question/save")]
        public async Task<IActionResult> SaveQuestion([FromBody] QuestionSaveRequest req)
        {
            if (req == null || req.ExamID <= 0 || string.IsNullOrWhiteSpace(req.QuestionText))
                return BadRequest(new { Status = "Error", Message = "Exam and question text are required." });

            var choiceTypes = new[] { "Single", "Multiple", "TrueFalse" };
            bool needsOptions = choiceTypes.Contains(req.QuestionType ?? "Single");

            if (needsOptions)
            {
                if (req.Options == null || req.Options.Count(o => !string.IsNullOrWhiteSpace(o.OptionText)) < 2)
                    return BadRequest(new { Status = "Error", Message = "A question needs at least two options." });
                if (!req.Options.Any(o => o.IsCorrect))
                    return BadRequest(new { Status = "Error", Message = "Mark at least one correct option." });
            }
            if ((req.QuestionType == "FillBlank") && string.IsNullOrWhiteSpace(req.AcceptableAnswers))
                return BadRequest(new { Status = "Error", Message = "Fill-blank questions need acceptable answers (pipe-separated)." });

            using var conn = CreateConn();
            await conn.OpenAsync();
            using var tx = await conn.BeginTransactionAsync();
            try
            {
                // 1. save the question header
                var qp = new DynamicParameters();
                qp.Add("p_QuestionID", req.QuestionID);
                qp.Add("p_ExamID", req.ExamID);
                qp.Add("p_QuestionText", req.QuestionText.Trim());
                qp.Add("p_QuestionType", string.IsNullOrWhiteSpace(req.QuestionType) ? "Single" : req.QuestionType);
                qp.Add("p_Difficulty", string.IsNullOrWhiteSpace(req.Difficulty) ? "Medium" : req.Difficulty);
                qp.Add("p_Marks", req.Marks <= 0 ? 1 : req.Marks);
                qp.Add("p_SortOrder", req.SortOrder);
                qp.Add("p_AcceptableAnswers", req.AcceptableAnswers);
                qp.Add("p_Explanation", req.Explanation);
                qp.Add("p_OutQuestionID", dbType: DbType.Int32, direction: ParameterDirection.Output);

                await conn.ExecuteAsync("SP_ExamQuestion_Save", qp,
                    transaction: tx, commandType: CommandType.StoredProcedure);
                int questionId = qp.Get<int>("p_OutQuestionID");

                // 2. replace options (only for choice types)
                await conn.ExecuteAsync("SP_ExamOption_DeleteByQuestion",
                    new { p_QuestionID = questionId },
                    transaction: tx, commandType: CommandType.StoredProcedure);

                if (needsOptions && req.Options != null)
                {
                    int order = 1;
                    foreach (var opt in req.Options)
                    {
                        if (string.IsNullOrWhiteSpace(opt.OptionText)) continue;
                        await conn.ExecuteAsync("SP_ExamOption_Save",
                            new
                            {
                                p_QuestionID = questionId,
                                p_OptionText = opt.OptionText.Trim(),
                                p_IsCorrect = opt.IsCorrect ? 1 : 0,
                                p_SortOrder = order++
                            },
                            transaction: tx, commandType: CommandType.StoredProcedure);
                    }
                }

                await tx.CommitAsync();
                return Ok(new { Status = "Success", QuestionID = questionId });
            }
            catch (System.Exception ex)
            {
                await tx.RollbackAsync();
                _logger.LogError(ex, "Error saving question for exam {Exam}", req.ExamID);
                return StatusCode(500, new { Status = "Error", Message = ex.Message });
            }
        }

        [HttpPost("question/delete")]
        public async Task<IActionResult> DeleteQuestion([FromBody] QuestionDeleteRequest req)
        {
            if (req == null || req.QuestionID <= 0)
                return BadRequest(new { Status = "Error", Message = "Invalid request." });

            try
            {
                using var conn = CreateConn();
                await conn.ExecuteAsync("SP_ExamQuestion_Delete",
                    new { p_QuestionID = req.QuestionID },
                    commandType: CommandType.StoredProcedure);
                return Ok(new { Status = "Success" });
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "Error deleting question {Q}", req.QuestionID);
                return StatusCode(500, new { Status = "Error", Message = ex.Message });
            }
        }

        [HttpPost("question/reorder")]
        public async Task<IActionResult> ReorderQuestions([FromBody] QuestionReorderRequest req)
        {
            if (req == null || req.ExamID <= 0 || req.QuestionIDs == null || req.QuestionIDs.Count == 0)
                return BadRequest(new { Status = "Error", Message = "Invalid request." });

            try
            {
                using var conn = CreateConn();
                await conn.ExecuteAsync("SP_ExamQuestion_Reorder",
                    new { p_ExamID = req.ExamID, p_QuestionIDs = string.Join(",", req.QuestionIDs) },
                    commandType: CommandType.StoredProcedure);
                return Ok(new { Status = "Success" });
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "Error reordering questions for exam {Exam}", req.ExamID);
                return StatusCode(500, new { Status = "Error", Message = ex.Message });
            }
        }


        // ── BANK: list ──────────────────────────────────────────────────────────────
        [HttpGet("bank/list")]
        public async Task<IActionResult> BankList(
            [FromQuery] int courseId,
            [FromQuery] string difficulty = null,
            [FromQuery] string type = null,
            [FromQuery] string search = null)
        {
            if (courseId <= 0)
                return BadRequest(new { Status = "Error", Message = "Invalid course id." });

            try
            {
                using var conn = CreateConn();
                var rows = await conn.QueryAsync(
                    "SP_QuestionBank_List",
                    new
                    {
                        p_CourseID = courseId,
                        p_Difficulty = difficulty ?? "",
                        p_Type = type ?? "",
                        p_Search = search ?? ""
                    },
                    commandType: CommandType.StoredProcedure);
                return Ok(ToDictList(rows));
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "BankList failed for course {Course}", courseId);
                return StatusCode(500, new { Status = "Error", Message = ex.Message });
            }
        }


        // ── BANK: get full (question + its options) ─────────────────────────────────
        [HttpGet("bank/full/{bankQuestionId:int}")]
        public async Task<IActionResult> BankGetFull(int bankQuestionId)
        {
            try
            {
                using var conn = CreateConn();
                using var multi = await conn.QueryMultipleAsync(
                    "SP_QuestionBank_GetFull",
                    new { p_BankQuestionID = bankQuestionId },
                    commandType: CommandType.StoredProcedure);

                var question = await multi.ReadFirstOrDefaultAsync<BankQuestionDto>();
                if (question == null)
                    return NotFound(new { Status = "Error", Message = "Bank question not found." });

                var options = (await multi.ReadAsync<BankOptionDto>()).ToList();
                question.Options = options;
                return Ok(question);
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "BankGetFull failed for {Id}", bankQuestionId);
                return StatusCode(500, new { Status = "Error", Message = ex.Message });
            }
        }


        // ── BANK: save (question + replace options) — atomic ───────────────────────
        [HttpPost("bank/save")]
        public async Task<IActionResult> BankSave([FromBody] BankSaveRequest req)
        {
            if (req == null || req.CourseID <= 0 || string.IsNullOrWhiteSpace(req.QuestionText))
                return BadRequest(new { Status = "Error", Message = "Course and question text are required." });

            // type-specific validation
            var typesNeedingOptions = new[] { "Single", "Multiple", "TrueFalse" };
            bool needsOptions = typesNeedingOptions.Contains(req.QuestionType);

            if (needsOptions)
            {
                var validOpts = (req.Options ?? new()).Where(o => !string.IsNullOrWhiteSpace(o.OptionText)).ToList();
                if (validOpts.Count < 2)
                    return BadRequest(new { Status = "Error", Message = "Choice questions need at least two options." });
                if (!validOpts.Any(o => o.IsCorrect))
                    return BadRequest(new { Status = "Error", Message = "Mark at least one correct option." });
            }
            if (req.QuestionType == "FillBlank" && string.IsNullOrWhiteSpace(req.AcceptableAnswers))
                return BadRequest(new { Status = "Error", Message = "Fill-blank questions need acceptable answers (pipe-separated)." });

            using var conn = (MySqlConnection)CreateConn();
            await conn.OpenAsync();
            using var tx = await conn.BeginTransactionAsync();
            try
            {
                // 1. save header
                var qp = new DynamicParameters();
                qp.Add("p_BankQuestionID", req.BankQuestionID);
                qp.Add("p_CourseID", req.CourseID);
                qp.Add("p_QuestionText", req.QuestionText.Trim());
                qp.Add("p_QuestionType", string.IsNullOrWhiteSpace(req.QuestionType) ? "Single" : req.QuestionType);
                qp.Add("p_Difficulty", string.IsNullOrWhiteSpace(req.Difficulty) ? "Medium" : req.Difficulty);
                qp.Add("p_Marks", req.Marks <= 0 ? 1 : req.Marks);
                qp.Add("p_AcceptableAnswers", req.AcceptableAnswers);
                qp.Add("p_Explanation", req.Explanation);
                qp.Add("p_Tags", req.Tags);
                qp.Add("p_Actor", req.Actor ?? "Admin");
                qp.Add("p_OutID", dbType: DbType.Int32, direction: ParameterDirection.Output);

                await conn.ExecuteAsync("SP_QuestionBank_SaveQuestion", qp,
                    transaction: tx, commandType: CommandType.StoredProcedure);
                int bankQuestionId = qp.Get<int>("p_OutID");

                // 2. replace options (only for choice types)
                await conn.ExecuteAsync("SP_QuestionBank_ClearOptions",
                    new { p_BankQuestionID = bankQuestionId },
                    transaction: tx, commandType: CommandType.StoredProcedure);

                if (needsOptions && req.Options != null)
                {
                    int order = 1;
                    foreach (var opt in req.Options.Where(o => !string.IsNullOrWhiteSpace(o.OptionText)))
                    {
                        await conn.ExecuteAsync("SP_QuestionBank_AddOption",
                            new
                            {
                                p_BankQuestionID = bankQuestionId,
                                p_OptionText = opt.OptionText.Trim(),
                                p_IsCorrect = opt.IsCorrect ? 1 : 0,
                                p_SortOrder = order++
                            },
                            transaction: tx, commandType: CommandType.StoredProcedure);
                    }
                }

                await tx.CommitAsync();
                return Ok(new { Status = "Success", BankQuestionID = bankQuestionId });
            }
            catch (System.Exception ex)
            {
                await tx.RollbackAsync();
                _logger.LogError(ex, "BankSave failed");
                return StatusCode(500, new { Status = "Error", Message = ex.Message });
            }
        }


        // ── BANK: delete (soft) ─────────────────────────────────────────────────────
        [HttpPost("bank/delete")]
        public async Task<IActionResult> BankDelete([FromBody] BankIdRequest req)
        {
            if (req == null || req.BankQuestionID <= 0)
                return BadRequest(new { Status = "Error", Message = "Invalid request." });

            try
            {
                using var conn = CreateConn();
                await conn.ExecuteAsync("SP_QuestionBank_Delete",
                    new { p_BankQuestionID = req.BankQuestionID },
                    commandType: CommandType.StoredProcedure);
                return Ok(new { Status = "Success" });
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "BankDelete failed for {Id}", req.BankQuestionID);
                return StatusCode(500, new { Status = "Error", Message = ex.Message });
            }
        }


        // ── BANK ↔ EXAM: link a bank question into an exam ─────────────────────────
        [HttpPost("bank/link")]
        public async Task<IActionResult> BankLink([FromBody] BankLinkRequest req)
        {
            if (req == null || req.ExamID <= 0 || req.BankQuestionID <= 0)
                return BadRequest(new { Status = "Error", Message = "Invalid request." });

            try
            {
                using var conn = CreateConn();
                await conn.ExecuteAsync("SP_ExamQuestion_LinkFromBank",
                    new
                    {
                        p_ExamID = req.ExamID,
                        p_BankQuestionID = req.BankQuestionID,
                        p_MarksOverride = req.MarksOverride
                    },
                    commandType: CommandType.StoredProcedure);
                return Ok(new { Status = "Success" });
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "BankLink failed E={Exam} B={Bank}", req.ExamID, req.BankQuestionID);
                return StatusCode(500, new { Status = "Error", Message = ex.Message });
            }
        }

        [HttpPost("bank/unlink")]
        public async Task<IActionResult> BankUnlink([FromBody] BankLinkRequest req)
        {
            if (req == null || req.ExamID <= 0 || req.BankQuestionID <= 0)
                return BadRequest(new { Status = "Error", Message = "Invalid request." });

            try
            {
                using var conn = CreateConn();
                await conn.ExecuteAsync("SP_ExamQuestion_UnlinkBank",
                    new { p_ExamID = req.ExamID, p_BankQuestionID = req.BankQuestionID },
                    commandType: CommandType.StoredProcedure);
                return Ok(new { Status = "Success" });
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "BankUnlink failed");
                return StatusCode(500, new { Status = "Error", Message = ex.Message });
            }
        }


        // ── Unified question list for an exam (inline + bank in one shape) ─────────
        [HttpGet("all-questions/{examId:int}")]
        public async Task<IActionResult> GetAllQuestions(int examId)
        {
            try
            {
                using var conn = CreateConn();
                var rows = await conn.QueryAsync(
                    "SP_Exam_GetAllQuestions",
                    new { p_ExamID = examId },
                    commandType: CommandType.StoredProcedure);
                return Ok(ToDictList(rows));
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "GetAllQuestions failed for exam {Exam}", examId);
                return StatusCode(500, new { Status = "Error", Message = ex.Message });
            }
        }

        [HttpPost("attempt/start")]
        public async Task<IActionResult> AttemptStart([FromBody] AttemptStartRequest req)
        {
            if (req == null || req.ExamID <= 0 || req.StudentID <= 0)
                return BadRequest(new { Status = "Error", Message = "Invalid request." });

            try
            {
                using var conn = CreateConn();
                var p = new DynamicParameters();
                p.Add("p_ExamID", req.ExamID);
                p.Add("p_StudentID", req.StudentID);
                p.Add("p_OrderCsv", req.OrderCsv);
                p.Add("p_OptionJson", req.OptionJson);
                p.Add("p_AttemptID", dbType: DbType.Int32, direction: ParameterDirection.Output);
                p.Add("p_ResultCode", dbType: DbType.Int32, direction: ParameterDirection.Output);
                p.Add("p_Message", dbType: DbType.String, size: 500, direction: ParameterDirection.Output);

                await conn.ExecuteAsync("SP_ExamAttempt_Start", p, commandType: CommandType.StoredProcedure);

                int code = p.Get<int>("p_ResultCode");
                return Ok(new
                {
                    AttemptID = p.Get<int>("p_AttemptID"),
                    Code = code,                         // 1 new, 2 resumed, 0 max-attempts, -1 not-active, -2 not-found
                    Message = p.Get<string>("p_Message"),
                    Success = code > 0
                });
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "AttemptStart failed");
                return StatusCode(500, new { Status = "Error", Message = ex.Message });
            }
        }

        [HttpGet("attempt/{attemptId:int}/header")]
        public async Task<IActionResult> AttemptHeader(int attemptId)
        {
            try
            {
                using var conn = CreateConn();
                var row = await conn.QueryFirstOrDefaultAsync(
                    "SP_ExamAttempt_GetHeader",
                    new { p_AttemptID = attemptId },
                    commandType: CommandType.StoredProcedure);
                if (row == null) return NotFound(new { Status = "Error", Message = "Attempt not found." });
                var rowDict = ((IDictionary<string, object>)row).ToDictionary(kv => kv.Key, kv => kv.Value);
                return Ok(rowDict);
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "AttemptHeader failed for {Id}", attemptId);
                return StatusCode(500, new { Status = "Error", Message = ex.Message });
            }
        }

        [HttpGet("attempt/{attemptId:int}/state")]
        public async Task<IActionResult> AttemptState(int attemptId)
        {
            try
            {
                using var conn = CreateConn();
                var rawHdr = await conn.QueryFirstOrDefaultAsync(
                    "SP_ExamAttempt_GetHeader",
                    new { p_AttemptID = attemptId },
                    commandType: CommandType.StoredProcedure);
                if (rawHdr == null) return NotFound(new { Status = "Error", Message = "Attempt not found." });
                var header = ((IDictionary<string, object>)rawHdr).ToDictionary(kv => kv.Key, kv => kv.Value);

                var rawAnswers = await conn.QueryAsync(
                    "SP_ExamAttempt_GetAnswers",
                    new { p_AttemptID = attemptId },
                    commandType: CommandType.StoredProcedure);
                var answers = rawAnswers
                    .Select(a => ((IDictionary<string, object>)a).ToDictionary(kv => kv.Key, kv => kv.Value))
                    .ToList();

                return Ok(new { header, answers });
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "AttemptState failed");
                return StatusCode(500, new { Status = "Error", Message = ex.Message });
            }
        }

        [HttpPost("attempt/answer")]
        public async Task<IActionResult> AttemptSaveAnswer([FromBody] SaveAnswerRequest req)
        {
            if (req == null || req.AttemptID <= 0 || req.QuestionRefID <= 0
                || (req.Source != "inline" && req.Source != "bank"))
                return BadRequest(new { Status = "Error", Message = "Invalid request." });

            try
            {
                using var conn = CreateConn();
                await conn.ExecuteAsync("SP_ExamAttempt_SaveAnswer",
                    new
                    {
                        p_AttemptID = req.AttemptID,
                        p_Source = req.Source,
                        p_QuestionRefID = req.QuestionRefID,
                        p_SelectedOptionIDs = req.SelectedOptionIDs,
                        p_TextAnswer = req.TextAnswer
                    },
                    commandType: CommandType.StoredProcedure);
                return Ok(new { Status = "Success" });
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "AttemptSaveAnswer failed");
                return StatusCode(500, new { Status = "Error", Message = ex.Message });
            }
        }

        [HttpPost("attempt/submit")]
        public async Task<IActionResult> AttemptSubmit([FromBody] SubmitAttemptRequest req)
        {
            if (req == null || req.AttemptID <= 0)
                return BadRequest(new { Status = "Error", Message = "Invalid request." });

            try
            {
                using var conn = CreateConn();
                await conn.ExecuteAsync("SP_ExamAttempt_Submit",
                    new { p_AttemptID = req.AttemptID, p_AutoSubmitted = req.AutoSubmitted ? 1 : 0 },
                    commandType: CommandType.StoredProcedure);

                // return the freshly-finalized header so the client can route to the result page
                var rawHeader = await conn.QueryFirstOrDefaultAsync(
                    "SP_ExamAttempt_GetHeader",
                    new { p_AttemptID = req.AttemptID },
                    commandType: CommandType.StoredProcedure);

                // notify the student of their result (background, own DI scope)
                FireExamResultNotification(rawHeader);

                // Convert DapperRow → Dictionary so System.Text.Json can serialize it.
                var finalHeader = rawHeader != null
                    ? ((IDictionary<string, object>)rawHeader).ToDictionary(kv => kv.Key, kv => kv.Value)
                    : new Dictionary<string, object>();

                return Ok(new { Status = "Success", AttemptID = req.AttemptID, Header = finalHeader });
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "AttemptSubmit failed");
                return StatusCode(500, new { Status = "Error", Message = ex.Message });
            }
        }

        // ────────────────────────────────────────────────────────
        // Notify the student of their exam result in the background
        // (own DI scope — NotificationService is scoped). When the
        // attempt still needs manual grading (short-answer), we only
        // send an in-app "awaiting grading" notice and hold the
        // pass/fail email until the score is final.
        // ────────────────────────────────────────────────────────
        private void FireExamResultNotification(object headerRow)
        {
            if (headerRow is not IDictionary<string, object> h) return;

            int GetInt(string k) => h.TryGetValue(k, out var v) && v != null ? Convert.ToInt32(v) : 0;
            decimal GetDec(string k) => h.TryGetValue(k, out var v) && v != null ? Convert.ToDecimal(v) : 0m;
            string GetStr(string k) => h.TryGetValue(k, out var v) && v != null ? v.ToString() : "";

            int studentId = GetInt("StudentID");
            if (studentId <= 0) return;

            int examId = GetInt("ExamID");
            int attemptId = GetInt("AttemptID");
            string examTitle = GetStr("ExamTitle");
            if (string.IsNullOrWhiteSpace(examTitle)) examTitle = "your exam";
            decimal scorePct = GetDec("ScorePercent");
            bool passed = GetInt("Passed") == 1;
            bool needsReview = GetInt("NeedsReview") == 1;

            _ = Task.Run(async () =>
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var notifier = scope.ServiceProvider.GetRequiredService<INotificationService>();
                    var gamif = scope.ServiceProvider.GetRequiredService<IGamificationService>();

                    if (needsReview)
                    {
                        await notifier.NotifyAsync(new NotifyRequest
                        {
                            UserID = studentId,
                            EventCode = "EXAM_RESULT",
                            Title = "Exam submitted",
                            Body = $"Your attempt at <strong>{examTitle}</strong> was submitted and is awaiting grading.",
                            Icon = "fa-solid fa-hourglass-half",
                            SendInApp = true,
                            SendEmail = false
                        });
                        return;
                    }

                    string outcome = passed ? "Passed" : "Not passed";
                    string scoreStr = scorePct.ToString("0.##");

                    await notifier.NotifyAsync(new NotifyRequest
                    {
                        UserID = studentId,
                        EventCode = "EXAM_RESULT",
                        Title = passed ? "You passed!" : "Exam result",
                        Body = $"You scored <strong>{scoreStr}%</strong> on {examTitle} — {outcome}.",
                        Icon = passed ? "fa-solid fa-trophy" : "fa-solid fa-clipboard-check",
                        SendInApp = true,
                        SendEmail = true,
                        EmailTemplateCode = "EXAM_RESULT",
                        Vars = new Dictionary<string, string>
                        {
                            ["ExamTitle"] = examTitle,
                            ["Score"] = scoreStr,
                            ["Outcome"] = outcome
                        }
                    });

                    // ── gamification: exam pass / perfect score + badges ──
                    // dedup-safe per (student, action, examId)
                    if (passed)
                    {
                        await gamif.AwardAsync(studentId, "EXAM_PASS",
                            "Exam", examId.ToString(), "Exam passed");

                        if (scorePct >= 100m)
                            await gamif.AwardAsync(studentId, "PERFECT_SCORE",
                                "Exam", examId.ToString(), "Perfect exam score");
                    }
                    await gamif.EvaluateAndNotifyBadgesAsync(studentId);

                    // auto-issue an exam-pass certificate (idempotent per student+course)
                    if (passed)
                    {
                        int courseId = 0;
                        using (var c = CreateConn())
                        {
                            courseId = await c.ExecuteScalarAsync<int?>(
                                "SELECT CourseID FROM TblExam WHERE ExamID = @e",
                                new { e = examId }) ?? 0;
                        }
                        if (courseId > 0)
                        {
                            var issuer = scope.ServiceProvider.GetRequiredService<ICertificateIssuer>();
                            await issuer.IssueAsync(studentId, courseId, "EXAM",
                                examAttemptId: attemptId, issuedBy: "system", sendEmail: true);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Exam result notification failed (non-fatal). Student={S}", studentId);
                }
            });
        }


        [HttpGet("attempt/{attemptId:int}/result")]
        public async Task<IActionResult> AttemptResult(int attemptId)
        {
            try
            {
                using var conn = CreateConn();

                var rawHeader = await conn.QueryFirstOrDefaultAsync(
                    "SP_ExamAttempt_GetHeader",
                    new { p_AttemptID = attemptId },
                    commandType: CommandType.StoredProcedure);
                if (rawHeader == null) return NotFound(new { Status = "Error", Message = "Attempt not found." });
                var header = ToDict(rawHeader);

                using var review = await conn.QueryMultipleAsync(
                    "SP_ExamAttempt_GetReview",
                    new { p_AttemptID = attemptId },
                    commandType: CommandType.StoredProcedure);
                var questions = ToDictList(await review.ReadAsync());
                var options   = ToDictList(await review.ReadAsync());

                using var bd = await conn.QueryMultipleAsync(
                    "SP_ExamAttempt_GetBreakdown",
                    new { p_AttemptID = attemptId },
                    commandType: CommandType.StoredProcedure);
                var byDifficulty = ToDictList(await bd.ReadAsync());
                var byType       = ToDictList(await bd.ReadAsync());

                return Ok(new { header, questions, options, byDifficulty, byType });
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "AttemptResult failed");
                return StatusCode(500, new { Status = "Error", Message = ex.Message });
            }
        }


        // ─── BUNDLED START — for the runtime's first render ──────────────────────────
        // Does everything the take-exam page needs in one round-trip:
        //   1. Loads exam header (for shuffle flags)
        //   2. Loads all questions (inline + bank, unified)
        //   3. Determines question order (shuffled if exam.ShuffleQuestions=1)
        //   4. Determines option order per question (shuffled if exam.ShuffleOptions=1)
        //   5. Starts (or resumes) the attempt with that order frozen on the row
        //   6. Loads each question's options in the order we just decided
        //   7. Loads any existing answers so the student can resume mid-attempt
        //
        // FIX (Jun 2026): TblExam.IsActive / ShuffleQuestions / ShuffleOptions are
        // tinyint(1), which MySql.Data returns as bool. The old code read them into a
        // dynamic and did (int)exam.IsActive — casting a boxed bool to int through
        // dynamic throws at runtime, so this method crashed (500) on every call.
        // It now reads the header into the typed ExamHeaderDto (bool maps cleanly) and
        // uses the DynInt/DynStr helpers for question/option rows (ints may arrive as
        // long). The resume path is also null-guarded against a missing header.
        // =============================================================================
        [HttpPost("take/start")]
        public async Task<IActionResult> TakeStart([FromBody] AttemptStartRequest req)
        {
            if (req == null || req.ExamID <= 0 || req.StudentID <= 0)
                return BadRequest(new { Status = "Error", Message = "Invalid request." });

            try
            {
                using var conn = CreateConn();
                // Open once and keep open for the whole method so that LAST_INSERT_ID()
                // is always visible on the same physical connection as the INSERT.
                await conn.OpenAsync();

                // 1. exam header — typed, so tinyint(1) maps cleanly to bool
                var exam = await conn.QueryFirstOrDefaultAsync<ExamHeaderDto>(
                    "SELECT ExamID, CourseID, Title, Instructions, PassPercentage, DurationMinutes, MaxAttempts, "
                  + "       ShuffleQuestions, ShuffleOptions, RevealAnswers, IsActive "
                  + "FROM TblExam WHERE ExamID = @e",
                    new { e = req.ExamID });
                if (exam == null)
                    return NotFound(new { Status = "Error", Message = "Exam not found." });
                if (!exam.IsActive)
                    return BadRequest(new { Status = "Error", Message = "Exam is not currently published." });

                // 2. all questions (unified inline + bank)
                var questions = (await conn.QueryAsync<dynamic>(
                    "SP_Exam_GetAllQuestions",
                    new { p_ExamID = req.ExamID },
                    commandType: CommandType.StoredProcedure)).ToList();
                if (questions.Count == 0)
                    return BadRequest(new { Status = "Error", Message = "This exam has no questions." });

                // 3. question order
                var rng = new System.Random();
                var ordered = exam.ShuffleQuestions
                    ? questions.OrderBy(_ => rng.Next()).ToList()
                    : questions;

                // CSV pairs: "source:ref,source:ref,..."
                var orderCsv = string.Join(",", ordered.Select(q => $"{DynStr(q, "Source")}:{DynInt(q, "QuestionRefID")}"));

                // 4. option order per question — kept as (id,text) pairs so we can both
                //    render and freeze the order safely
                var optionsByQ = new Dictionary<string, List<(int Id, string Text)>>();
                var optionOrderMap = new Dictionary<string, List<int>>();
                foreach (var q in ordered)
                {
                    string src = DynStr(q, "Source");
                    int refId = DynInt(q, "QuestionRefID");

                    var opts = (await conn.QueryAsync<dynamic>(
                        "SP_Exam_GetQuestionOptions",
                        new { p_Source = src, p_RefID = refId },
                        commandType: CommandType.StoredProcedure)).ToList();

                    // hide IsCorrect from the runtime — grading is server-side.
                    // Cast the helper results so the tuple is List<(int,string)>, not
                    // List<dynamic> (calling a helper with a dynamic arg yields dynamic).
                    var pairs = opts.Select(o => (Id: (int)DynInt(o, "OptionID"), Text: (string)DynStr(o, "OptionText"))).ToList();
                    if (exam.ShuffleOptions && pairs.Count > 1)
                        pairs = pairs.OrderBy(_ => rng.Next()).ToList();

                    var key = $"{src}:{refId}";
                    optionsByQ[key] = pairs;
                    optionOrderMap[key] = pairs.Select(p => p.Id).ToList();
                }

                // 5. start (or resume) the attempt — done INLINE with plain queries.
                //    The connection is kept open from the top of the try block so
                //    LAST_INSERT_ID() is guaranteed to be on the same physical connection
                //    as the INSERT.
                int code;
                int attemptId;

                // Abandon any InProgress attempts that have passed their expiry time.
                // Without this, a stale expired attempt would always be "resumed" and
                // the student could never start a fresh attempt.
                await conn.ExecuteAsync(
                    "UPDATE TblExamAttempt SET Status = 'Abandoned' " +
                    "WHERE ExamID = @e AND StudentID = @s AND Status = 'InProgress' " +
                    "  AND ExpiresAt IS NOT NULL AND ExpiresAt < NOW()",
                    new { e = req.ExamID, s = req.StudentID });

                // resume an existing in-progress attempt if there is one (non-expired)
                var existingId = await conn.ExecuteScalarAsync<int?>(
                    "SELECT AttemptID FROM TblExamAttempt " +
                    "WHERE ExamID = @e AND StudentID = @s AND Status = 'InProgress' LIMIT 1",
                    new { e = req.ExamID, s = req.StudentID });

                if (existingId.HasValue && existingId.Value > 0)
                {
                    attemptId = existingId.Value;
                    code = 2; // resume
                }
                else
                {
                    // enforce max attempts (0 = unlimited)
                    int used = await conn.ExecuteScalarAsync<int>(
                        "SELECT COUNT(*) FROM TblExamAttempt " +
                        "WHERE ExamID = @e AND StudentID = @s AND Status IN ('Submitted','Graded')",
                        new { e = req.ExamID, s = req.StudentID });

                    if (exam.MaxAttempts > 0 && used >= exam.MaxAttempts)
                        return Ok(new { Success = false, Code = 0, Message = "Maximum attempts reached.", AttemptID = 0 });

                    var optJson = JsonConvert.SerializeObject(optionOrderMap);

                    if (exam.DurationMinutes > 0)
                    {
                        await conn.ExecuteAsync(
                            "INSERT INTO TblExamAttempt " +
                            "(ExamID, StudentID, Status, QuestionOrderCsv, OptionOrderJson, StartedAt, ExpiresAt) " +
                            "VALUES (@e, @s, 'InProgress', @csv, @oj, NOW(), DATE_ADD(NOW(), INTERVAL @d MINUTE))",
                            new { e = req.ExamID, s = req.StudentID, csv = orderCsv, oj = optJson, d = exam.DurationMinutes });
                    }
                    else
                    {
                        await conn.ExecuteAsync(
                            "INSERT INTO TblExamAttempt " +
                            "(ExamID, StudentID, Status, QuestionOrderCsv, OptionOrderJson, StartedAt, ExpiresAt) " +
                            "VALUES (@e, @s, 'InProgress', @csv, @oj, NOW(), NULL)",
                            new { e = req.ExamID, s = req.StudentID, csv = orderCsv, oj = optJson });
                    }

                    attemptId = await conn.ExecuteScalarAsync<int>("SELECT LAST_INSERT_ID()");
                    code = 1; // new
                }

                // 6. on resume, replay the frozen order/options so the page is stable
                if (code == 2)
                {
                    var hdr = await conn.QueryFirstOrDefaultAsync<dynamic>(
                        "SP_ExamAttempt_GetHeader",
                        new { p_AttemptID = attemptId },
                        commandType: CommandType.StoredProcedure);

                    string frozenOrder = hdr != null ? DynStr(hdr, "QuestionOrderCsv") : null;
                    if (string.IsNullOrWhiteSpace(frozenOrder)) frozenOrder = orderCsv;
                    string frozenOptJson = hdr != null ? DynStr(hdr, "OptionOrderJson") : null;

                    if (!string.IsNullOrWhiteSpace(frozenOrder))
                    {
                        var refs = frozenOrder.Split(',')
                            .Select(p => p.Split(':'))
                            .Where(parts => parts.Length == 2)
                            .Select(parts => new { Source = parts[0], RefID = int.Parse(parts[1]) })
                            .ToList();

                        ordered = refs
                            .Select(r => questions.FirstOrDefault(q => DynStr(q, "Source") == r.Source
                                                                    && DynInt(q, "QuestionRefID") == r.RefID))
                            .Where(q => q != null)
                            .ToList();
                    }

                    if (!string.IsNullOrWhiteSpace(frozenOptJson))
                    {
                        var map = JsonConvert.DeserializeObject<Dictionary<string, List<int>>>(frozenOptJson);
                        if (map != null)
                        {
                            foreach (var key in optionsByQ.Keys.ToList())
                            {
                                if (!map.ContainsKey(key)) continue;
                                var byId = optionsByQ[key].ToDictionary(o => o.Id, o => o);
                                var rehydrated = map[key]
                                    .Where(id => byId.ContainsKey(id))
                                    .Select(id => byId[id]).ToList();
                                if (rehydrated.Count > 0) optionsByQ[key] = rehydrated;
                            }
                        }
                    }
                }

                // 7. any answers already saved
                // NOTE: Dapper returns DapperRow (dynamic) objects that System.Text.Json
                // cannot serialize. Convert each row to a plain Dictionary<string,object>
                // before returning so the JSON serializer produces the correct output.
                var rawAnswers = (await conn.QueryAsync(
                    "SP_ExamAttempt_GetAnswers",
                    new { p_AttemptID = attemptId },
                    commandType: CommandType.StoredProcedure)).ToList();

                var answers = rawAnswers
                    .Select(a => ((IDictionary<string, object>)a)
                        .ToDictionary(kv => kv.Key, kv => kv.Value))
                    .ToList();

                // assemble final question payload (already uses anonymous types → serializes fine)
                var payload = ordered.Select(q =>
                {
                    string src = DynStr(q, "Source");
                    int refId = DynInt(q, "QuestionRefID");
                    var key = $"{src}:{refId}";
                    return new
                    {
                        Source = src,
                        QuestionRefID = refId,
                        QuestionText = DynStr(q, "QuestionText"),
                        QuestionType = DynStr(q, "QuestionType"),
                        Difficulty = DynStr(q, "Difficulty"),
                        Marks = DynInt(q, "Marks"),
                        Options = optionsByQ[key].Select(o => new { OptionID = o.Id, OptionText = o.Text }).ToList()
                    };
                }).ToList();

                // Dapper DapperRow is also not serializable by System.Text.Json — convert to Dictionary.
                var rawHeader = await conn.QueryFirstOrDefaultAsync(
                    "SP_ExamAttempt_GetHeader",
                    new { p_AttemptID = attemptId },
                    commandType: CommandType.StoredProcedure);

                var header = rawHeader != null
                    ? ((IDictionary<string, object>)rawHeader)
                        .ToDictionary(kv => kv.Key, kv => kv.Value)
                    : new Dictionary<string, object>();

                // Compute remaining seconds on the server so the client can anchor
                // the expiry to Date.now() — avoids timezone issues with MySQL datetime
                // values that have no timezone indicator (parsed as local by JS).
                int remainingSeconds = 0;
                if (exam.DurationMinutes > 0)
                {
                    if (header.TryGetValue("ExpiresAt", out var expObj) && expObj is DateTime expDt)
                        remainingSeconds = Math.Max(0, (int)(expDt - DateTime.Now).TotalSeconds);
                    else
                        remainingSeconds = exam.DurationMinutes * 60;
                }

                return Ok(new
                {
                    Success = true,
                    Code = code,
                    AttemptID = attemptId,
                    Header = header,
                    Questions = payload,
                    Answers = answers,
                    RemainingSeconds = remainingSeconds
                });
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "TakeStart failed");
                return StatusCode(500, new { Status = "Error", Message = ex.Message });
            }
        }

    }

    public class AttemptStartRequest
    {
        public int ExamID { get; set; }
        public int StudentID { get; set; }
        public string? OrderCsv { get; set; }     // ignored by /take/start (computed server-side)
        public string? OptionJson { get; set; }   // ignored by /take/start
    }
    public class SaveAnswerRequest
    {
        public int AttemptID { get; set; }
        public string? Source { get; set; }
        public int QuestionRefID { get; set; }
        public string? SelectedOptionIDs { get; set; }
        public string? TextAnswer { get; set; }
    }
    public class SubmitAttemptRequest
    {
        public int AttemptID { get; set; }
        public bool AutoSubmitted { get; set; }  // true if the timer fired the submit
    }

    // ── DTOs ──────────────────────────────────────────────────────
    public class ExamSaveRequest
    {
        public int ExamID { get; set; }
        public int CourseID { get; set; }
        public string Title { get; set; }
        public string Instructions { get; set; }
        public int PassPercentage { get; set; } = 50;
        public int DurationMinutes { get; set; } = 30;
        public int MaxAttempts { get; set; }
        public bool ShuffleQuestions { get; set; }
        public bool ShuffleOptions { get; set; }   // shuffle option order per student
        public bool RevealAnswers { get; set; }   // show correct answers after submission
        public string Actor { get; set; }
    }

    public class ExamToggleRequest
    {
        public int ExamID { get; set; }
        public bool IsActive { get; set; }
    }

    public class QuestionSaveRequest
    {
        public int QuestionID { get; set; }
        public int ExamID { get; set; }
        public string QuestionText { get; set; }
        public string QuestionType { get; set; } = "Single";
        public string Difficulty { get; set; } = "Medium";
        public int Marks { get; set; } = 1;
        public int SortOrder { get; set; }
        // Optional — nullable so a null value doesn't trip implicit [Required].
        public string? AcceptableAnswers { get; set; }   // pipe-separated, FillBlank only
        public string? Explanation { get; set; }
        public List<OptionDto> Options { get; set; } = new();
    }

    public class OptionDto
    {
        public string OptionText { get; set; }
        public bool IsCorrect { get; set; }
    }

    public class QuestionDeleteRequest { public int QuestionID { get; set; } }
    public class QuestionReorderRequest { public int ExamID { get; set; } public List<int> QuestionIDs { get; set; } }

    // shapes read from SP_Exam_GetFull
    public class ExamHeaderDto
    {
        public int ExamID { get; set; }
        public int CourseID { get; set; }
        public string Title { get; set; }
        public string Instructions { get; set; }
        public int PassPercentage { get; set; }
        public int DurationMinutes { get; set; }
        public int MaxAttempts { get; set; }
        public bool ShuffleQuestions { get; set; }
        public bool ShuffleOptions { get; set; }
        public bool RevealAnswers { get; set; }
        public bool IsActive { get; set; }
    }

    public class ExamQuestionDto
    {
        public int QuestionID { get; set; }
        public int ExamID { get; set; }
        public string QuestionText { get; set; }
        public string QuestionType { get; set; }
        public string Difficulty { get; set; } = "Medium";
        public int Marks { get; set; }
        public int SortOrder { get; set; }
        public string AcceptableAnswers { get; set; }
        public string Explanation { get; set; }
        public List<ExamOptionDto> Options { get; set; } = new();
    }

    public class ExamOptionDto
    {
        public int OptionID { get; set; }
        public int QuestionID { get; set; }
        public string OptionText { get; set; }
        public bool IsCorrect { get; set; }
        public int SortOrder { get; set; }
    }
    public class BankSaveRequest
    {
        public int BankQuestionID { get; set; }
        public int CourseID { get; set; }
        public string QuestionText { get; set; }
        public string QuestionType { get; set; } = "Single";
        public string Difficulty { get; set; } = "Medium";
        public int Marks { get; set; } = 1;
        // Optional — only FillBlank needs AcceptableAnswers; the others are free-text.
        // Must be nullable, else nullable-ref-type implicit [Required] rejects null.
        public string? AcceptableAnswers { get; set; }
        public string? Explanation { get; set; }
        public string? Tags { get; set; }
        public string? Actor { get; set; }
        public List<OptionDto> Options { get; set; } = new();
    }

    public class BankIdRequest { public int BankQuestionID { get; set; } }
    public class BankLinkRequest { public int ExamID { get; set; } public int BankQuestionID { get; set; } public int? MarksOverride { get; set; } }

    public class BankQuestionDto
    {
        public int BankQuestionID { get; set; }
        public int CourseID { get; set; }
        public string QuestionText { get; set; }
        public string QuestionType { get; set; }
        public string Difficulty { get; set; }
        public int Marks { get; set; }
        public string AcceptableAnswers { get; set; }
        public string Explanation { get; set; }
        public string Tags { get; set; }
        public List<BankOptionDto> Options { get; set; } = new();
    }

    public class BankOptionDto
    {
        public int BankOptionID { get; set; }
        public int BankQuestionID { get; set; }
        public string OptionText { get; set; }
        public bool IsCorrect { get; set; }
        public int SortOrder { get; set; }
    }

}
using Dapper;
using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
using ProximaLMSAPI.Services;
using System.Data;
using System.IO.Compression;
using System.Text;

namespace ProximaLMSAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class InvoiceController : ControllerBase
    {
        private readonly IConfiguration _config;
        private readonly IInvoiceService _inv;
        private readonly ILogger<InvoiceController> _logger;

        public InvoiceController(IConfiguration config, IInvoiceService inv, ILogger<InvoiceController> logger)
        {
            _config = config; _inv = inv; _logger = logger;
        }

        private IDbConnection Conn() => new MySqlConnection(_config.GetConnectionString("ConnectionString"));

        // POST api/invoice/generate  Body: { OrderID }
        // Idempotent — returns existing invoice if already generated.
        [HttpPost("generate")]
        public async Task<IActionResult> Generate([FromBody] GenerateRequest req)
        {
            if (req == null || string.IsNullOrWhiteSpace(req.OrderID))
                return BadRequest(new { success = false, message = "OrderID is required." });
            try
            {
                using var conn = Conn();
                var p = new DynamicParameters();
                p.Add("p_OrderID", req.OrderID);
                p.Add("p_InvoiceID", dbType: DbType.Int32, direction: ParameterDirection.Output);
                p.Add("p_InvoiceNo", dbType: DbType.String, size: 40, direction: ParameterDirection.Output);
                await conn.ExecuteAsync("SP_Invoice_Generate", p, commandType: CommandType.StoredProcedure);

                int id = p.Get<int>("p_InvoiceID");
                if (id <= 0) return BadRequest(new { success = false, message = "No paid order found for that OrderID." });
                return Ok(new { success = true, invoiceId = id, invoiceNo = p.Get<string>("p_InvoiceNo") });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Invoice generate failed for {Order}", req.OrderID);
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        // GET api/invoice/student/{studentId}
        [HttpGet("student/{studentId:int}")]
        public async Task<IActionResult> ListByStudent(int studentId)
        {
            using var conn = Conn();
            var rows = await conn.QueryAsync("SP_Invoice_ListByStudent",
                new { p_StudentID = studentId }, commandType: CommandType.StoredProcedure);
            return Ok(new { success = true, data = rows });
        }

        // GET api/invoice/all?from=&to=
        [HttpGet("all")]
        public async Task<IActionResult> ListAll([FromQuery] DateTime? from, [FromQuery] DateTime? to)
        {
            using var conn = Conn();
            var rows = await conn.QueryAsync("SP_Invoice_ListAll",
                new { p_From = from, p_To = to }, commandType: CommandType.StoredProcedure);
            return Ok(new { success = true, data = rows });
        }

        // GET api/invoice/{id}/download
        [HttpGet("{id:int}/download")]
        public async Task<IActionResult> Download(int id)
        {
            try
            {
                var data = await Load(id);
                if (data == null)
                    return NotFound(new { success = false, message = $"Invoice {id} not found." });
                var pdf = _inv.BuildPdf(data);
                return File(pdf, "application/pdf", $"{data.InvoiceNo}.pdf");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Invoice download failed for id={Id}", id);
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        // GET api/invoice/export?from=&to=   → ZIP { invoices.csv + each PDF }
        [HttpGet("export")]
        public async Task<IActionResult> Export([FromQuery] DateTime? from, [FromQuery] DateTime? to)
        {
            using var conn = Conn();
            var rows = (await conn.QueryAsync("SP_Invoice_ListAll",
                new { p_From = from, p_To = to }, commandType: CommandType.StoredProcedure)).ToList();

            if (rows.Count == 0)
                return NotFound(new { success = false, message = "No invoices in that range." });

            using var zipStream = new MemoryStream();
            using (var zip = new ZipArchive(zipStream, ZipArchiveMode.Create, true))
            {
                // CSV summary
                var csv = new StringBuilder();
                csv.AppendLine("InvoiceNo,Date,Buyer,Course,Taxable,CGST,SGST,IGST,Total,InterState");
                foreach (var r in rows)
                {
                    var data = MapRow(r);
                    csv.AppendLine(string.Join(",",
                        Csv(data.InvoiceNo), Csv(data.InvoiceDate.ToString("yyyy-MM-dd")),
                        Csv(data.BuyerName), Csv(data.CourseTitle),
                        data.TaxableValue, data.CGST, data.SGST, data.IGST,
                        data.TotalAmount, data.IsInterState ? "Y" : "N"));

                    // each PDF
                    var pdf = _inv.BuildPdf(data);
                    var entry = zip.CreateEntry($"pdf/{data.InvoiceNo}.pdf", CompressionLevel.Optimal);
                    using var es = entry.Open();
                    es.Write(pdf, 0, pdf.Length);
                }
                var csvEntry = zip.CreateEntry("invoices.csv", CompressionLevel.Optimal);
                using var cs = new StreamWriter(csvEntry.Open(), Encoding.UTF8);
                cs.Write(csv.ToString());
            }

            zipStream.Position = 0;
            var fname = $"invoices_{DateTime.Now:yyyyMMdd_HHmm}.zip";
            return File(zipStream.ToArray(), "application/zip", fname);
        }

        // ── helpers ──────────────────────────────────────────
        private async Task<InvoiceRenderData?> Load(int id)
        {
            using var conn = Conn();
            var row = await conn.QuerySingleOrDefaultAsync("SP_Invoice_Get",
                new { p_InvoiceID = id }, commandType: CommandType.StoredProcedure);
            return row == null ? null : MapRow(row);
        }

        private static InvoiceRenderData MapRow(dynamic r) => new InvoiceRenderData
        {
            InvoiceNo      = (string)r.InvoiceNo,
            InvoiceDate    = (DateTime)r.InvoiceDate,
            PaymentID      = (r.PaymentID as string) ?? "",
            SellerName     = (r.SellerName as string) ?? "ProximaLMS",
            SellerGSTIN    = (r.SellerGSTIN as string) ?? "",
            SellerState    = (r.SellerState as string) ?? "",
            BuyerName      = (string)r.BuyerName,
            BuyerEmail     = (r.BuyerEmail as string) ?? "",
            BuyerState     = (r.BuyerStateCode as string) ?? "",
            BuyerGSTIN     = (r.BuyerGSTIN as string) ?? "",
            CourseTitle    = (string)r.CourseTitle,
            CouponCode     = r.CouponCode as string,
            OriginalAmount = Convert.ToDecimal(r.OriginalAmount),
            DiscountAmount = Convert.ToDecimal(r.DiscountAmount),
            TaxableValue   = Convert.ToDecimal(r.TaxableValue),
            CGST           = Convert.ToDecimal(r.CGST),
            SGST           = Convert.ToDecimal(r.SGST),
            IGST           = Convert.ToDecimal(r.IGST),
            TotalAmount    = Convert.ToDecimal(r.TotalAmount),
            IsInterState   = Convert.ToInt32(r.IsInterState) == 1
        };

        private static string Csv(string s)
        {
            s ??= "";
            return s.Contains(',') || s.Contains('"') ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
        }

        public class GenerateRequest { public string OrderID { get; set; } = ""; }
    }
}

using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace ProximaLMSAPI.Services
{
    public class InvoiceRenderData
    {
        public string InvoiceNo    { get; set; } = "";
        public DateTime InvoiceDate { get; set; } = DateTime.Now;
        public string PaymentID    { get; set; } = "";

        public string SellerName   { get; set; } = "ProximaLMS";
        public string SellerGSTIN  { get; set; } = "";
        public string SellerState  { get; set; } = "";

        public string BuyerName    { get; set; } = "";
        public string BuyerEmail   { get; set; } = "";
        public string BuyerState   { get; set; } = "";
        public string BuyerGSTIN   { get; set; } = "";

        public string CourseTitle  { get; set; } = "";
        public string? CouponCode  { get; set; }

        public decimal OriginalAmount { get; set; }
        public decimal DiscountAmount { get; set; }
        public decimal TaxableValue   { get; set; }
        public decimal CGST  { get; set; }
        public decimal SGST  { get; set; }
        public decimal IGST  { get; set; }
        public decimal TotalAmount    { get; set; }
        public bool    IsInterState   { get; set; }
    }

    public interface IInvoiceService
    {
        byte[] BuildPdf(InvoiceRenderData d);
    }

    public class InvoiceService : IInvoiceService
    {
        private readonly string _logoPath;

        public InvoiceService(IWebHostEnvironment env)
        {
            _logoPath = Path.Combine(env.WebRootPath ?? env.ContentRootPath, "images", "logo.png");
        }

        private static string Rs(decimal v) => "Rs. " + v.ToString("N2");

        // Logo native ratio 16727 x 4666  ≈ 3.585 : 1
        private const float LogoW = 165f;
        private const float LogoH = 46f;

        // ── Palette ───────────────────────────────────────────────
        private static readonly string C1   = "#3D1054";   // deep purple (ink accents / total)
        private static readonly string C2   = "#7C3AED";   // brand purple (labels / table head)
        private static readonly string C3   = "#A855F7";   // accent purple (rules)
        private static readonly string CBg   = "#FAF8FF";  // whisper purple bg
        private static readonly string CBd   = "#E9E3F7";  // soft border
        private static readonly string CMut  = "#94A3B8";  // muted grey
        private static readonly string CInk  = "#1E1633";  // near-black ink
        private static readonly string CSub  = "#475569";  // subtext grey
        private static readonly string CGrnBg = "#DCFCE7"; // paid badge bg
        private static readonly string CGrn   = "#15803D"; // paid badge text
        private static readonly string CRed   = "#DC2626"; // discount

        public byte[] BuildPdf(InvoiceRenderData d)
        {
            bool hasLogo = File.Exists(_logoPath);
            bool isPaid  = !string.IsNullOrWhiteSpace(d.PaymentID);

            var doc = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.MarginHorizontal(45);
                    page.MarginTop(0);
                    page.MarginBottom(34);
                    page.DefaultTextStyle(x => x.FontFamily("Helvetica").FontSize(10).FontColor(CInk));

                    // ════════════════════════════════════════════════
                    // HEADER — light, logo left, INVOICE right
                    // ════════════════════════════════════════════════
                    page.Header().Column(head =>
                    {
                        // thin brand bar pinned to the very top edge
                        head.Item().Height(6).Background(C2);

                        head.Item().PaddingTop(26).Row(row =>
                        {
                            // Logo (native aspect, never distorted)
                            row.RelativeItem().AlignMiddle().Column(c =>
                            {
                                if (hasLogo)
                                    c.Item().Width(LogoW).Height(LogoH)
                                        .Image(_logoPath).FitUnproportionally()
                                        .WithCompressionQuality(ImageCompressionQuality.High);
                                else
                                    c.Item().Text(d.SellerName)
                                        .FontSize(22).Bold().FontColor(C1);
                            });

                            // INVOICE title block
                            row.RelativeItem().AlignRight().Column(c =>
                            {
                                c.Item().AlignRight()
                                    .Text("INVOICE")
                                    .FontSize(30).Bold().FontColor(C1);
                                c.Item().PaddingTop(2).AlignRight()
                                    .Text(d.InvoiceNo)
                                    .FontSize(10).SemiBold().FontColor(C2);
                                c.Item().PaddingTop(1).AlignRight()
                                    .Text(d.InvoiceDate.ToString("dd MMMM yyyy"))
                                    .FontSize(9).FontColor(CMut);
                            });
                        });

                        head.Item().PaddingTop(18).Height(1).Background(CBd);
                    });

                    // ════════════════════════════════════════════════
                    // CONTENT
                    // ════════════════════════════════════════════════
                    page.Content().PaddingTop(22).Column(col =>
                    {
                        // ── Parties + PAID badge ─────────────────────
                        col.Item().Row(row =>
                        {
                            // BILL FROM
                            row.RelativeItem().Column(c =>
                            {
                                Label(c, "BILL FROM");
                                c.Item().PaddingTop(8)
                                    .Text(d.SellerName).FontSize(13).Bold().FontColor(CInk);
                                if (!string.IsNullOrWhiteSpace(d.SellerGSTIN))
                                    c.Item().PaddingTop(4)
                                        .Text($"GSTIN  {d.SellerGSTIN}").FontSize(9).FontColor(CSub);
                                if (!string.IsNullOrWhiteSpace(d.SellerState))
                                    c.Item().PaddingTop(2)
                                        .Text($"State  {d.SellerState}").FontSize(9).FontColor(CSub);
                            });

                            row.ConstantItem(28);

                            // BILL TO
                            row.RelativeItem().Column(c =>
                            {
                                Label(c, "BILL TO");
                                c.Item().PaddingTop(8)
                                    .Text(d.BuyerName).FontSize(13).Bold().FontColor(CInk);
                                if (!string.IsNullOrWhiteSpace(d.BuyerEmail))
                                    c.Item().PaddingTop(4)
                                        .Text(d.BuyerEmail).FontSize(9).FontColor(CSub);
                                if (!string.IsNullOrWhiteSpace(d.BuyerState))
                                    c.Item().PaddingTop(2)
                                        .Text($"State  {d.BuyerState}").FontSize(9).FontColor(CSub);
                                if (!string.IsNullOrWhiteSpace(d.BuyerGSTIN))
                                    c.Item().PaddingTop(2)
                                        .Text($"GSTIN  {d.BuyerGSTIN}").FontSize(9).FontColor(CSub);
                            });

                            // PAID stamp
                            row.ConstantItem(96).AlignTop().AlignRight().Column(c =>
                            {
                                if (isPaid)
                                    c.Item().AlignRight().Background(CGrnBg)
                                        .Border(1).BorderColor("#86EFAC")
                                        .PaddingHorizontal(16).PaddingVertical(8)
                                        .Text("PAID")
                                        .FontSize(14).Bold().FontColor(CGrn);
                            });
                        });

                        // ── Meta strip ───────────────────────────────
                        col.Item().PaddingTop(22).Background(CBg)
                            .Border(1).BorderColor(CBd)
                            .PaddingVertical(13).PaddingHorizontal(4).Row(row =>
                        {
                            Meta(row, "INVOICE NO", d.InvoiceNo);
                            Divider(row);
                            Meta(row, "DATE", d.InvoiceDate.ToString("dd MMM yyyy"));
                            if (isPaid)
                            {
                                Divider(row);
                                Meta(row, "PAYMENT REF", d.PaymentID);
                            }
                            Divider(row);
                            Meta(row, "SUPPLY TYPE",
                                d.IsInterState ? "Inter-State" : "Intra-State");
                        });

                        // ── Items table ──────────────────────────────
                        col.Item().PaddingTop(26).Table(table =>
                        {
                            table.ColumnsDefinition(cd =>
                            {
                                cd.RelativeColumn(6);
                                cd.RelativeColumn(2);
                            });

                            table.Header(h =>
                            {
                                h.Cell().Background(C1)
                                    .PaddingHorizontal(16).PaddingVertical(11)
                                    .Text("DESCRIPTION")
                                    .FontSize(8.5f).Bold().FontColor("#FFFFFF");
                                h.Cell().Background(C1)
                                    .PaddingHorizontal(16).PaddingVertical(11).AlignRight()
                                    .Text("AMOUNT")
                                    .FontSize(8.5f).Bold().FontColor("#FFFFFF");
                            });

                            // Course line
                            table.Cell().BorderBottom(1).BorderColor(CBd)
                                .PaddingHorizontal(16).PaddingVertical(14).Column(c =>
                                {
                                    c.Item().Text(d.CourseTitle)
                                        .FontSize(11).SemiBold().FontColor(CInk);
                                    c.Item().PaddingTop(3)
                                        .Text("Online Course  ·  Lifetime Access")
                                        .FontSize(8.5f).FontColor(CMut);
                                });
                            table.Cell().BorderBottom(1).BorderColor(CBd)
                                .PaddingHorizontal(16).PaddingVertical(14)
                                .AlignRight().AlignMiddle()
                                .Text(Rs(d.OriginalAmount))
                                .FontSize(11).SemiBold().FontColor(CInk);

                            // Discount line
                            if (d.DiscountAmount > 0)
                            {
                                string lbl = string.IsNullOrWhiteSpace(d.CouponCode)
                                    ? "Discount Applied"
                                    : $"Coupon Discount  ·  {d.CouponCode}";

                                table.Cell().BorderBottom(1).BorderColor(CBd)
                                    .PaddingHorizontal(16).PaddingVertical(11)
                                    .Text(lbl).FontSize(9.5f).FontColor(CSub);
                                table.Cell().BorderBottom(1).BorderColor(CBd)
                                    .PaddingHorizontal(16).PaddingVertical(11).AlignRight()
                                    .Text("- " + Rs(d.DiscountAmount))
                                    .FontSize(9.5f).SemiBold().FontColor(CRed);
                            }
                        });

                        // ── Totals (right) ───────────────────────────
                        col.Item().PaddingTop(16).Row(row =>
                        {
                            // left: thank-you note
                            row.RelativeItem().AlignBottom().Column(c =>
                            {
                                c.Item().Text("Thank you for learning with us!")
                                    .FontSize(11).Bold().FontColor(C1);
                                c.Item().PaddingTop(4)
                                    .Text("Your purchase helps us build better courses.")
                                    .FontSize(9).FontColor(CMut);
                            });

                            row.ConstantItem(258).Column(c =>
                            {
                                TotalRow(c, "Taxable Value", Rs(d.TaxableValue), false);
                                if (d.IsInterState)
                                    TotalRow(c, "IGST @ 18%", Rs(d.IGST), false);
                                else
                                {
                                    TotalRow(c, "CGST @ 9%", Rs(d.CGST), false);
                                    TotalRow(c, "SGST @ 9%", Rs(d.SGST), false);
                                }

                                // grand total
                                c.Item().PaddingTop(8).Background(C1)
                                    .PaddingHorizontal(16).PaddingVertical(13).Row(r =>
                                    {
                                        r.RelativeItem().AlignMiddle()
                                            .Text("TOTAL PAID")
                                            .FontSize(10).Bold().FontColor("#E9D5FF");
                                        r.AutoItem().AlignMiddle()
                                            .Text(Rs(d.TotalAmount))
                                            .FontSize(18).Bold().FontColor("#FFFFFF");
                                    });
                            });
                        });

                        // ── Note ─────────────────────────────────────
                        col.Item().PaddingTop(26).BorderTop(1).BorderColor(CBd)
                            .PaddingTop(10).Text(t =>
                            {
                                t.Span("Note  ").FontSize(8).Bold().FontColor(C2);
                                t.Span("This is a system-generated invoice and does not require a " +
                                       "physical signature. All amounts are inclusive of applicable " +
                                       "GST as per Indian tax regulations.")
                                    .FontSize(8).FontColor(CMut);
                            });
                    });

                    // ════════════════════════════════════════════════
                    // FOOTER
                    // ════════════════════════════════════════════════
                    page.Footer().Row(row =>
                    {
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text(t =>
                            {
                                t.Span("ProximaLMS").FontSize(8).SemiBold().FontColor(C1);
                                t.Span("  ·  AI Driven Education Personalized For You")
                                    .FontSize(8).FontColor(CMut);
                            });
                            c.Item().PaddingTop(2)
                                .Text("www.proximalms.com")
                                .FontSize(7.5f).FontColor(C2);
                        });

                        row.AutoItem().AlignRight().AlignBottom().Text(t =>
                        {
                            t.Span(d.InvoiceNo).FontSize(8).FontColor(CMut);
                            t.Span("   Page ").FontSize(8).FontColor(CMut);
                            t.CurrentPageNumber().FontSize(8).FontColor(CMut);
                            t.Span(" / ").FontSize(8).FontColor(CMut);
                            t.TotalPages().FontSize(8).FontColor(CMut);
                        });
                    });
                });
            });

            return doc.GeneratePdf();
        }

        // ── small helpers ─────────────────────────────────────────
        private static void Label(ColumnDescriptor c, string text)
        {
            c.Item().Text(text).FontSize(8).Bold().FontColor(C2);
            c.Item().PaddingTop(4).Width(26).Height(2).Background(C3);
        }

        private static void Meta(RowDescriptor row, string label, string value)
        {
            row.RelativeItem().PaddingHorizontal(12).Column(c =>
            {
                c.Item().Text(label).FontSize(7).Bold().FontColor(C2);
                c.Item().PaddingTop(3).Text(value).FontSize(9).SemiBold().FontColor(CInk);
            });
        }

        private static void Divider(RowDescriptor row)
        {
            row.ConstantItem(1).LineVertical(1).LineColor(CBd);
        }

        private static void TotalRow(ColumnDescriptor c, string label, string value, bool strong)
        {
            c.Item().BorderBottom(1).BorderColor(CBd)
                .PaddingVertical(9).Row(r =>
                {
                    r.RelativeItem().Text(label).FontSize(9.5f).FontColor(CSub);
                    r.AutoItem().Text(value)
                        .FontSize(9.5f).SemiBold().FontColor(CInk);
                });
        }
    }
}

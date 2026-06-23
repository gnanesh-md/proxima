using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using QRCoder;

namespace ProximaLMSAPI.Services
{
    public class CertificateRenderData
    {
        public string CertificateNo { get; set; } = "";
        public string StudentName { get; set; } = "";
        public string CourseTitle { get; set; } = "";
        public DateTime IssuedDate { get; set; } = DateTime.Now;
        public string Source { get; set; } = "COURSE";
        public string VerifyUrl { get; set; } = "";          // public verification URL (encoded into QR)
        public string? BackgroundImageUrl { get; set; }          // optional, from template
        public string Orientation { get; set; } = "landscape";
    }

    public interface ICertificateService
    {
        byte[] BuildPdf(CertificateRenderData data);
        byte[] BuildQrPng(string content);
    }

    public class CertificateService : ICertificateService
    {
        // ── palette ──────────────────────────────────────────────
        private static readonly string C1    = "#3D1054";   // deep purple (frame / ink)
        private static readonly string C2    = "#7C3AED";   // brand purple (course)
        private static readonly string GOLD  = "#B8902E";   // antique gold
        private static readonly string GOLDL = "#D4AF37";   // bright gold
        private static readonly string CInk  = "#1F1A2E";   // near-black
        private static readonly string CSub  = "#4B5563";   // subtext
        private static readonly string CMut  = "#9197A3";   // muted
        private static readonly string CBd   = "#E7E1F3";   // faint divider

        // ── QR as PNG bytes ──────────────────────────────────
        public byte[] BuildQrPng(string content)
        {
            using var gen = new QRCodeGenerator();
            using var qrData = gen.CreateQrCode(content, QRCodeGenerator.ECCLevel.Q);
            var png = new PngByteQRCode(qrData);
            return png.GetGraphic(20); // 20 px per module
        }

        // ── Certificate PDF ──────────────────────────────────
        public byte[] BuildPdf(CertificateRenderData d)
        {
            var qrPng = BuildQrPng(d.VerifyUrl);
            byte[]? bg   = TryLoadBackground(d.BackgroundImageUrl);
            byte[]? logo = TryLoadLogo();
            bool isExam  = d.Source == "EXAM";

            var doc = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(d.Orientation?.ToLower() == "portrait"
                        ? PageSizes.A4.Portrait()
                        : PageSizes.A4.Landscape());
                    page.Margin(0);
                    page.DefaultTextStyle(t => t.FontFamily("Helvetica").FontColor(CInk));

                    page.Content().Layers(layers =>
                    {
                        // optional background image fills the page
                        if (bg != null)
                            layers.Layer().Image(bg).FitArea();

                        var content = layers.PrimaryLayer();
                        if (bg == null) content = content.Background("#FFFFFF");

                        // ── ornamental double frame ──────────────
                        content
                            .Padding(15)
                            .Border(7).BorderColor(C1)         // thick purple outer band
                            .Padding(5)
                            .Border(1.4f).BorderColor(GOLD)    // thin gold inner line
                            .Padding(28)
                            .Column(col =>
                            {
                                // ── brand emblem ─────────────────
                                col.Item().AlignCenter().Height(40).Column(c =>
                                {
                                    if (logo != null)
                                        c.Item().AlignCenter().Width(150).Height(40)
                                            .Image(logo).FitUnproportionally();
                                    else
                                    {
                                        c.Item().AlignCenter().Text("ProximaLMS")
                                            .FontSize(17).Bold().FontColor(C2);
                                        c.Item().AlignCenter().Text("AI DRIVEN EDUCATION")
                                            .FontSize(6.5f).FontColor(CMut).LetterSpacing(0.12f);
                                    }
                                });

                                // ── eyebrow ──────────────────────
                                col.Item().PaddingTop(12).AlignCenter().Text("PROUDLY PRESENTS THIS")
                                    .FontSize(9.5f).SemiBold().FontColor(GOLD).LetterSpacing(0.10f);

                                // ── title ────────────────────────
                                col.Item().PaddingTop(4).AlignCenter().Text("CERTIFICATE OF COMPLETION")
                                    .FontSize(33).Bold().FontColor(C1).LetterSpacing(0.03f);

                                // ── gold divider with center bar ─
                                col.Item().PaddingTop(8).AlignCenter().Width(300).Height(8).Row(r =>
                                {
                                    r.RelativeItem().AlignMiddle().LineHorizontal(1).LineColor(GOLD);
                                    r.ConstantItem(46).PaddingHorizontal(11).AlignMiddle().Height(3).Background(GOLDL);
                                    r.RelativeItem().AlignMiddle().LineHorizontal(1).LineColor(GOLD);
                                });

                                // ── presented to ─────────────────
                                col.Item().PaddingTop(14).AlignCenter()
                                    .Text("This certificate is proudly presented to")
                                    .FontSize(12).Italic().FontColor(CSub);

                                // ── recipient name ───────────────
                                col.Item().PaddingTop(8).AlignCenter().Text(d.StudentName)
                                    .FontSize(40).Bold().FontColor(CInk);

                                col.Item().PaddingTop(5).AlignCenter().Width(380)
                                    .LineHorizontal(1.4f).LineColor(GOLD);

                                // ── course ───────────────────────
                                col.Item().PaddingTop(13).AlignCenter()
                                    .Text("for successfully completing the course")
                                    .FontSize(12).FontColor(CSub);
                                col.Item().PaddingTop(4).AlignCenter().Text(d.CourseTitle)
                                    .FontSize(21).Bold().FontColor(C2);
                                col.Item().PaddingTop(5).AlignCenter().Text(isExam
                                        ? "by passing the final assessment with distinction"
                                        : "having fulfilled all course requirements")
                                    .FontSize(10).Italic().FontColor(CMut);

                                // ── footer row: signature | seal | QR ─
                                col.Item().PaddingTop(24).Row(row =>
                                {
                                    // left — signature
                                    row.RelativeItem().AlignBottom().Column(c =>
                                    {
                                        c.Item().Width(160).LineHorizontal(1).LineColor("#9CA3AF");
                                        c.Item().PaddingTop(4).Text("Authorized Signatory")
                                            .FontSize(9).SemiBold().FontColor(CSub);
                                        c.Item().Text("ProximaLMS  ·  Academic Office")
                                            .FontSize(7.5f).FontColor(CMut);
                                    });

                                    // center — official seal
                                    row.RelativeItem().AlignCenter().AlignBottom()
                                        .Width(74).Height(74).Background(C1)
                                        .Padding(4).Border(1.6f).BorderColor(GOLDL)
                                        .AlignMiddle().Column(b =>
                                        {
                                            b.Item().AlignCenter().Text("OFFICIAL")
                                                .FontSize(7).Bold().FontColor(GOLDL).LetterSpacing(0.12f);
                                            b.Item().AlignCenter().Text("CERTIFIED")
                                                .FontSize(8.5f).Bold().FontColor("#FFFFFF").LetterSpacing(0.06f);
                                            b.Item().PaddingTop(2).AlignCenter().Text(d.IssuedDate.ToString("yyyy"))
                                                .FontSize(13).Bold().FontColor(GOLDL);
                                        });

                                    // right — verification QR
                                    row.RelativeItem().AlignRight().AlignBottom().Column(c =>
                                    {
                                        c.Item().AlignRight().Width(62).Image(qrPng);
                                        c.Item().PaddingTop(2).AlignRight().Text("Scan to verify")
                                            .FontSize(7).FontColor(CMut);
                                    });
                                });

                                // ── micro footer ─────────────────
                                col.Item().PaddingTop(14).LineHorizontal(0.5f).LineColor(CBd);
                                col.Item().PaddingTop(6).Row(row =>
                                {
                                    row.RelativeItem().Text(t =>
                                    {
                                        t.Span("CERTIFICATE NO   ").FontSize(7.5f).SemiBold().FontColor(CSub);
                                        t.Span(d.CertificateNo).FontSize(7.5f).FontColor(CMut);
                                    });
                                    row.RelativeItem().AlignCenter().Text(t =>
                                    {
                                        t.Span("ISSUED   ").FontSize(7.5f).SemiBold().FontColor(CSub);
                                        t.Span(d.IssuedDate.ToString("dd MMM yyyy")).FontSize(7.5f).FontColor(CMut);
                                    });
                                    row.RelativeItem().AlignRight().Text(t =>
                                    {
                                        t.Span("BASIS   ").FontSize(7.5f).SemiBold().FontColor(CSub);
                                        t.Span(isExam ? "Exam Passed" : "Course Completed")
                                            .FontSize(7.5f).FontColor(CMut);
                                    });
                                });
                            });
                    });
                });
            });

            return doc.GeneratePdf();
        }

        // best-effort: read a wwwroot-relative or absolute path; ignore on failure
        private byte[]? TryLoadBackground(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;
            try
            {
                if (url.StartsWith("http")) return null; // skip remote fetch in this build
                var rel = url.TrimStart('/', '~');
                var path = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", rel);
                return File.Exists(path) ? File.ReadAllBytes(path) : null;
            }
            catch { return null; }
        }

        // best-effort: load the brand logo from wwwroot/images/logo.png
        private byte[]? TryLoadLogo()
        {
            try
            {
                var path = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "images", "logo.png");
                return File.Exists(path) ? File.ReadAllBytes(path) : null;
            }
            catch { return null; }
        }
    }
}

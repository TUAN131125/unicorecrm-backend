using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using PdfSharp.Fonts;
using PdfSharp.Drawing;

// Dedicated bounded rendering process: no network, database, HTML, path or caller-provided assets.
Console.InputEncoding = Encoding.UTF8;
try
{
    using var input = new MemoryStream();
    var stdin = Console.OpenStandardInput(); var buffer = new byte[8192]; int count;
    while ((count = await stdin.ReadAsync(buffer)) != 0)
    { if (input.Length + count > 8 * 1024 * 1024) return 22; input.Write(buffer, 0, count); }
    using var json = JsonDocument.Parse(input.ToArray()); var root = json.RootElement;
    var q = root.GetProperty("quote"); var seller = root.GetProperty("seller"); var buyer = root.GetProperty("buyer");
    var currency = Text(q, "currency"); var precision = currency switch { "VND" or "JPY" => 0, "USD" or "EUR" => 2, "KWD" => 3, _ => throw new InvalidOperationException("Unsupported precision") };
    string Money(JsonElement value) => decimal.Parse(Text(value, "amount"), CultureInfo.InvariantCulture).ToString("N" + precision, CultureInfo.InvariantCulture) + " " + currency;
    string UnitPrice(JsonElement value)
    {
        var amount = decimal.Parse(Text(value, "amount"), CultureInfo.InvariantCulture);
        var scale = (decimal.GetBits(amount)[3] >> 16) & 0x7f;
        return amount.ToString("N" + Math.Max(precision, Math.Min(6, scale)), CultureInfo.InvariantCulture) + " " + currency;
    }
    var lines = q.GetProperty("lineItems"); if (lines.GetArrayLength() > 500) return 22;
    GlobalFontSettings.FontResolver = new EmbeddedFonts();
    var document = new Document(); document.Info.Title = "Báo giá " + Text(q, "quoteNumber"); document.Info.Author = Text(seller, "legalName");
    var normal = document.Styles[StyleNames.Normal]!; normal.Font.Name = "Noto Sans"; normal.Font.Size = 9; normal.ParagraphFormat.SpaceAfter = Unit.FromMillimeter(2);
    var section = document.AddSection(); section.PageSetup.PageFormat = PageFormat.A4;
    section.PageSetup.LeftMargin = section.PageSetup.RightMargin = Unit.FromMillimeter(18);
    section.PageSetup.TopMargin = section.PageSetup.BottomMargin = Unit.FromMillimeter(20);
    var footer = section.Footers.Primary.AddParagraph(); footer.AddText("Trang "); footer.AddPageField(); footer.AddText(" / "); footer.AddNumPagesField();
    footer.Format.Alignment = ParagraphAlignment.Right; footer.Format.Font.Size = 8;
    Paragraph Para(string text, bool bold = false) { var p = section.AddParagraph(text); p.Format.Font.Bold = bold; return p; }
    Para(Text(seller, "legalName"), true).Format.Font.Size = 15;
    if (Text(seller, "displayName") != Text(seller, "legalName")) Para(Text(seller, "displayName"));
    Para(Text(seller, "address")); Para(string.Join(" · ", new[] { Text(seller, "email"), Text(seller, "phone"), Text(seller, "website") }.Where(x => x.Length != 0)));
    if (Text(seller, "taxId").Length != 0) Para("Mã số thuế: " + Text(seller, "taxId"));
    Para("BÁO GIÁ " + Text(q, "quoteNumber"), true).Format.SpaceBefore = Unit.FromMillimeter(4);
    Para(Text(q, "title"), true); Para("Phiên bản: " + Text(q, "quoteRevision") + " · Ngày phát hành (UTC): " + Text(root, "issuedAt"));
    Para("Có hiệu lực đến hết ngày Workspace (" + Text(root, "workspaceTimeZone") + "): " + (Text(q, "validUntil") is { Length: > 0 } date ? date : "Không đặt ngày"));
    Para("Khách hàng: " + Text(buyer, "displayName"), true);
    Para(string.Join(" · ", new[] { Text(buyer, "email"), Text(buyer, "phone") }.Where(x => x.Length != 0)));
    if (Text(q, "sourceDealId").Length != 0) Para("Cơ hội tham chiếu: " + Text(q, "sourceDealId"));
    var table = section.AddTable(); table.Borders.Width = 0.4; table.Borders.Color = Colors.LightGray;
    foreach (var width in new[] { 8, 64, 14, 28, 24, 36 }) table.AddColumn(Unit.FromMillimeter(width));
    var heading = table.AddRow(); heading.HeadingFormat = true; heading.Shading.Color = Colors.AliceBlue; heading.Format.Font.Bold = true;
    var labels = new[] { "STT", "Sản phẩm / Dịch vụ", "SL", "Đơn giá", "Thuế", "Thành tiền" };
    for (var i = 0; i < labels.Length; i++) heading.Cells[i].AddParagraph(labels[i]);
    var index = 0;
    foreach (var line in lines.EnumerateArray())
    {
        index++; var description = Text(line, "productNameSnapshot") + "\n" + Text(line, "descriptionSnapshot");
        if (Text(line, "skuSnapshot").Length != 0) description += "\nSKU: " + Text(line, "skuSnapshot");
        if (Text(line, "billingCycleSnapshot").Length != 0) description += "\nChu kỳ tính phí: " + Label(Text(line, "billingCycleSnapshot"));
        description += "\nĐơn vị: " + (Text(line, "unitSnapshot") is { Length: > 0 } unit ? unit : "— (chưa ghi nhận)");
        description += "\nGiảm giá: " + Text(line, "discountRate") + "% · " + Money(line.GetProperty("lineDiscountAmount"));
        description += "\nThuế suất: " + Text(line, "taxRate") + "% · " + Text(line, "taxMode");
        var parts = Chunks(description, 420).ToArray();
        for (var n = 0; n < parts.Length; n++)
        {
            var row = table.AddRow(); row.TopPadding = row.BottomPadding = Unit.FromMillimeter(2);
            row.Cells[0].AddParagraph(n == 0 ? index.ToString(CultureInfo.InvariantCulture) : "...");
            row.Cells[1].AddParagraph(parts[n]);
            if (n == 0)
            {
                row.Cells[2].AddParagraph(Text(line, "quantity")); row.Cells[3].AddParagraph(UnitPrice(line.GetProperty("unitPrice")));
                row.Cells[4].AddParagraph(Money(line.GetProperty("lineTaxAmount"))); row.Cells[5].AddParagraph(Money(line.GetProperty("lineTotal")));
            }
        }
    }
    Para("Tạm tính: " + Money(q.GetProperty("subtotal")));
    Para("Giảm giá: " + Money(q.GetProperty("discountTotal"))); Para("Thuế: " + Money(q.GetProperty("taxTotal")));
    if (q.TryGetProperty("adjustments", out var adjustments) && adjustments.ValueKind == JsonValueKind.Array)
        foreach (var a in adjustments.EnumerateArray()) Para(Text(a, "label") + " (" + Text(a, "type") + "): " + Money(a.GetProperty("amount")));
    Para("TỔNG CỘNG: " + Money(q.GetProperty("grandTotal")), true).Format.Font.Size = 12;
    if (q.TryGetProperty("paymentAgreement", out var payment) && payment.ValueKind == JsonValueKind.Object)
    {
        Para("THỎA THUẬN THANH TOÁN · " + Text(payment, "kind"), true);
        foreach (var line in payment.GetProperty("lines").EnumerateArray())
        {
            Para(Text(line, "sequence") + ". " + Text(line, "label") + " · " + Text(line, "purpose"));
            if (line.TryGetProperty("previewAmount", out var amount)) Para("Số tiền: " + Money(amount));
            foreach (var key in new[] { "amountRule", "dueRule" }) if (line.TryGetProperty(key, out var rule))
            {
                Para((key == "amountRule" ? "Cách xác định số tiền: " : "Thời hạn thanh toán: ") + Label(Text(rule, "type")));
                foreach (var field in rule.EnumerateObject().Where(x => x.Name != "type"))
                    Para(Label(field.Name) + ": " + (field.Value.ValueKind == JsonValueKind.Object && field.Value.TryGetProperty("amount", out _) ? Money(field.Value) : Label(field.Value.ToString())));
            }
            if (line.TryGetProperty("allowedMethodCodes", out var methods)) Para("Phương thức được phép: " + string.Join(", ", methods.EnumerateArray().Select(x => Label(x.GetString() ?? ""))));
            foreach (var key in new[] { "fulfillmentGate", "preferredMethodCode", "channel", "invoicePolicyCode" })
                if (Text(line, key).Length != 0) Para(Label(key) + ": " + Label(Text(line, key)));
        }
    }
    if (Text(q, "notes").Length != 0) { Para("GHI CHÚ", true); foreach (var part in Chunks(Text(q, "notes"), 1000)) Para(part); }
    Para("Mã tài liệu: " + Text(root, "documentId"));
    Para("Tài liệu đã phát hành. Việc phát hành không xác nhận đã gửi, khách hàng đã chấp nhận hoặc ký số.");
    var renderer = new PdfDocumentRenderer { Document = document }; renderer.RenderDocument();
    var pageCount = renderer.PdfDocument.PageCount;
    if (pageCount is < 1 or > 100) return 22;
    foreach (var page in renderer.PdfDocument.Pages)
    {
        using var graphics = XGraphics.FromPdfPage(page);
        graphics.DrawString("BÁO GIÁ · " + Text(q, "quoteNumber") + " · Phiên bản " + Text(q, "quoteRevision"),
            new XFont("Noto Sans", 8), XBrushes.DimGray, new XRect(51, 16, page.Width.Point - 102, 12), XStringFormats.TopLeft);
    }
    using var output = new MemoryStream(); renderer.PdfDocument.Save(output, false);
    if (output.Length is < 1 or > 10485760) return 22;
    var stdout = Console.OpenStandardOutput(); await stdout.WriteAsync(BitConverter.GetBytes(pageCount));
    await stdout.WriteAsync(output.ToArray()); await stdout.FlushAsync(); return 0;
}
catch (Exception error) { Console.Error.Write("PDF_RENDER_FAILED:" + error.GetType().Name); if (args.Contains("--diagnostics", StringComparer.Ordinal)) Console.Error.Write(error.ToString()); return 21; }

static string Label(string value) => value switch
{
    "type" => "Loại", "amount" => "Số tiền", "percentage" => "Tỷ lệ phần trăm", "date" => "Ngày đến hạn", "event" => "Mốc tham chiếu",
    "offsetDays" => "Số ngày tính từ mốc", "dayBasis" => "Cách tính ngày", "operation" => "Hoạt động", "leadDays" => "Số ngày trước hoạt động",
    "milestoneCode" => "Mốc thỏa thuận", "firstDueDate" => "Ngày đến hạn đầu tiên", "interval" => "Chu kỳ", "count" => "Số kỳ",
    "fulfillmentGate" => "Điều kiện thực hiện", "preferredMethodCode" => "Phương thức ưu tiên", "channel" => "Kênh thanh toán", "invoicePolicyCode" => "Chính sách hóa đơn",
    "REMAINDER" => "Phần còn lại", "FIXED_AMOUNT" => "Số tiền cố định", "PERCENTAGE" => "Tỷ lệ phần trăm", "FIXED_DATE" => "Ngày cố định",
    "EVENT_RELATIVE" => "Theo mốc sự kiện", "OPERATIONAL_PRECONDITION" => "Điều kiện trước hoạt động", "MILESTONE" => "Theo mốc thỏa thuận", "RECURRING_FINITE" => "Định kỳ hữu hạn",
    "ORDER_CONFIRMED" => "Xác nhận đơn hàng", "INVOICE_ISSUED" => "Phát hành hóa đơn", "DELIVERY_CONFIRMED" => "Xác nhận giao hàng", "ACCEPTANCE_CONFIRMED" => "Xác nhận nghiệm thu",
    "CALENDAR" => "Ngày lịch", "BUSINESS" => "Ngày làm việc", "BEFORE_BOOKING" => "Trước đặt lịch", "BEFORE_DISPATCH" => "Trước xuất/gửi hàng", "BEFORE_COMPLETION" => "Trước hoàn thành",
    "BOOKING" => "Đặt lịch", "DISPATCH" => "Xuất/gửi hàng", "COMPLETION" => "Hoàn thành", "NONE" => "Không đặt điều kiện",
    "WEEKLY" or "weekly" => "Hàng tuần", "MONTHLY" or "monthly" => "Hàng tháng", "QUARTERLY" or "quarterly" => "Hàng quý",
    "yearly" => "Hàng năm", "one_time" => "Một lần", "BANK_TRANSFER" => "Chuyển khoản", "CASH" => "Tiền mặt", _ => value
};

static string Text(JsonElement node, string property) => node.TryGetProperty(property, out var v) && v.ValueKind != JsonValueKind.Null ? v.ToString() : "";
static IEnumerable<string> Chunks(string value, int length)
{
    for (var at = 0; at < value.Length;)
    {
        var count = Math.Min(length, value.Length - at);
        if (at + count < value.Length)
        {
            var boundary = value.LastIndexOfAny([' ', '\n'], at + count - 1, count);
            if (boundary >= at + count / 2) count = boundary - at + 1;
            if (char.IsHighSurrogate(value[at + count - 1])) count--;
        }
        yield return value.Substring(at, count); at += count;
    }
}
sealed class EmbeddedFonts : IFontResolver
{
    public FontResolverInfo ResolveTypeface(string familyName, bool bold, bool italic) => new(bold ? "bold" : "regular");
    public byte[] GetFont(string faceName)
    {
        var assembly = Assembly.GetExecutingAssembly(); var file = faceName == "bold" ? "NotoSans-Bold.ttf" : "NotoSans-Regular.ttf";
        using var stream = assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(x => x.EndsWith(file, StringComparison.Ordinal)))!;
        using var result = new MemoryStream(); stream.CopyTo(result); return result.ToArray();
    }
}

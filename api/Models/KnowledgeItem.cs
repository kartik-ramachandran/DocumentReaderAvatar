using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using UglyToad.PdfPig;

namespace AvatarDocReader.Api.Models;

public sealed record KnowledgeItem(
    Guid Id,
    string Name,
    string Path,
    string Kind,
    string ContentType,
    long Size,
    string Description,
    string Text,
    string? ModelDataBase64)
{
    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".markdown", ".csv", ".json", ".xml", ".html", ".htm", ".log",
        ".yaml", ".yml", ".cs", ".ts", ".js", ".vue", ".css", ".sql", ".sh", ".bat",
        ".config", ".ini", ".toml", ".tf", ".bicep"
    };

    public bool CanSendToModel => Kind is "image" or "pdf" && !string.IsNullOrWhiteSpace(ModelDataBase64);

    public static async Task<KnowledgeItem> FromFileAsync(IFormFile file, string relativePath, CancellationToken ct)
    {
        var extension = System.IO.Path.GetExtension(file.FileName).ToLowerInvariant();
        var kind = GetKind(file.ContentType, extension);
        var text = string.Empty;
        string? modelDataBase64 = null;
        var description = $"{kind} file, {file.Length:n0} bytes";

        await using var stream = file.OpenReadStream();

        switch (kind)
        {
            case "text":
                using (var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true))
                {
                    var buffer = new char[(int)Math.Min(file.Length, 500_000)];
                    var read = await reader.ReadBlockAsync(buffer.AsMemory(0, buffer.Length), ct);
                    text = new string(buffer, 0, read);
                }
                description = $"Text file, {text.Length:n0} characters extracted";
                break;

            case "word":
                text = ExtractWordText(stream);
                description = $"Word document, {text.Length:n0} characters extracted";
                break;

            case "excel":
                text = ExtractExcelText(stream);
                description = $"Excel spreadsheet, {text.Length:n0} characters extracted";
                break;

            case "powerpoint":
                text = ExtractPowerPointText(stream);
                description = $"PowerPoint presentation, {text.Length:n0} characters extracted";
                break;

            case "pdf":
                text = ExtractPdfText(stream);
                stream.Position = 0;
                using (var mem = new MemoryStream())
                {
                    await stream.CopyToAsync(mem, ct);
                    modelDataBase64 = Convert.ToBase64String(mem.ToArray());
                }
                description = $"PDF document, {text.Length:n0} characters extracted";
                break;

            case "image":
                using (var mem = new MemoryStream())
                {
                    await stream.CopyToAsync(mem, ct);
                    modelDataBase64 = Convert.ToBase64String(mem.ToArray());
                }
                description = "Image file, ready for visual analysis.";
                break;

            case "audio":
                description = "Audio file stored as library asset.";
                break;

            case "video":
                description = "Video file stored as library asset.";
                break;
        }

        return new KnowledgeItem(
            Guid.NewGuid(),
            file.FileName,
            relativePath.Replace('\\', '/'),
            kind,
            file.ContentType,
            file.Length,
            description,
            text,
            modelDataBase64);
    }

    private static string ExtractWordText(Stream stream)
    {
        try
        {
            using var doc = WordprocessingDocument.Open(stream, false);
            var body = doc.MainDocumentPart?.Document?.Body;
            if (body is null) return string.Empty;
            var sb = new StringBuilder();
            foreach (var para in body.Descendants<DocumentFormat.OpenXml.Wordprocessing.Paragraph>())
            {
                sb.AppendLine(para.InnerText);
            }
            return sb.ToString().Trim();
        }
        catch { return string.Empty; }
    }

    private static string ExtractExcelText(Stream stream)
    {
        try
        {
            using var doc = SpreadsheetDocument.Open(stream, false);
            var workbook = doc.WorkbookPart;
            if (workbook is null) return string.Empty;

            var sharedStrings = workbook.SharedStringTablePart?.SharedStringTable
                .Elements<SharedStringItem>().Select(s => s.InnerText).ToArray() ?? [];

            var sb = new StringBuilder();
            foreach (var sheet in workbook.WorksheetParts)
            {
                foreach (var row in sheet.Worksheet.Descendants<Row>())
                {
                    var cells = row.Descendants<Cell>().Select(c =>
                    {
                        if (c.DataType?.Value == CellValues.SharedString &&
                            int.TryParse(c.CellValue?.Text, out var idx) &&
                            idx < sharedStrings.Length)
                            return sharedStrings[idx];
                        return c.CellValue?.Text ?? string.Empty;
                    });
                    sb.AppendLine(string.Join("\t", cells));
                }
            }
            return sb.ToString().Trim();
        }
        catch { return string.Empty; }
    }

    private static string ExtractPowerPointText(Stream stream)
    {
        try
        {
            using var doc = PresentationDocument.Open(stream, false);
            var presentation = doc.PresentationPart;
            if (presentation is null) return string.Empty;
            var sb = new StringBuilder();
            foreach (var slide in presentation.SlideParts)
            {
                foreach (var para in slide.Slide.Descendants<DocumentFormat.OpenXml.Drawing.Paragraph>())
                    sb.AppendLine(para.InnerText);
            }
            return sb.ToString().Trim();
        }
        catch { return string.Empty; }
    }

    private static string ExtractPdfText(Stream stream)
    {
        try
        {
            using var pdf = PdfDocument.Open(stream);
            var sb = new StringBuilder();
            foreach (var page in pdf.GetPages())
                sb.AppendLine(page.Text);
            return sb.ToString().Trim();
        }
        catch { return string.Empty; }
    }

    private static string GetKind(string contentType, string extension) => extension switch
    {
        ".docx" or ".doc" => "word",
        ".xlsx" or ".xls" => "excel",
        ".pptx" or ".ppt" => "powerpoint",
        ".pdf" => "pdf",
        _ when contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) => "image",
        _ when contentType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase) => "audio",
        _ when contentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase) => "video",
        _ when contentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase) => "text",
        _ when TextExtensions.Contains(extension) => "text",
        _ => "other"
    };
}

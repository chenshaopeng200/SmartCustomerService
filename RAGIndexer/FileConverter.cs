using System.Text;
using System.Text.RegularExpressions;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Listener;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using HtmlAgilityPack;
using ReverseMarkdown;
using MiniExcelLibs;

namespace RAGIndexer;

/// <summary>
/// Unified file converter — extracts structured Markdown from various formats.
/// Markdown is richer than raw text for embedding: preserves headings, lists, tables.
/// </summary>
public static class FileConverter
{
    private static readonly ReverseMarkdown.Converter _converter = new(new Config());

    public static string Convert(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".pdf" => ConvertPdf(path),
            ".docx" => ConvertDocx(path),
            ".xlsx" or ".xls" => ConvertExcel(path),
            ".txt" or ".md" or ".csv" => File.ReadAllText(path, Encoding.UTF8),
            ".html" or ".htm" => ConvertHtml(path),
            _ => throw new NotSupportedException($"Unsupported file type: {ext}")
        };
    }

    private static string ConvertPdf(string path)
    {
        // Strategy 1: iText7 HTML extraction + ReverseMarkdown
        try
        {
            var html = ExtractPdfAsHtml(path);
            if (!string.IsNullOrWhiteSpace(html) && html.Length > 10)
            {
                var md = _converter.Convert(html);
                return NormalizeMarkdown(md);
            }
        }
        catch
        {
            // Fall through to strategy 2
        }

        // Strategy 2: PdfPig (best for CJK text without ToUnicode CMaps)
        try
        {
            var text = ExtractWithPdfPig(path);
            if (!string.IsNullOrWhiteSpace(text) && text.Length > 10)
            {
                return NormalizeMarkdown(text);
            }
        }
        catch
        {
            // Fall through to strategy 3
        }

        // Strategy 3: Fallback to iText7 raw text extraction
        return ExtractFromPdfWithIText(path);
    }

    private static string ConvertDocx(string path)
    {
        var sb = new StringBuilder();
        using var doc = WordprocessingDocument.Open(path, false);
        var body = doc?.MainDocumentPart?.Document?.Body;
        if (body == null) return "";

        foreach (var para in body.Elements<Paragraph>())
        {
            var text = para.InnerText.Trim();
            if (string.IsNullOrWhiteSpace(text)) continue;

            // Detect heading style
            var styleId = para.ParagraphProperties?.ParagraphStyleId?.Val?.ToString();
            if (styleId != null && styleId.StartsWith("Heading"))
            {
                var level = int.TryParse(styleId.Substring("Heading".Length), out var l) ? l : 1;
                sb.AppendLine(new string('#', Math.Min(level, 6)) + " " + text);
            }
            else if (text.Length > 0)
            {
                sb.AppendLine(text);
            }
            sb.AppendLine(); // blank line between paragraphs
        }

        return NormalizeMarkdown(sb.ToString());
    }

    private static string ConvertExcel(string path)
    {
        try
        {
            var rows = MiniExcel.Query(path, useHeaderRow: true);
            var data = rows.Cast<dynamic>().ToList();
            if (data.Count == 0) return "";

            // ExpandoObject implements IEnumerable<KeyValuePair<string,object>>
            var firstRowPairs = data[0] as IEnumerable<KeyValuePair<string, object>>;
            if (firstRowPairs == null) return "[Excel: 无法获取表头]";

            var headers = firstRowPairs.Select(kv => kv.Key).ToList();

            // Build markdown table
            var sb = new StringBuilder();
            sb.AppendLine("| " + string.Join(" | ", headers) + " |");
            sb.AppendLine("| " + string.Join(" | ", headers.Select(_ => "---")) + " |");

            foreach (var row in data.Skip(1))
            {
                var rowPairs = row as IEnumerable<KeyValuePair<string, object>>;
                if (rowPairs == null) continue;
                var cells = headers.Select(h => (rowPairs.FirstOrDefault(p => p.Key == h).Value?.ToString()) ?? "").ToList();
                sb.AppendLine("| " + string.Join(" | ", cells) + " |");
            }

            return sb.ToString();
        }
        catch
        {
            return $"[Excel文件 {Path.GetFileName(path)} 解析失败]";
        }
    }

    private static string ConvertHtml(string path)
    {
        var html = File.ReadAllText(path, Encoding.UTF8);
        try
        {
            var md = _converter.Convert(html);
            return NormalizeMarkdown(md);
        }
        catch
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(html);
            return doc.DocumentNode.InnerText;
        }
    }

    // --- PDF extraction helpers ---

    private static string ExtractPdfAsHtml(string path)
    {
        var sb = new StringBuilder();
        using var reader = new PdfReader(path);
        using var pdfDoc = new iText.Kernel.Pdf.PdfDocument(reader);

        for (int i = 1; i <= pdfDoc.GetNumberOfPages(); i++)
        {
            var page = pdfDoc.GetPage(i);
            var strategy = new LocationTextExtractionStrategy();
            var processor = new iText.Kernel.Pdf.Canvas.Parser.PdfCanvasProcessor(strategy);
            processor.ProcessPageContent(page);

            var text = strategy.GetResultantText();
            if (!string.IsNullOrWhiteSpace(text))
                sb.AppendLine("<p>" + text.Replace("\n", "<br/>") + "</p>");
        }

        return sb.ToString();
    }

    private static string ExtractWithPdfPig(string path)
    {
        var sb = new StringBuilder();
        using var pdf = UglyToad.PdfPig.PdfDocument.Open(path);
        foreach (var page in pdf.GetPages())
        {
            var text = page.Text.Trim();
            if (!string.IsNullOrWhiteSpace(text))
                sb.AppendLine(text);
        }
        return sb.ToString();
    }

    private static string ExtractFromPdfWithIText(string path)
    {
        var sb = new StringBuilder();
        using var reader = new PdfReader(path);
        using var pdfDoc = new iText.Kernel.Pdf.PdfDocument(reader);
        for (int i = 1; i <= pdfDoc.GetNumberOfPages(); i++)
        {
            var page = pdfDoc.GetPage(i);
            var strategy = new LocationTextExtractionStrategy();
            var processor = new iText.Kernel.Pdf.Canvas.Parser.PdfCanvasProcessor(strategy);
            processor.ProcessPageContent(page);
            sb.AppendLine(strategy.GetResultantText());
        }
        return sb.ToString();
    }

    // --- Markdown normalization ---

    private static string NormalizeMarkdown(string text)
    {
        // Remove excessive blank lines
        var normalized = Regex.Replace(text, @"\n{3,}", "\n\n");

        // Clean up trailing whitespace
        var lines = normalized.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        normalized = string.Join("\n", lines);

        return normalized.Trim();
    }
}

using Azure;
using Azure.AI.DocumentIntelligence;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using SmartArchive.Application;
using SmartArchive.Domain;
using System.Globalization;
using System.Text.RegularExpressions;

namespace SmartArchive.Infrastructure;

public sealed class DocumentIntelligenceService(IConfiguration configuration) : IDocumentIntelligenceService
{
    private static readonly Regex DateCandidateRegex = new(
        @"(?<!\d)(?:\d{4}[./-]\d{1,2}[./-]\d{1,2}|\d{1,2}[./-]\d{1,2}[./-]\d{4}|\d{1,2}[-\s][\p{L}]{3,}[-\s]\d{4}|[\p{L}]{3,}\s+\d{1,2},?\s+\d{4})(?!\d)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public async Task<AiSuggestion> AnalyzeAsync(string fileName, Stream pdf, string? suppliedText = null, CancellationToken ct = default)
    {
        var endpoint = configuration["AzureDocumentIntelligence:Endpoint"];
        var key = configuration["AzureDocumentIntelligence:ApiKey"];
        var text = suppliedText ?? "";
        double? ocrConfidence = null;
        var azureWasUsed = false;

        if (!string.IsNullOrWhiteSpace(endpoint))
        {
            var uri = new Uri(endpoint, UriKind.Absolute);
            var client = string.IsNullOrWhiteSpace(key)
                ? new DocumentIntelligenceClient(uri, new DefaultAzureCredential())
                : new DocumentIntelligenceClient(uri, new AzureKeyCredential(key));
            using var memory = new MemoryStream();
            await pdf.CopyToAsync(memory, ct);
            memory.Position = 0;
            var operation = await client.AnalyzeDocumentAsync(
                WaitUntil.Completed,
                "prebuilt-read",
                BinaryData.FromBytes(memory.ToArray()),
                cancellationToken: ct);
            text = operation.Value.Content ?? "";
            azureWasUsed = true;
            var wordScores = operation.Value.Pages?
                .SelectMany(page => page.Words ?? [])
                .Select(word => (double)word.Confidence)
                .Where(score => double.IsFinite(score) && score is >= 0 and <= 1)
                .ToArray() ?? [];
            if (wordScores.Length > 0) ocrConfidence = wordScores.Average();
        }

        var normalizedFileName = Path.GetFileNameWithoutExtension(fileName).Replace('_', ' ').Replace('-', ' ');
        var title = string.IsNullOrWhiteSpace(normalizedFileName) ? "Untitled document" : normalizedFileName;
        var source = (fileName + " " + text).ToLowerInvariant();
        var (department, category) = source switch
        {
            var x when x.Contains("maintenance") || x.Contains("intervention") || x.Contains("panne") => ("Maintenance", "Service report"),
            var x when x.Contains("quality") || x.Contains("qualité") || x.Contains("audit") || x.Contains("non-conform") => ("Quality", "Quality report"),
            var x when x.Contains("production") || x.Contains("fabrication") => ("Production", "Production report"),
            var x when x.Contains("incident") || x.Contains("informatique") || x.Contains("it ") => ("IT", "IT incident"),
            var x when x.Contains("delivery") || x.Contains("livraison") || x.Contains("logistique") => ("Logistics", "Delivery note"),
            var x when x.Contains("invoice") || x.Contains("facture") => ("Finance", "Invoice"),
            var x when x.Contains("human resources") || x.Contains("ressources humaines") || x.Contains("rh ") => ("Human Resources", "HR document"),
            _ => ("General Administration", "General document")
        };

        var categorySignals = CountCategorySignals(source, department);
        var classificationEvidence = Math.Clamp(
            0.18 + Math.Min(categorySignals, 3) * 0.14 + (text.Length > 200 ? 0.12 : 0) + (text.Length > 1200 ? 0.08 : 0),
            0.18,
            0.88);
        var confidence = Math.Round(ocrConfidence.HasValue
            ? (ocrConfidence.Value * 0.65) + (classificationEvidence * 0.35)
            : classificationEvidence, 2);

        var reference = Regex.Match(text, @"(?i)(?:ref(?:erence)?|n[°o])\s*[:#-]?\s*([A-Z0-9/-]{3,})").Groups[1].Value;
        var documentDate = ExtractDocumentDate(text);
        var description = string.IsNullOrWhiteSpace(text)
            ? "No readable text was extracted. The suggestion is based on the file name and requires manual review."
            : text[..Math.Min(text.Length, 240)];
        var warning = azureWasUsed
            ? ocrConfidence.HasValue
                ? "The score combines Azure word-level OCR confidence with rule-based category evidence; category assignment is not an Azure classifier. Human approval is required."
                : "Azure Document Intelligence ran, but returned no word-level confidence. Category assignment is rule-based and requires human approval."
            : "Azure Document Intelligence is not configured. Local filename/text rules only were used; manual review is required.";

        return new AiSuggestion(title, department, category, documentDate, reference, text, string.Join(", ", new[] { department, category }), description, confidence, warning);
    }

    private static DateOnly? ExtractDocumentDate(string text)
    {
        DateOnly? firstCandidate = null;
        foreach (Match match in DateCandidateRegex.Matches(text))
        {
            if (!TryParseDocumentDate(match.Value, out var date)) continue;

            var precedingText = text[..match.Index];
            if (Regex.IsMatch(precedingText, @"(?is)\b(?:date(?:\s+(?:of|de|du|de facture|d[’']?émission))?|dated)\s*[:#-]?\s*$"))
                return date;

            firstCandidate ??= date;
        }

        return firstCandidate;
    }

    private static bool TryParseDocumentDate(string value, out DateOnly date)
    {
        var formats = new[]
        {
            "d/M/yyyy", "dd/MM/yyyy", "d-M-yyyy", "dd-MM-yyyy", "d.M.yyyy", "dd.MM.yyyy",
            "yyyy-M-d", "yyyy-MM-dd", "yyyy/M/d", "yyyy/MM/dd", "yyyy.M.d", "yyyy.MM.dd",
            "d/M/yy", "dd/MM/yy", "d-M-yy", "dd-MM-yy", "d.M.yy", "dd.MM.yy"
        };
        var candidate = value.Trim();
        if (DateOnly.TryParseExact(candidate, formats, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out date))
            return true;
        if (DateOnly.TryParse(candidate, CultureInfo.GetCultureInfo("fr-FR"), DateTimeStyles.AllowWhiteSpaces, out date))
            return true;
        return DateOnly.TryParse(candidate, CultureInfo.GetCultureInfo("en-US"), DateTimeStyles.AllowWhiteSpaces, out date);
    }

    private static int CountCategorySignals(string source, string department)
    {
        var terms = department switch
        {
            "Maintenance" => new[] { "maintenance", "intervention", "panne" },
            "Quality" => new[] { "quality", "qualité", "audit", "non-conform" },
            "Production" => new[] { "production", "fabrication" },
            "IT" => new[] { "incident", "informatique", "it " },
            "Logistics" => new[] { "delivery", "livraison", "logistique" },
            "Finance" => new[] { "invoice", "facture" },
            "Human Resources" => new[] { "human resources", "ressources humaines", "rh " },
            _ => Array.Empty<string>()
        };
        return terms.Count(source.Contains);
    }
}

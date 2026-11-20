using Azure;
using Azure.AI.DocumentIntelligence;
using Microsoft.Extensions.Configuration;
using SmartArchive.Application;
using SmartArchive.Domain;
using System.Text.RegularExpressions;

namespace SmartArchive.Infrastructure;

public sealed class DocumentIntelligenceService(IConfiguration configuration) : IDocumentIntelligenceService
{
    public async Task<AiSuggestion> AnalyzeAsync(string fileName, Stream pdf, string? suppliedText = null, CancellationToken ct = default)
    {
        var endpoint = configuration["AzureDocumentIntelligence:Endpoint"];
        var key = configuration["AzureDocumentIntelligence:ApiKey"];
        string text = suppliedText ?? "";
        if (!string.IsNullOrWhiteSpace(endpoint) && !string.IsNullOrWhiteSpace(key))
        {
            var client = new DocumentIntelligenceClient(new Uri(endpoint), new AzureKeyCredential(key));
            using var ms = new MemoryStream(); await pdf.CopyToAsync(ms, ct); ms.Position = 0;
            var operation = await client.AnalyzeDocumentAsync(WaitUntil.Completed, "prebuilt-read", BinaryData.FromBytes(ms.ToArray()), cancellationToken: ct);
            text = operation.Value.Content ?? "";
        }

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

        var title = Path.GetFileNameWithoutExtension(fileName).Replace('_', ' ').Replace('-', ' ');
        if (string.IsNullOrWhiteSpace(title)) title = "Untitled document";
        var confidence = string.IsNullOrWhiteSpace(text) ? .38 : .72;
        var reference = Regex.Match(text, @"(?i)(?:ref(?:erence)?|n[°o])\s*[:#-]?\s*([A-Z0-9/-]{3,})").Groups[1].Value;
        var description = string.IsNullOrWhiteSpace(text)
            ? "Local analysis: OCR is unavailable. Manually verify the suggested fields."
            : text[..Math.Min(text.Length, 240)];
        var warning = string.IsNullOrWhiteSpace(text)
            ? "No OCR text is available. Manual review is required."
            : "Classification is a suggestion based on the text and file name. Human approval is required.";

        return new AiSuggestion(title, department, category, null, reference, text, string.Join(", ", new[] { department, category }), description, confidence, warning);
    }
}

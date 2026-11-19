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
            var x when x.Contains("maintenance") || x.Contains("intervention") || x.Contains("panne") => ("Maintenance", "Rapport d’intervention"),
            var x when x.Contains("quality") || x.Contains("qualité") || x.Contains("audit") || x.Contains("non-conform") => ("Quality", "Rapport qualité"),
            var x when x.Contains("production") || x.Contains("fabrication") => ("Production", "Rapport de production"),
            var x when x.Contains("incident") || x.Contains("informatique") || x.Contains("it ") => ("IT", "Incident informatique"),
            var x when x.Contains("delivery") || x.Contains("livraison") || x.Contains("logistique") => ("Logistics", "Bon de livraison"),
            var x when x.Contains("invoice") || x.Contains("facture") => ("Finance", "Facture"),
            var x when x.Contains("human resources") || x.Contains("ressources humaines") || x.Contains("rh ") => ("Human Resources", "Document RH"),
            _ => ("General Administration", "Document général")
        };
        var title = Path.GetFileNameWithoutExtension(fileName).Replace('_', ' ').Replace('-', ' ');
        if (string.IsNullOrWhiteSpace(title)) title = "Document sans titre";
        var confidence = string.IsNullOrWhiteSpace(text) ? .38 : .72;
        var reference = Regex.Match(text, @"(?i)(?:ref(?:erence)?|n[°o])\s*[:#-]?\s*([A-Z0-9/-]{3,})").Groups[1].Value;
        return new AiSuggestion(title, department, category, null, reference, text, string.Join(", ", new[] { department, category }), string.IsNullOrWhiteSpace(text) ? "Analyse locale : OCR indisponible. Vérifiez manuellement les champs suggérés." : text[..Math.Min(text.Length, 240)], confidence, string.IsNullOrWhiteSpace(text) ? "Aucun texte OCR disponible : vérification manuelle requise." : "Classification indicative basée sur le texte/fichier ; approbation humaine obligatoire.");
    }
}

using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Azure;
using Azure.AI.DocumentIntelligence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;
using SmartArchive.Application.Common.Interfaces;
using SmartArchive.Application.DTOs.AI;

namespace SmartArchive.Infrastructure.AI;

public sealed class AzureDocumentIntelligenceOptions
{
    public const string SectionName = "AzureDocumentIntelligence";
    public string Endpoint { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public float MinimumConfidenceThreshold { get; set; } = 0.80f;
}

public sealed class AzureDocumentIntelligenceService : IDocumentAiService
{
    private readonly DocumentIntelligenceClient _client;
    private readonly IApplicationDbContext _dbContext;
    private readonly AzureDocumentIntelligenceOptions _options;
    private readonly ILogger<AzureDocumentIntelligenceService> _logger;
    private readonly AsyncRetryPolicy _retryPolicy;

    public AzureDocumentIntelligenceService(
        IOptions<AzureDocumentIntelligenceOptions> options,
        IApplicationDbContext dbContext,
        ILogger<AzureDocumentIntelligenceService> logger)
    {
        _options = options.Value;
        _dbContext = dbContext;
        _logger = logger;

        _client = new DocumentIntelligenceClient(
            new Uri(_options.Endpoint),
            new AzureKeyCredential(_options.ApiKey));

        _retryPolicy = Policy
            .Handle<RequestFailedException>(ex => ex.Status is 429 or 500 or 502 or 503 or 504)
            .WaitAndRetryAsync(3, attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)),
                (ex, ts, attempt, _) => _logger.LogWarning(ex, "Azure AI transient failure. Retry {Attempt} in {Delay}s", attempt, ts.TotalSeconds));
    }

    public async Task<DocumentAnalysisOutcome> AnalyzeDocumentAsync(Stream pdfStream, string fileName, CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        using var buffer = new MemoryStream();
        await pdfStream.CopyToAsync(buffer, cancellationToken);
        var binaryData = BinaryData.FromBytes(buffer.ToArray());

        // Stage 1: Layout & OCR Extraction
        var layoutOperation = await _retryPolicy.ExecuteAsync(() =>
            _client.AnalyzeDocumentAsync(WaitUntil.Completed, "prebuilt-layout", binaryData, cancellationToken: cancellationToken));

        var layoutResult = layoutOperation.Value;
        var fullText = layoutResult.Content ?? string.Empty;
        var detectedLanguage = layoutResult.Languages?.OrderByDescending(l => l.Confidence).FirstOrDefault()?.Locale ?? DetectLanguageFallback(fullText);

        // Stage 2: Classify Document Type & Department via configured keywords
        var docTypes = await _dbContext.DocumentTypes
            .AsNoTracking()
            .Include(dt => dt.DefaultDepartment)
            .ToListAsync(cancellationToken);

        var matchedType = docTypes
            .Select(dt => new
            {
                DocType = dt,
                Score = dt.ClassificationKeywords
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Count(kw => fullText.Contains(kw, StringComparison.OrdinalIgnoreCase))
            })
            .OrderByDescending(x => x.Score)
            .FirstOrDefault();

        var selectedType = (matchedType is not null && matchedType.Score > 0)
            ? matchedType.DocType
            : docTypes.FirstOrDefault(dt => dt.Code == "GEN");

        var modelToUse = selectedType?.AzureModelId ?? "prebuilt-document";

        // Stage 3: Specialized Model Extraction
        AnalyzeResult specializedResult = layoutResult;
        if (!string.Equals(modelToUse, "prebuilt-layout", StringComparison.OrdinalIgnoreCase))
        {
            var specOp = await _retryPolicy.ExecuteAsync(() =>
                _client.AnalyzeDocumentAsync(WaitUntil.Completed, modelToUse, binaryData, cancellationToken: cancellationToken));
            specializedResult = specOp.Value;
        }

        var outcome = new DocumentAnalysisOutcome
        {
            ModelUsed = modelToUse,
            DetectedLanguage = detectedLanguage,
            PageCount = layoutResult.Pages?.Count ?? 1,
            SuggestedDocumentTypeCode = selectedType?.Code,
            SuggestedDepartmentCode = selectedType?.DefaultDepartment?.Code,
            ExtractedOcrText = fullText,
            RawResponseJson = JsonSerializer.Serialize(new
            {
                Model = modelToUse,
                Language = detectedLanguage,
                Pages = layoutResult.Pages?.Count,
                DocumentCount = specializedResult.Documents?.Count
            })
        };

        float confidenceSum = 0;
        int fieldCount = 0;

        if (specializedResult.Documents is { Count: > 0 })
        {
            var doc = specializedResult.Documents[0];
            confidenceSum += doc.Confidence;
            fieldCount++;

            foreach (var (key, field) in doc.Fields)
            {
                if (string.IsNullOrWhiteSpace(field.Content)) continue;

                var conf = field.Confidence ?? 0.5f;
                confidenceSum += conf;
                fieldCount++;

                outcome.AdditionalFields.Add(new ExtractedFieldDto(key, field.Content, conf));

                switch (key)
                {
                    case "InvoiceId" or "DocumentId" or "ContractId":
                        outcome.ReferenceNumber ??= field.Content;
                        break;
                    case "InvoiceDate" or "DocumentDate" or "ExecutionDate":
                        if (field.ValueDate.HasValue)
                            outcome.DocumentDate = DateOnly.FromDateTime(field.ValueDate.Value.DateTime);
                        break;
                    case "VendorName" or "MerchantName":
                        outcome.SenderName ??= field.Content;
                        break;
                    case "CustomerName" or "RecipientName":
                        outcome.RecipientName ??= field.Content;
                        break;
                    case "InvoiceTotal" or "TotalAmount":
                        if (field.ValueCurrency is not null)
                        {
                            outcome.TotalAmount = (decimal)field.ValueCurrency.Amount;
                            outcome.Currency = field.ValueCurrency.CurrencyCode ?? field.ValueCurrency.CurrencySymbol;
                        }
                        break;
                }
            }
        }

        // Fallback deterministic extractors for general documents
        outcome.ReferenceNumber ??= ExtractRegex(fullText, @"(?:Ref|N°|No|Facture|Contrat)[\s:\-#]*([A-Z0-9\-\/]{4,25})");
        outcome.Title = ExtractTitle(fullText, fileName);
        outcome.Keywords = ExtractTopKeywords(fullText);

        outcome.OverallConfidence = fieldCount > 0 ? MathF.Round(confidenceSum / fieldCount, 3) : 0.65f;
        outcome.RequiresManualReview = outcome.OverallConfidence < _options.MinimumConfidenceThreshold
                                       || matchedType is null
                                       || matchedType.Score == 0;

        sw.Stop();
        outcome.ProcessingDurationMs = sw.ElapsedMilliseconds;
        return outcome;
    }

    private static string DetectLanguageFallback(string text)
    {
        if (Regex.IsMatch(text, @"[؀-ۿ]")) return "ar";
        if (Regex.IsMatch(text, @"(le|la|les|des|facture|contrat|monsieur|madame)", RegexOptions.IgnoreCase)) return "fr";
        return "en";
    }

    private static string? ExtractRegex(string text, string pattern)
    {
        var match = Regex.Match(text, pattern, RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    private static string ExtractTitle(string text, string fallbackFileName)
    {
        var firstLine = text
            .Split("<br>", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(l => l.Length >= 5 && l.Length <= 120);

        return firstLine ?? Path.GetFileNameWithoutExtension(fallbackFileName);
    }

    private static string ExtractTopKeywords(string text)
    {
        var words = Regex.Matches(text.ToLowerInvariant(), @"[a-z؀-ۿ]{5,}")
            .Select(m => m.Value)
            .GroupBy(w => w)
            .OrderByDescending(g => g.Count())
            .Take(8)
            .Select(g => g.Key);
        return string.Join(", ", words);
    }
}

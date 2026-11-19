using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using SmartArchive.Application;
using SmartArchive.Domain;

namespace SmartArchive.Infrastructure;

public sealed class DocumentWorkflow(IDocumentRepository repository, IFileStorageService storage, IDocumentIntelligenceService intelligence) : IDocumentWorkflow
{
    public async Task<UploadResult> UploadAsync(string fileName, string contentType, Stream stream, string uploadedBy, CancellationToken ct = default)
    {
        var ext = Path.GetExtension(fileName);
        if (!string.Equals(ext, ".pdf", StringComparison.OrdinalIgnoreCase) || (contentType != "application/pdf" && contentType != "application/octet-stream")) throw new InvalidOperationException("Seuls les fichiers PDF sont acceptés.");
        var max = 50L * 1024 * 1024; using var input = new MemoryStream(); await stream.CopyToAsync(input, ct);
        if (input.Length == 0 || input.Length > max) throw new InvalidOperationException("Le PDF doit peser entre 1 octet et 50 Mo.");
        var bytes = input.ToArray(); if (bytes.Length < 5 || System.Text.Encoding.ASCII.GetString(bytes, 0, 5) != "%PDF-") throw new InvalidOperationException("Le contenu du fichier n’est pas un PDF valide.");
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        if ((await repository.ListAsync(ct: ct)).Any(d => d.ContentHash == hash)) return new UploadResult(Guid.Empty, fileName, true);
        var id = Guid.NewGuid(); input.Position = 0; var reference = await storage.SaveAsync(id, Path.GetFileName(fileName), input, ct);
        var doc = new ArchiveDocument { Id = id, FileName = Path.GetFileName(fileName), Title = Path.GetFileNameWithoutExtension(fileName), BlobReference = reference, ContentHash = hash, SizeBytes = bytes.Length, UploadedBy = uploadedBy, Status = DocumentStatus.Uploaded, AuditJson = AddAudit("Upload", uploadedBy, "PDF reçu") };
        DocumentLifecycle.Move(doc, DocumentStatus.Processing);
        await repository.AddAsync(doc, ct);
        try { await Analyze(doc, ct); } catch { DocumentLifecycle.Move(doc, DocumentStatus.ProcessingFailed); doc.Warnings = "Analyse échouée. Vous pouvez relancer le traitement."; await repository.SaveAsync(doc, ct); }
        return new UploadResult(id, fileName, false);
    }
    public async Task RetryAnalysisAsync(Guid id, CancellationToken ct = default) { var doc = await repository.GetAsync(id, ct) ?? throw new KeyNotFoundException(); DocumentLifecycle.Move(doc, DocumentStatus.Processing); await repository.SaveAsync(doc, ct); await Analyze(doc, ct); }
    private async Task Analyze(ArchiveDocument doc, CancellationToken ct)
    {
        await using var input = await storage.OpenAsync(doc.BlobReference, ct) ?? throw new IOException("Fichier introuvable");
        var s = await intelligence.AnalyzeAsync(doc.FileName, input, ct: ct); doc.Title = s.Title; doc.DepartmentName = s.Department; doc.DepartmentId = (await repository.DepartmentsAsync(ct)).FirstOrDefault(x => x.Name == s.Department)?.Id ?? 10; doc.Category = s.Category; doc.DocumentDate = s.DocumentDate; doc.ReferenceNumber = s.ReferenceNumber; doc.ExtractedText = s.ExtractedText; doc.Keywords = s.Keywords; doc.Description = s.Description; doc.Confidence = s.Confidence; doc.Warnings = s.Warnings; DocumentLifecycle.Move(doc, DocumentStatus.PendingValidation); doc.AuditJson = AddAudit("AIAnalysis", "SmartArchive", $"Analyse indicative, confiance {s.Confidence:P0}", doc.AuditJson); await repository.SaveAsync(doc, ct);
    }
    public async Task ReviewAsync(Guid id, string title, string department, string category, DateOnly? date, string reference, string comment, string reviewer, bool approve, CancellationToken ct = default)
    {
        var doc = await repository.GetAsync(id, ct) ?? throw new KeyNotFoundException();
        if (doc.Status is not (DocumentStatus.PendingValidation or DocumentStatus.UnderReview or DocumentStatus.Rejected)) throw new InvalidOperationException("Ce document n’est pas dans la file de validation.");
        doc.Title = title.Trim(); doc.DepartmentName = department; doc.DepartmentId = (await repository.DepartmentsAsync(ct)).FirstOrDefault(x => x.Name == department)?.Id ?? doc.DepartmentId; doc.Category = category.Trim(); doc.DocumentDate = date; doc.ReferenceNumber = reference.Trim(); doc.ReviewerComment = comment; doc.ValidatedBy = reviewer; doc.ValidatedAt = DateTimeOffset.UtcNow; DocumentLifecycle.Move(doc, approve ? DocumentStatus.Archived : DocumentStatus.Rejected); doc.AuditJson = AddAudit(approve ? "ApprovedAndArchived" : "Rejected", reviewer, comment, doc.AuditJson); await repository.SaveAsync(doc, ct);
    }
    public async Task<byte[]?> DownloadAsync(Guid id, CancellationToken ct = default) { var doc = await repository.GetAsync(id, ct); if (doc is null) return null; await using var s = await storage.OpenAsync(doc.BlobReference, ct); if (s is null) return null; using var m = new MemoryStream(); await s.CopyToAsync(m, ct); return m.ToArray(); }
    private static string AddAudit(string action, string actor, string detail, string existing = "[]") { var rows = JsonSerializer.Deserialize<List<object>>(existing) ?? []; rows.Add(new { action, actor, detail, at = DateTimeOffset.UtcNow }); return JsonSerializer.Serialize(rows); }
}

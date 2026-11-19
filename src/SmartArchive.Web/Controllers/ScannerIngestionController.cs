using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartArchive.Application.Common.Interfaces;
using SmartArchive.Application.Services;
using SmartArchive.Domain.Enums;

namespace SmartArchive.Web.Controllers;

[ApiController]
[Route("api/scanner")]
[Authorize]
public sealed class ScannerIngestionController : ControllerBase
{
    private readonly DocumentWorkflowService _workflowService;
    private readonly IBlobStorageService _blobStorage;
    private readonly IAuditService _auditService;

    public ScannerIngestionController(
        DocumentWorkflowService workflowService,
        IBlobStorageService blobStorage,
        IAuditService auditService)
    {
        _workflowService = workflowService;
        _blobStorage = blobStorage;
        _auditService = auditService;
    }

    /// <summary>
    /// Future-ready REST endpoint for direct desktop TWAIN/WIA/eSCL scanner bridge ingestion.
    /// </summary>
    [HttpPost("ingest")]
    [RequestSizeLimit(100_000_000)] // 100 MB max scanned PDF
    public async Task<IActionResult> IngestFromScanner(IFormFile file, [FromForm] Guid? departmentId, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
            return BadRequest("No PDF file received from scanner.");

        await using var stream = file.OpenReadStream();
        var doc = await _workflowService.IngestPdfAsync(stream, file.FileName, file.ContentType, departmentId, cancellationToken);

        return Accepted(new
        {
            doc.Id,
            doc.DocumentNumber,
            doc.Status
        });
    }

    [HttpGet("download/{container}/{*blobPath}")]
    public async Task<IActionResult> DownloadPdf(string container, string blobPath, [FromQuery] Guid documentId, CancellationToken cancellationToken)
    {
        var stream = await _blobStorage.DownloadAsync(container, blobPath, cancellationToken);
        await _auditService.LogAsync(AuditActionType.Downloaded, "Document", documentId, $"Downloaded PDF {blobPath}", cancellationToken: cancellationToken);
        return File(stream, "application/pdf", Path.GetFileName(blobPath));
    }
}

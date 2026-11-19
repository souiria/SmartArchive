using SmartArchive.Application.DTOs.AI;

namespace SmartArchive.Application.Common.Interfaces;

public interface IDocumentAiService
{
    Task<DocumentAnalysisOutcome> AnalyzeDocumentAsync(Stream pdfStream, string fileName, CancellationToken cancellationToken = default);
}

using Microsoft.EntityFrameworkCore;
using SmartArchive.Domain.Entities;

namespace SmartArchive.Application.Common.Interfaces;

public interface IApplicationDbContext
{
    DbSet<Department> Departments { get; }
    DbSet<Document> Documents { get; }
    DbSet<DocumentType> DocumentTypes { get; }
    DbSet<DocumentMetadata> DocumentMetadata { get; }
    DbSet<DocumentVersion> DocumentVersions { get; }
    DbSet<DocumentPermission> DocumentPermissions { get; }
    DbSet<AIProcessingResult> AIProcessingResults { get; }
    DbSet<AuditLog> AuditLogs { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

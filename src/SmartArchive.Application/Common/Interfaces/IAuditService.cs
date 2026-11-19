using SmartArchive.Domain.Enums;

namespace SmartArchive.Application.Common.Interfaces;

public interface IAuditService
{
    Task LogAsync(AuditActionType action, string entityName, Guid entityId, string? details = null, string? oldValues = null, string? newValues = null, CancellationToken cancellationToken = default);
}

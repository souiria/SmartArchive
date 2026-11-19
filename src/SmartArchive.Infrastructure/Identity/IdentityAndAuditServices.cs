using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using SmartArchive.Application.Common.Interfaces;
using SmartArchive.Domain.Entities;
using SmartArchive.Domain.Enums;

namespace SmartArchive.Infrastructure.Identity;

public sealed class CurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUserService(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public Guid? UserId
    {
        get
        {
            var id = _httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(id, out var parsed) ? parsed : null;
        }
    }

    public string? Email => _httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.Email);

    public Guid? DepartmentId
    {
        get
        {
            var dept = _httpContextAccessor.HttpContext?.User?.FindFirstValue("DepartmentId");
            return Guid.TryParse(dept, out var parsed) ? parsed : null;
        }
    }

    public string? IpAddress => _httpContextAccessor.HttpContext?.Connection?.RemoteIpAddress?.ToString();

    public bool IsAdministrator => _httpContextAccessor.HttpContext?.User?.IsInRole("Administrator") ?? false;

    public bool IsDepartmentManager => _httpContextAccessor.HttpContext?.User?.IsInRole("DepartmentManager") ?? false;
}

public sealed class AuditService : IAuditService
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;

    public AuditService(IApplicationDbContext context, ICurrentUserService currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task LogAsync(
        AuditActionType action,
        string entityName,
        Guid entityId,
        string? details = null,
        string? oldValues = null,
        string? newValues = null,
        CancellationToken cancellationToken = default)
    {
        _context.AuditLogs.Add(new AuditLog
        {
            UserId = _currentUser.UserId,
            UserEmail = _currentUser.Email ?? "SystemWorker",
            IpAddress = _currentUser.IpAddress,
            Action = action,
            EntityName = entityName,
            EntityId = entityId,
            Details = details,
            OldValuesJson = oldValues,
            NewValuesJson = newValues
        });

        await _context.SaveChangesAsync(cancellationToken);
    }
}

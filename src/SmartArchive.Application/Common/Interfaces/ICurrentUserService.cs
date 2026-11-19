namespace SmartArchive.Application.Common.Interfaces;

public interface ICurrentUserService
{
    Guid? UserId { get; }
    string? Email { get; }
    Guid? DepartmentId { get; }
    string? IpAddress { get; }
    bool IsAdministrator { get; }
    bool IsDepartmentManager { get; }
}

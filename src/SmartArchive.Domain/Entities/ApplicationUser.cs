using Microsoft.AspNetCore.Identity;

namespace SmartArchive.Domain.Entities;

public class ApplicationUser : IdentityUser<Guid>
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string PreferredLanguage { get; set; } = "fr";
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public Guid? DepartmentId { get; set; }
    public Department? Department { get; set; }

    public ICollection<DocumentPermission> DocumentPermissions { get; set; } = new List<DocumentPermission>();
}

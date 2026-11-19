using Microsoft.AspNetCore.Identity;

namespace SmartArchive.Domain.Entities;

public class ApplicationRole : IdentityRole<Guid>
{
    public string Description { get; set; } = string.Empty;

    public ApplicationRole() { }
    public ApplicationRole(string roleName, string description) : base(roleName)
    {
        Id = Guid.CreateVersion7();
        Description = description;
    }
}

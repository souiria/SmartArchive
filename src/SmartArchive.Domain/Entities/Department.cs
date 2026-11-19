using SmartArchive.Domain.Common;

namespace SmartArchive.Domain.Entities;

public class Department : BaseAuditableEntity, ISoftDeletable
{
    public string Code { get; set; } = string.Empty;
    public string NameFr { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string? DescriptionFr { get; set; }
    public string? DescriptionAr { get; set; }
    public int DefaultRetentionYears { get; set; } = 10;
    public bool IsActive { get; set; } = true;

    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public Guid? DeletedByUserId { get; set; }

    public ICollection<ApplicationUser> Users { get; set; } = new List<ApplicationUser>();
    public ICollection<Document> Documents { get; set; } = new List<Document>();
    public ICollection<DocumentType> DefaultDocumentTypes { get; set; } = new List<DocumentType>();
}

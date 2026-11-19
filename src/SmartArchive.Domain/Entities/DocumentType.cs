using SmartArchive.Domain.Common;

namespace SmartArchive.Domain.Entities;

public class DocumentType : BaseAuditableEntity, ISoftDeletable
{
    public string Code { get; set; } = string.Empty;
    public string NameFr { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string AzureModelId { get; set; } = "prebuilt-document";
    public string ClassificationKeywords { get; set; } = string.Empty;

    public Guid? DefaultDepartmentId { get; set; }
    public Department? DefaultDepartment { get; set; }

    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public Guid? DeletedByUserId { get; set; }

    public ICollection<Document> Documents { get; set; } = new List<Document>();
}

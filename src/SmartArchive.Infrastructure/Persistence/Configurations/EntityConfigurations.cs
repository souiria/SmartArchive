using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartArchive.Domain.Entities;

namespace SmartArchive.Infrastructure.Persistence.Configurations;

public class DepartmentConfiguration : IEntityTypeConfiguration<Department>
{
    public void Configure(EntityTypeBuilder<Department> builder)
    {
        builder.ToTable("Departments");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Code).IsRequired().HasMaxLength(20);
        builder.HasIndex(d => d.Code).IsUnique().HasFilter("[IsDeleted] = 0");
        builder.Property(d => d.NameFr).IsRequired().HasMaxLength(150);
        builder.Property(d => d.NameAr).IsRequired().HasMaxLength(150);
        builder.Property(d => d.DescriptionFr).HasMaxLength(500);
        builder.Property(d => d.DescriptionAr).HasMaxLength(500);
    }
}

public class DocumentTypeConfiguration : IEntityTypeConfiguration<DocumentType>
{
    public void Configure(EntityTypeBuilder<DocumentType> builder)
    {
        builder.ToTable("DocumentTypes");
        builder.HasKey(dt => dt.Id);
        builder.Property(dt => dt.Code).IsRequired().HasMaxLength(30);
        builder.HasIndex(dt => dt.Code).IsUnique().HasFilter("[IsDeleted] = 0");
        builder.Property(dt => dt.NameFr).IsRequired().HasMaxLength(150);
        builder.Property(dt => dt.NameAr).IsRequired().HasMaxLength(150);
        builder.Property(dt => dt.AzureModelId).IsRequired().HasMaxLength(100);
        builder.Property(dt => dt.ClassificationKeywords).HasMaxLength(1000);

        builder.HasOne(dt => dt.DefaultDepartment)
            .WithMany(d => d.DefaultDocumentTypes)
            .HasForeignKey(dt => dt.DefaultDepartmentId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

public class DocumentConfiguration : IEntityTypeConfiguration<Document>
{
    public void Configure(EntityTypeBuilder<Document> builder)
    {
        builder.ToTable("Documents");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.DocumentNumber).IsRequired().HasMaxLength(50);
        builder.HasIndex(d => d.DocumentNumber).IsUnique();
        builder.Property(d => d.OriginalFileName).IsRequired().HasMaxLength(260);
        builder.Property(d => d.FileSha256Hash).IsRequired().HasMaxLength(64);
        builder.HasIndex(d => d.FileSha256Hash).IsUnique().HasFilter("[IsDeleted] = 0");

        builder.Property(d => d.ContentType).IsRequired().HasMaxLength(100);
        builder.Property(d => d.BlobContainer).IsRequired().HasMaxLength(63);
        builder.Property(d => d.BlobPath).IsRequired().HasMaxLength(500);

        builder.Property(d => d.Title).HasMaxLength(300);
        builder.Property(d => d.ReferenceNumber).HasMaxLength(100);
        builder.Property(d => d.SenderName).HasMaxLength(200);
        builder.Property(d => d.RecipientName).HasMaxLength(200);
        builder.Property(d => d.RelatedPersonName).HasMaxLength(200);
        builder.Property(d => d.Currency).HasMaxLength(10);
        builder.Property(d => d.DetectedLanguage).HasMaxLength(10);
        builder.Property(d => d.Keywords).HasMaxLength(1000);
        builder.Property(d => d.RejectionReason).HasMaxLength(500);
        builder.Property(d => d.TotalAmount).HasPrecision(18, 2);

        builder.HasIndex(d => new { d.DepartmentId, d.Status, d.DocumentDate });
        builder.HasIndex(d => d.ReferenceNumber);
        builder.HasIndex(d => d.RelatedPersonName);
        builder.HasIndex(d => d.CreatedAt);

        builder.HasOne(d => d.Department).WithMany(dept => dept.Documents).HasForeignKey(d => d.DepartmentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(d => d.SuggestedDepartment).WithMany().HasForeignKey(d => d.SuggestedDepartmentId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(d => d.DocumentType).WithMany(dt => dt.Documents).HasForeignKey(d => d.DocumentTypeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(d => d.UploadedByUser).WithMany().HasForeignKey(d => d.UploadedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(d => d.ValidatedByUser).WithMany().HasForeignKey(d => d.ValidatedByUserId).OnDelete(DeleteBehavior.NoAction);
    }
}

public class DocumentMetadataConfiguration : IEntityTypeConfiguration<DocumentMetadata>
{
    public void Configure(EntityTypeBuilder<DocumentMetadata> builder)
    {
        builder.ToTable("DocumentMetadata");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Key).IsRequired().HasMaxLength(100);
        builder.Property(m => m.Value).IsRequired().HasMaxLength(1000);
        builder.HasIndex(m => new { m.DocumentId, m.Key });
        builder.HasOne(m => m.Document).WithMany(d => d.MetadataItems).HasForeignKey(m => m.DocumentId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class AIProcessingResultConfiguration : IEntityTypeConfiguration<AIProcessingResult>
{
    public void Configure(EntityTypeBuilder<AIProcessingResult> builder)
    {
        builder.ToTable("AIProcessingResults");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.ModelUsed).IsRequired().HasMaxLength(100);
        builder.Property(a => a.DetectedLanguage).HasMaxLength(10);
        builder.Property(a => a.ErrorMessage).HasMaxLength(2000);
        builder.HasOne(a => a.Document).WithOne(d => d.AIProcessingResult).HasForeignKey<AIProcessingResult>(a => a.DocumentId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class DocumentVersionConfiguration : IEntityTypeConfiguration<DocumentVersion>
{
    public void Configure(EntityTypeBuilder<DocumentVersion> builder)
    {
        builder.ToTable("DocumentVersions");
        builder.HasKey(v => v.Id);
        builder.Property(v => v.BlobContainer).IsRequired().HasMaxLength(63);
        builder.Property(v => v.BlobPath).IsRequired().HasMaxLength(500);
        builder.Property(v => v.FileSha256Hash).IsRequired().HasMaxLength(64);
        builder.Property(v => v.ChangeNote).HasMaxLength(500);
        builder.HasIndex(v => new { v.DocumentId, v.VersionNumber }).IsUnique();
        builder.HasOne(v => v.Document).WithMany(d => d.Versions).HasForeignKey(v => v.DocumentId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class DocumentPermissionConfiguration : IEntityTypeConfiguration<DocumentPermission>
{
    public void Configure(EntityTypeBuilder<DocumentPermission> builder)
    {
        builder.ToTable("DocumentPermissions");
        builder.HasKey(p => p.Id);
        builder.HasOne(p => p.Document).WithMany(d => d.Permissions).HasForeignKey(p => p.DocumentId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(p => p.User).WithMany(u => u.DocumentPermissions).HasForeignKey(p => p.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(p => p.Department).WithMany().HasForeignKey(p => p.DepartmentId).OnDelete(DeleteBehavior.NoAction);
    }
}

public class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("AuditLogs");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.UserEmail).IsRequired().HasMaxLength(256);
        builder.Property(a => a.IpAddress).HasMaxLength(45);
        builder.Property(a => a.EntityName).IsRequired().HasMaxLength(100);
        builder.Property(a => a.Details).HasMaxLength(1000);
        builder.HasIndex(a => a.Timestamp);
        builder.HasIndex(a => new { a.EntityName, a.EntityId });
        builder.HasIndex(a => a.UserId);
    }
}

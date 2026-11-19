using Microsoft.EntityFrameworkCore;
using SmartArchive.Application;
using SmartArchive.Domain;

namespace SmartArchive.Infrastructure;

public sealed class ArchiveDbContext(DbContextOptions<ArchiveDbContext> options) : DbContext(options)
{
    public DbSet<ArchiveDocument> Documents => Set<ArchiveDocument>();
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<DocumentCategory> Categories => Set<DocumentCategory>();
    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<ArchiveDocument>().HasIndex(x => x.ContentHash).IsUnique();
        model.Entity<ArchiveDocument>().HasIndex(x => new { x.DepartmentId, x.Status, x.UploadedAt });
        model.Entity<ArchiveDocument>().HasIndex(x => new { x.DocumentDate, x.ReferenceNumber });
        model.Entity<Department>().HasIndex(x => x.Name).IsUnique();
        model.Entity<ArchiveDocument>().Property(x => x.UploadedAt).HasConversion(v => v.UtcDateTime, v => new DateTimeOffset(DateTime.SpecifyKind(v, DateTimeKind.Utc)));
        model.Entity<ArchiveDocument>().Property(x => x.ValidatedAt).HasConversion(v => v.HasValue ? v.Value.UtcDateTime : (DateTime?)null, v => v.HasValue ? new DateTimeOffset(DateTime.SpecifyKind(v.Value, DateTimeKind.Utc)) : (DateTimeOffset?)null);
        model.Entity<ArchiveDocument>().Property(x => x.Title).HasMaxLength(300);
        model.Entity<ArchiveDocument>().Property(x => x.ExtractedText).HasMaxLength(1000000);
        model.Entity<Department>().HasData(new[] { "Quality", "Maintenance", "Production", "IT", "Logistics", "Human Resources", "Finance", "Purchasing", "Engineering", "General Administration" }.Select((name, i) => new Department { Id = i + 1, Name = name }));
    }
}

public sealed class EfDocumentRepository(ArchiveDbContext db) : IDocumentRepository
{
    public async Task<IReadOnlyList<ArchiveDocument>> ListAsync(string? search = null, DocumentStatus? status = null, int? departmentId = null, CancellationToken ct = default)
    {
        var query = db.Documents.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(search)) query = query.Where(d => d.Title.Contains(search) || d.FileName.Contains(search) || d.ReferenceNumber.Contains(search) || d.DepartmentName.Contains(search) || d.Category.Contains(search) || d.ExtractedText.Contains(search) || d.Keywords.Contains(search));
        if (status is not null) query = query.Where(d => d.Status == status);
        if (departmentId is not null) query = query.Where(d => d.DepartmentId == departmentId);
        return await query.OrderByDescending(d => d.UploadedAt).Take(500).ToListAsync(ct);
    }
    public Task<ArchiveDocument?> GetAsync(Guid id, CancellationToken ct = default) => db.Documents.FirstOrDefaultAsync(d => d.Id == id, ct);
    public async Task AddAsync(ArchiveDocument document, CancellationToken ct = default) { db.Documents.Add(document); await db.SaveChangesAsync(ct); }
    public async Task SaveAsync(ArchiveDocument document, CancellationToken ct = default) { db.Documents.Update(document); await db.SaveChangesAsync(ct); }
    public async Task<IReadOnlyList<Department>> DepartmentsAsync(CancellationToken ct = default) => await db.Departments.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name).ToListAsync(ct);
    public async Task<object> StatisticsAsync(CancellationToken ct = default)
    {
        var start = new DateTimeOffset(DateTimeOffset.UtcNow.UtcDateTime.Date, TimeSpan.Zero);
        var end = start.AddDays(1);
        return new { Total = await db.Documents.CountAsync(d => d.Status == DocumentStatus.Archived, ct), Pending = await db.Documents.CountAsync(d => d.Status == DocumentStatus.PendingValidation || d.Status == DocumentStatus.UnderReview, ct), Today = await db.Documents.CountAsync(d => d.UploadedAt >= start && d.UploadedAt < end, ct), Rejected = await db.Documents.CountAsync(d => d.Status == DocumentStatus.Rejected, ct), Processing = await db.Documents.CountAsync(d => d.Status == DocumentStatus.Processing, ct), ByDepartment = await db.Documents.Where(d => d.Status == DocumentStatus.Archived).GroupBy(d => d.DepartmentName).Select(g => new { Name = g.Key, Count = g.Count() }).ToListAsync(ct) };
    }
}

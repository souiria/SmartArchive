using MudBlazor.Services;
using MudBlazor;
using Microsoft.EntityFrameworkCore;
using SmartArchive.Application;
using SmartArchive.Infrastructure;
using SmartArchive.Web.Components;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddMudServices();
builder.Services.AddSmartArchiveInfrastructure(builder.Configuration, builder.Environment);
builder.Services.AddProblemDetails();
var app = builder.Build();
Directory.CreateDirectory(Path.Combine(app.Environment.ContentRootPath, "App_Data"));
using (var scope = app.Services.CreateScope()) { var db = scope.ServiceProvider.GetRequiredService<ArchiveDbContext>(); await db.Database.EnsureCreatedAsync(); }
if (!app.Environment.IsDevelopment()) { app.UseExceptionHandler(); app.UseHsts(); }
app.UseHttpsRedirection(); app.UseAntiforgery();
app.MapStaticAssets();
app.MapDefaultEndpoints();
app.MapGet("/api/documents", async (string? q, string? status, IDocumentRepository repo) => { Enum.TryParse<SmartArchive.Domain.DocumentStatus>(status, true, out var parsed); return Results.Ok(await repo.ListAsync(q, status is null ? null : parsed)); });
app.MapGet("/api/dashboard", async (IDocumentRepository repo) => Results.Ok(await repo.StatisticsAsync()));
app.MapPost("/api/documents/upload", async (HttpRequest request, IDocumentWorkflow workflow) =>
{
    if (!request.HasFormContentType || request.Form.Files.Count == 0) return Results.BadRequest(new { error = "Sélectionnez un fichier PDF." });
    var results = new List<object>();
    foreach (var file in request.Form.Files)
    {
        await using var stream = file.OpenReadStream();
        try { results.Add(await workflow.UploadAsync(file.FileName, file.ContentType, stream, "Opérateur local")); }
        catch (InvalidOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }
    }
    return Results.Ok(results);
}).DisableAntiforgery();
app.MapPost("/api/documents/{id:guid}/review", async (Guid id, ReviewRequest body, IDocumentWorkflow workflow) =>
{
    try { await workflow.ReviewAsync(id, body.Title, body.Department, body.Category, body.DocumentDate, body.ReferenceNumber, body.Comment ?? "", "Validateur local", body.Approve); return Results.NoContent(); }
    catch (KeyNotFoundException) { return Results.NotFound(); }
    catch (InvalidOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }
});
app.MapPost("/api/documents/{id:guid}/retry", async (Guid id, IDocumentWorkflow workflow) => { try { await workflow.RetryAnalysisAsync(id); return Results.NoContent(); } catch (KeyNotFoundException) { return Results.NotFound(); } });
app.MapGet("/api/documents/{id:guid}/file", async (Guid id, IDocumentWorkflow workflow, IDocumentRepository repo) => { var doc = await repo.GetAsync(id); if (doc is null) return Results.NotFound(); var bytes = await workflow.DownloadAsync(id); return bytes is null ? Results.NotFound() : Results.File(bytes, "application/pdf", doc.FileName, enableRangeProcessing: true); });
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.Run();

public sealed record ReviewRequest(string Title, string Department, string Category, DateOnly? DocumentDate, string ReferenceNumber, string? Comment, bool Approve);

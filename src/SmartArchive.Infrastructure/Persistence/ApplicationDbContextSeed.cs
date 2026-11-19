using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SmartArchive.Domain.Entities;

namespace SmartArchive.Infrastructure.Persistence;

public static class ApplicationDbContextSeed
{
    public static async Task SeedAsync(
        ApplicationDbContext context,
        UserManager<ApplicationUser> userManager,
        RoleManager<ApplicationRole> roleManager)
    {
        var roles = new[]
        {
            new ApplicationRole("Administrator", "Full system access, department and user management."),
            new ApplicationRole("DepartmentManager", "Can validate, archive, edit, and manage documents within assigned department."),
            new ApplicationRole("Employee", "Can upload documents and view/search permitted department documents.")
        };

        foreach (var role in roles)
        {
            if (!await roleManager.RoleExistsAsync(role.Name!))
            {
                await roleManager.CreateAsync(role);
            }
        }

        if (!await context.Departments.AnyAsync())
        {
            var departments = new List<Department>
            {
                new() { Code = "FIN", NameFr = "Finance & Comptabilité", NameAr = "المالية والمحاسبة", DefaultRetentionYears = 10 },
                new() { Code = "HR", NameFr = "Ressources Humaines", NameAr = "الموارد البشرية", DefaultRetentionYears = 30 },
                new() { Code = "LEGAL", NameFr = "Affaires Juridiques", NameAr = "الشؤون القانونية", DefaultRetentionYears = 20 },
                new() { Code = "PROC", NameFr = "Achats & Approvisionnement", NameAr = "المشتريات والتموين", DefaultRetentionYears = 10 },
                new() { Code = "IT", NameFr = "Systèmes d'Information", NameAr = "نظم المعلومات", DefaultRetentionYears = 5 },
                new() { Code = "ADMIN", NameFr = "Administration Générale", NameAr = "الإدارة العامة", DefaultRetentionYears = 10 }
            };

            context.Departments.AddRange(departments);
            await context.SaveChangesAsync();

            var docTypes = new List<DocumentType>
            {
                new()
                {
                    Code = "INV",
                    NameFr = "Facture Fournisseur / Client",
                    NameAr = "فاتورة",
                    AzureModelId = "prebuilt-invoice",
                    ClassificationKeywords = "facture,invoice,فاتورة,tva,ttc,ht,montant,total,fournisseur",
                    DefaultDepartmentId = departments.First(d => d.Code == "FIN").Id
                },
                new()
                {
                    Code = "PO",
                    NameFr = "Bon de Commande",
                    NameAr = "أمر شراء",
                    AzureModelId = "prebuilt-document",
                    ClassificationKeywords = "bon de commande,purchase order,أمر شراء,commande,livraison",
                    DefaultDepartmentId = departments.First(d => d.Code == "PROC").Id
                },
                new()
                {
                    Code = "CONTRACT",
                    NameFr = "Contrat / Convention",
                    NameAr = "عقد / اتفاقية",
                    AzureModelId = "prebuilt-contract",
                    ClassificationKeywords = "contrat,convention,agreement,عقد,اتفاقية,entre les soussignés,article",
                    DefaultDepartmentId = departments.First(d => d.Code == "LEGAL").Id
                },
                new()
                {
                    Code = "HR_DOC",
                    NameFr = "Dossier Employé / Attestation",
                    NameAr = "ملف موظف / شهادة عمل",
                    AzureModelId = "prebuilt-document",
                    ClassificationKeywords = "attestation de travail,bulletin de paie,congé,salaire,شهادة عمل,راتب,employé",
                    DefaultDepartmentId = departments.First(d => d.Code == "HR").Id
                },
                new()
                {
                    Code = "GEN",
                    NameFr = "Courrier / Document Général",
                    NameAr = "مراسلة / وثيقة عامة",
                    AzureModelId = "prebuilt-document",
                    ClassificationKeywords = "objet,madame,monsieur,مراسلة,الموضوع",
                    DefaultDepartmentId = departments.First(d => d.Code == "ADMIN").Id
                }
            };

            context.DocumentTypes.AddRange(docTypes);
            await context.SaveChangesAsync();
        }

        const string adminEmail = "admin@smartarchive.local";
        if (await userManager.FindByEmailAsync(adminEmail) is null)
        {
            var adminUser = new ApplicationUser
            {
                Id = Guid.CreateVersion7(),
                UserName = adminEmail,
                Email = adminEmail,
                EmailConfirmed = true,
                FirstName = "System",
                LastName = "Administrator",
                PreferredLanguage = "fr",
                IsActive = true
            };

            var result = await userManager.CreateAsync(adminUser, "Admin@2026!Secure");
            if (result.Succeeded)
            {
                await userManager.AddToRoleAsync(adminUser, "Administrator");
            }
        }
    }
}

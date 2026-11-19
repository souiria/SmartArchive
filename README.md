# SmartArchive – AI-Powered Enterprise Document Management System (.NET 10)

## Overview
SmartArchive is a Clean Architecture .NET 10 Blazor Web App + ASP.NET Core Web API solution for scanning, classifying, extracting metadata, validating, and archiving enterprise documents using **Azure AI Document Intelligence**, **Azure Blob Storage**, **SQL Server (EF Core 10)**, and **MudBlazor** with full **French (LTR)** and **Arabic (RTL)** support.

## Architecture
- **SmartArchive.Domain**: Entities, Enums, Value Objects, Domain Exceptions (zero external dependencies beyond Identity stores).
- **SmartArchive.Application**: Business Services, DTOs, Interfaces (`IDocumentAiService`, `IBlobStorageService`, `IScannerIngestionService`), Search & Validation Logic.
- **SmartArchive.Infrastructure**: EF Core 10 `ApplicationDbContext`, Audit & Soft-Delete Interceptors, Azure Blob Storage Service, Azure AI Document Intelligence Hybrid Classifier with Polly Resilience, and Background `Channel<Guid>` Worker.
- **SmartArchive.Web**: Blazor Interactive Server UI (MudBlazor), Localization (`fr` / `ar` RTL), Authentication/Authorization, and Scanner REST API (`/api/scanner/ingest`).

## Quick Start
1. Configure `src/SmartArchive.Web/appsettings.json` with your SQL Server connection string, Azure Blob Storage connection string, and Azure AI Document Intelligence Endpoint & API Key.
2. Run EF Core Migrations:
   ```bash
   dotnet ef migrations add InitialCreate --project src/SmartArchive.Infrastructure --startup-project src/SmartArchive.Web --output-dir Persistence/Migrations
   dotnet ef database update --project src/SmartArchive.Infrastructure --startup-project src/SmartArchive.Web
   ```
3. Run the application:
   ```bash
   dotnet run --project src/SmartArchive.Web
   ```
4. Default Administrator Credentials:
   - **Email**: `admin@smartarchive.local`
   - **Password**: `Admin@2026!Secure`

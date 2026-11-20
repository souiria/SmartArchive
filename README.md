# Smart Archive — SEWS Maroc

Socle fonctionnel de gestion documentaire avec dépôt PDF, empreinte anti-doublon, analyse indicative, file de validation humaine obligatoire, archivage après approbation, recherche, aperçu PDF et statistiques. Solution .NET 9 / Blazor Web App avec **MudBlazor 8.15** / EF Core / Aspire.

> **État et sécurité :** cette version est un socle de démonstration/local pilot. L’interface et les endpoints ne disposent pas encore de l’authentification, des rôles, ni des restrictions d’accès par département demandés pour le déploiement en entreprise. Les routes de fichier/API ne doivent pas être exposées à un réseau ou contenir de vrais documents sensibles avant l’ajout et la vérification de ces contrôles. La détection de type PDF est une validation de base, pas une protection antivirus. Les intégrations Azure sont des adaptateurs optionnels, pas une configuration de production prévalidée.

## Prérequis
- SDK .NET 9 (le SDK Aspire des templates 9.x est restauré par NuGet)
- Pour le mode local : aucune ressource Azure/SQL Server nécessaire (SQLite local et stockage sur disque)
- Pour SQL Server/Azure : ressources et secrets propres à l’environnement

## Démarrage local
Depuis la racine du dépôt :

```bash
dotnet restore SmartArchive.slnx
dotnet run --project src/SmartArchive.Web
```

Ouvrir l’URL indiquée par ASP.NET Core. La base SQLite `App_Data/smartarchive.db` est initialisée automatiquement au premier lancement. Les PDF de démo sont enregistrés dans `src/SmartArchive.Web/App_Data/documents` (non versionné). Aspire : `dotnet run --project src/SmartArchive.AppHost`.

## Parcours de démonstration
1. Déposer un PDF depuis « Dépôt de documents » (jusqu’à 50 Mo par fichier, PDF uniquement).
2. L’analyse mock locale fournit des suggestions fondées sur le nom du fichier et les indices de texte disponibles. Sans endpoint Azure, aucun OCR réel n’est exécuté.
3. Examiner et corriger les suggestions dans « Centre de validation ».
4. Seule l’action « Approuver et archiver » place le document dans l’archive; « Rejeter » conserve l’audit de transition.
5. Rechercher les éléments archivés. Les téléchargements/aperçus sont servis via un endpoint applicatif.

## Configuration
Set runtime values through environment variables or a local `appsettings.Development.json`. Never commit secrets:

- `ConnectionStrings__SqlServer`: SQL Server (preferred when configured); otherwise local SQLite.
- `AzureBlobStorage__ConnectionString` and `AzureBlobStorage__Container`: Azure Blob Storage; otherwise local disk storage.
- `AzureDocumentIntelligence__Endpoint`: the Document Intelligence resource endpoint. With `AzureDocumentIntelligence__ApiKey`, the app uses the resource key. If the key is empty, the app uses `DefaultAzureCredential` (for example, `az login` locally or a managed identity in Azure). For Entra ID, use a custom-subdomain endpoint and grant the app identity the `Cognitive Services User` role. Never use a Portal username/password in the application.
- `SmartArchive__User__DisplayName`: the configured display label shown in the app. This is not an authenticated identity; sign-in is not implemented.
- `SmartArchive__Storage__LocalFolder`: relative subfolder inside `App_Data`, default `documents`. The Settings page can change this for new uploads; existing files remain in place and accessible.
- `SmartArchive__Compression__Enabled`: enables lossless GZip storage for PDFs. The app stores the compressed representation only when it is smaller; previews and downloads decompress it to the exact original PDF bytes. No lossy image changes are made.

Azure keys belong only in the hosting secret store or environment, never in source control. With no Document Intelligence endpoint, the app clearly indicates that only local rule-based classification is being used. OCR text extraction uses Azure `prebuilt-read`; department/category suggestions remain rule-based and require human review.

## Limites fonctionnelles connues
- Pas encore d’authentification/autorisation (Identity, RBAC, restrictions département), administration CRUD des départements, gestion versionnée de document, rétention, chiffrement de bout en bout, antimalware ou protection CSRF vérifiée sur tous les parcours.
- L’indexation actuelle utilise des filtres EF Core simples; une recherche full-text distribuée et un traitement OCR asynchrone par file persistante sont nécessaires à l’échelle entreprise.
- Une suggestion de catégorie n’est qu’une heuristique mock, et une analyse Azure `prebuilt-read` donne du texte OCR; elle n’implémente pas de classifieur métier entraîné.
- `EnsureCreated` initialise une base de démo neuve. Avant usage durable avec une base existante, mettre en place et appliquer des migrations EF Core revues.
- Les comptes/permissions, rôles, rôles par département, jobs persistants, mécanisme de réessai asynchrone, logs de conformité protégés et tests de sécurité restent à compléter.

## Architecture
- `SmartArchive.Domain` : entités et statuts métier.
- `SmartArchive.Application` : contrats de repository, workflow, IA et stockage.
- `SmartArchive.Infrastructure` : EF Core SQLite/SQL Server, stockage local/Azure Blob, analyse mock/Azure.
- `SmartArchive.Web` : UI Blazor/MudBlazor responsive, shell navigationnel avec drawer, tableau de bord, dépôt, centre de validation, archive, routes API et bootstrap local.
- `SmartArchive.AppHost` et `SmartArchive.ServiceDefaults` : orchestration Aspire et instrumentation commune.

## Build

```bash
dotnet build SmartArchive.slnx
```

## Tests unitaires

```bash
dotnet test SmartArchive.slnx
```

Les tests couvrent les transitions autorisées/interdites du cycle documentaire, notamment l’interdiction d’archiver directement un document non validé.

# Smart Archive — SEWS Maroc

Socle fonctionnel de gestion documentaire avec dépôt PDF, empreinte anti-doublon, analyse indicative, file de validation humaine obligatoire, archivage après approbation, recherche, aperçu PDF et statistiques. Solution .NET 9 / Blazor Web App / EF Core / Aspire.

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
Créer les clés par variables d’environnement ou `appsettings.Development.json` local (ne jamais committer de secrets) :

- `ConnectionStrings__SqlServer` : SQL Server (prioritaire si défini); sinon SQLite local.
- `AzureBlobStorage__ConnectionString` et `AzureBlobStorage__Container` : Azure Blob Storage; sinon stockage disque local.
- `AzureDocumentIntelligence__Endpoint` et `AzureDocumentIntelligence__ApiKey` : Document Intelligence (modèle `prebuilt-read`). Sans les deux valeurs, le mode heuristique local est utilisé.

Le fichier `.env.example` donne les noms sans valeurs. Les clés Azure réelles restent dans le gestionnaire de secrets de l’hôte.

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
- `SmartArchive.Web` : UI Blazor, routes API et bootstrap local.
- `SmartArchive.AppHost` et `SmartArchive.ServiceDefaults` : orchestration Aspire et instrumentation commune.

## Build

```bash
dotnet build SmartArchive.slnx
```

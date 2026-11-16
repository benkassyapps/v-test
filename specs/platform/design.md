# Design — Plateforme (transverse)

> Répond à : PLT-1 à PLT-8 et aux principes de la [constitution](../constitution.md).
> Les designs fonctionnels sont dans [`catalog-sync/design.md`](../catalog-sync/design.md) et [`cart/design.md`](../cart/design.md).

## 1. Vue d'ensemble

```
                 ┌──────────── docker compose ────────────┐
   client ──► nginx :8080 ──► api #1 ─┐                   │
                 (round-robin) ► api #2 ─┼──► SQL Server 2022 ◄── migrator (one-shot, avant les api)
                 │                      │        ▲
                 │  chaque instance : API HTTP + planificateur de synchro
                 └────────────────────────────────────────┘
                                         DummyJSON (HTTPS) ◄── synchro (une seule instance à la fois)
```

- Toutes les instances sont **identiques et sans état** : même image, même configuration. Seul l'identifiant d'instance diffère, et il est déduit du nom d'hôte (PLT-1).
- **SQL Server est le seul point de coordination** : transactions et verrous de ligne pour le panier, verrou applicatif pour la synchro.

## 2. Solution et dépendances entre projets

```
Shop.slnx
├─ src/
│  ├─ Shop.Domain          (aucune dépendance)
│  ├─ Shop.Application     → Domain ; Cronos
│  ├─ Shop.Infrastructure  → Application ; EF Core SqlServer, DistributedLock.SqlServer, Http.Resilience
│  └─ Shop.Api             → Application, Infrastructure ; OpenApi, Scalar
└─ tests/
   ├─ Shop.Testing           (bibliothèque partagée : conteneur SQL, 2 hôtes, faux fournisseur, horloge, logs)
   ├─ Shop.UnitTests         (xUnit v3 → Domain, Application ; TraceabilityTests)
   ├─ Shop.IntegrationTests  (xUnit v3 → Shop.Testing)
   └─ Shop.AcceptanceTests   (Reqnroll.xunit.v3 → Shop.Testing ; .feature liés depuis specs/)
```

- `Shop.Api` ne référence Infrastructure que pour l'enregistrement des services (`AddInfrastructure()`) : les endpoints n'appellent que les services d'Application.
- Application définit des **ports par cas d'usage** (pas de repository générique) : `ICartStore`, `ICartReader`, `IProductReader`, `ICatalogProvider`, `ICatalogStore`, `ISyncRunStore`, `ICatalogSyncLock`.
- `Directory.Build.props` : `net10.0`, `Nullable` activé, `TreatWarningsAsErrors`, `ImplicitUsings`. `Directory.Packages.props` : versions centralisées, figées à la tâche de squelette.

| Bibliothèque | Rôle | Justification |
|---|---|---|
| EF Core 10 (SqlServer) | Persistance, migrations | Standard .NET ; SQL brut paramétré (`FromSql`) là où les hints de verrou sont nécessaires. |
| DistributedLock.SqlServer | Verrou exclusif de synchro | Bibliothèque éprouvée sur `sp_getapplock` ; libération si la connexion tombe ; `HandleLostToken`. |
| Cronos | Calcul du créneau quotidien | Parsing cron fiable, gestion UTC. |
| Microsoft.Extensions.Http.Resilience | Appels au fournisseur | Retry, timeout, circuit breaker standard (Polly v8). |
| Microsoft.AspNetCore.OpenApi + Scalar.AspNetCore | Documentation (PLT-7) | OpenAPI natif .NET 10 + interface d'exploration. |
| xUnit v3, Reqnroll.xunit.v3, Testcontainers.MsSql | Tests | Reqnroll ≥ 3.1 prend en charge xUnit v3 ; SQL Server réel en test. |
| Microsoft.Extensions.TimeProvider.Testing, Microsoft.Extensions.Diagnostics.Testing | Tests | `FakeTimeProvider` (horloge) et `FakeLogger` (vérification des logs, CAT-R12). |

## 3. Pipeline HTTP (`Shop.Api/Program.cs`)

1. **Mode migrateur** : si l'argument `--migrate` est présent, l'application applique les migrations EF puis s'arrête avec le code 0 (ou 1 en cas d'échec), sans démarrer le serveur HTTP ni le planificateur (PLT-5).
2. Middleware `X-Instance` : ajoute l'identifiant d'instance à chaque réponse (PLT-1).
3. `UseExceptionHandler` + `IExceptionHandler` maison (section 5) et `UseStatusCodePages` : toute erreur sort en ProblemDetails.
4. Authentification : schéma `UserId` (en-tête `X-User-Id`) ; endpoints admin protégés par un filtre de clé d'administration.
5. Endpoints par fonctionnalité : `MapProductEndpoints()`, `MapCartEndpoints()`, `MapAdminCatalogEndpoints()`, `/health`, `/openapi/v1.json`, `/scalar` (si `Api:EnableDocs`).
6. Validation des entrées : validation intégrée des minimal APIs .NET 10 (`AddValidation()`, DataAnnotations avec messages en français).

## 4. Identification de l'utilisateur (PLT-2)

- `UserIdAuthenticationHandler : AuthenticationHandler<…>` lit `X-User-Id`, le valide avec `^[A-Za-z0-9._@-]{1,64}$` et crée un `ClaimsPrincipal` (claim `sub`). En cas d'absence ou d'invalidité : échec, puis challenge en `401 user_not_identified` au format ProblemDetails.
- Les endpoints panier exigent `RequireAuthorization()` et obtiennent l'utilisateur par `ICurrentUser` (lecture du claim). Remplacer ce handler par `AddJwtBearer` ne touche à aucun endpoint.
- La colonne `Carts.UserId` utilise une **collation binaire** (`Latin1_General_100_BIN2`) : l'identifiant est opaque et sensible à la casse (`alice` ≠ `Alice`). La collation par défaut de SQL Server, insensible à la casse, aurait fusionné les deux.
- **Clé d'administration** : un filtre d'endpoint compare `X-Admin-Key` à `Admin:ApiKey` avec `CryptographicOperations.FixedTimeEquals` (pas de fuite par le temps de réponse). Absente ou invalide : `403 admin_key_invalid`. Une clé vide en configuration désactive les endpoints admin (403 systématique).

## 5. Contrat d'erreurs (PLT-3)

- Les services d'Application renvoient un `Result` / `Result<T>` portant une `DomainError(Code, Message, Extensions)`. **Aucune exception pour les cas métier attendus.**
- La couche Api convertit `DomainError` en ProblemDetails par une table unique `ErrorCode → statut HTTP` (`Errors/ProblemMapping.cs`) :

| `code` | Statut |
|---|---|
| `validation_failed` | 400 |
| `user_not_identified` | 401 |
| `admin_key_invalid` | 403 |
| `cart_not_found`, `cart_line_not_found` | 404 |
| `cart_line_version_conflict` (+ `currentVersion`), `sync_already_running` | 409 |
| `product_unavailable`, `quantity_limit_exceeded`, `sync_guard_triggered` (+ `wouldDiscontinue`, `activeProducts`) | 422 |
| `provider_unavailable` | 502 |
| `service_unavailable` | 503 |
| `internal_error` | 500 |

- `type` = `https://errors.shop.local/{code}` (URI stable et documentée), `title` et `detail` en français, `instance` = chemin de la requête, extension `traceId`.
- **Exceptions inattendues** (`IExceptionHandler`) :
  - exceptions de base transitoires (`SqlException` à numéro transitoire, `TimeoutException`, échec après épuisement des nouvelles tentatives EF) → `503 service_unavailable` ;
  - toute autre exception → `500 internal_error`.
  Dans les deux cas, le détail réel est **logué**, jamais renvoyé.
- Les erreurs de validation (400) sont enrichies par `CustomizeProblemDetails` : `code = validation_failed` et `errors` détaillé par champ.

## 6. Persistance et migrations (PLT-5)

- Un seul `ShopDbContext` (Infrastructure) avec configurations Fluent par fonctionnalité (`Persistence/Configurations/Catalog/*`, `.../Carts/*`). Le schéma par défaut est `dbo`.
- **Stratégie de rejeu** : elle n'est **pas** activée globalement (`EnableRetryOnFailure` interdirait les transactions explicites hors stratégie). Une `SqlServerRetryingExecutionStrategy` (3 essais, délai max 2 s) est instanciée explicitement pour les opérations **idempotentes** : lectures, création ou récupération du panier, application de la synchro (le même instantané produit le même état). Elle enveloppe l'unité de travail entière, transaction comprise.
- **Pas de rejeu automatique pour les commandes de panier** : si un commit réussit mais que son accusé de réception est perdu, un rejeu appliquerait la commande deux fois (quantité doublée). Elles s'exécutent sans nouvelle tentative et renvoient `503` en cas d'erreur transitoire (voir [cart/design.md §3](../cart/design.md)).
- **Migrations** : migrations EF dans Infrastructure. La migration initiale contient, en premier, `ALTER DATABASE CURRENT SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE` (`suppressTransaction: true`), exécutée par le migrateur seul.
- **Contrôle au démarrage** : un `IHostedService` lit `sys.databases.is_read_committed_snapshot_on` et émet un avertissement (`ReadCommittedSnapshotDisabled`) si RCSI est désactivé. La correction n'en dépend pas.
- `Database:MigrateOnStartup` (faux par défaut) : migration au démarrage, pour le développement en instance unique uniquement.

## 7. Configuration

| Clé | Défaut | Rôle |
|---|---|---|
| `ConnectionStrings:Shop` | — (obligatoire) | Base SQL Server. |
| `Instance:Id` | nom d'hôte | Identifiant d'instance (`X-Instance`, `SyncRuns.InstanceId`). |
| `Admin:ApiKey` | vide (admin désactivé) | Clé d'administration. |
| `Api:EnableDocs` | `true` en Development | Expose `/scalar` et `/openapi`. |
| `Database:MigrateOnStartup` | `false` | Voir §6. |
| `CatalogSync:*` | voir [catalog-sync/design.md](../catalog-sync/design.md#configuration) | Fournisseur, planification, garde-fou. |

Secrets (PLT-8) : aucune valeur sensible dans `appsettings*.json`. On utilise les variables d'environnement (`ConnectionStrings__Shop`, `Admin__ApiKey`) dans compose via `.env` (non versionné ; `.env.example` versionné avec des valeurs factices) et `dotnet user-secrets` en local.

## 8. Observabilité (PLT-6)

- Logs structurés via `ILogger` avec des messages **en français** et `LoggerMessage` (source generator) pour des `EventId` stables. Format JSON (`AddJsonConsole`) dans les conteneurs.
- Événements stables : `CatalogSyncStarted` (1000), `CatalogSyncSucceeded` (1001), `CatalogSyncFailed` (1002), `CatalogSyncGuardTriggered` (1003), `CatalogSyncSkipped` (1004), `CatalogSyncLockLost` (1005), `ReadCommittedSnapshotDisabled` (2000), `UnhandledException` (9000).
- `/health` : `AddHealthChecks().AddDbContextCheck<ShopDbContext>()` (`Healthy` / `Unhealthy`).

## 9. Exécution locale (PLT-4)

- `Dockerfile` multi-étapes (`sdk:10.0` → `aspnet:10.0`), utilisateur non root, même image pour `api` et `migrator` (argument `--migrate`).
- `docker-compose.yml` :
  - `sqlserver` (`mcr.microsoft.com/mssql/server:2022-latest`, healthcheck via `sqlcmd`) ;
  - `migrator` (`depends_on: sqlserver: service_healthy`) ;
  - `api` (`deploy.replicas: 2`, `depends_on: migrator: service_completed_successfully`) ;
  - `nginx` (`:8080`, round-robin sur le service `api` via le DNS Docker).
- `deploy/nginx.conf` : `resolver 127.0.0.11` + `upstream` dynamique pour répartir sur les replicas.

## 10. Infrastructure de test (`Shop.Testing`)

- **Un conteneur SQL Server** (Testcontainers) par exécution de tests, base migrée une fois. Avant chaque scénario, les tables sont vidées dans l'ordre des clés étrangères (`CartLines`, `Carts`, `Products`, `SyncRuns`) et les identités sont réinitialisées. Les scénarios s'exécutent séquentiellement dans une même base.
- **Deux hôtes** `WebApplicationFactory<Program>` (`Instance:Id = instance-1 / instance-2`) sur la même base : les scénarios multi-instance envoient leurs requêtes à l'un ou à l'autre.
- Services remplacés dans les deux hôtes (instances partagées) :
  - `FakeDummyJsonHandler` (`HttpMessageHandler` du client fournisseur) : catalogue programmable, pagination réelle, pannes à partir de la page N, total mensonger, et **porte** (`TaskCompletionSource`) pour maintenir une synchro « en cours » ;
  - `FakeTimeProvider` : horloge contrôlée ;
  - `FakeLogCollector` : vérification des événements de log ;
  - `FaultInjectionInterceptor` (EF `SaveChangesInterceptor`) : lève une `TimeoutException` tant qu'il est armé, pour simuler une panne de base (CART-R12, CAT-R8).
- Le planificateur `BackgroundService` est **désactivé** en test (`CatalogSync:SchedulerEnabled=false`). Les scénarios appellent directement `ICatalogSyncScheduler.CheckAsync()` sur l'instance voulue.

## 11. Traçabilité automatisée (`TraceabilityTests`)

- Lit les ID d'exigences dans les titres `### CAT-R…`, `### CART-R…`, `### PLT-…` des `requirements.md`.
- Lit les tags `@ID` des `.feature` et les `[Trait("Spec", "ID")]` dans le **source** des projets de test (analyse des fichiers `.cs`, sans dépendance aux autres assemblies).
- Échoue si une exigence n'est couverte par rien, ou si un tag ou un trait cite un ID inconnu. Liste d'exemptions explicite et commentée : `PLT-4` (vérification manuelle documentée).

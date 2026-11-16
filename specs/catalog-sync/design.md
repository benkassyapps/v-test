# Design — Catalogue et synchronisation

> Répond à : CAT-R1 à CAT-R12. Socle transverse : [platform/design.md](../platform/design.md).

## 1. Modèle de données

### `Products`
| Colonne | Type | Contraintes / rôle |
|---|---|---|
| `Id` | `int identity` | PK. Identifiant interne exposé par l'API (`productId`) et référencé par `CartLines`. |
| `ExternalId` | `int` | **Index unique** `UX_Products_ExternalId` : un produit fournisseur = un produit local (CAT-R4). |
| `Name` | `nvarchar(200)` | |
| `Category` | `nvarchar(100)` | |
| `Price` | `decimal(18,2)` | CHECK `Price >= 0`. |
| `Stock` | `int` | Disponibilité indicative. |
| `AvailabilityStatus` | `nvarchar(50)` | Libellé du fournisseur (`In Stock`, `Low Stock`…). |
| `Status` | `tinyint` | `Active = 1`, `Discontinued = 2`. |
| `ProviderUpdatedAt` | `datetimeoffset null` | `meta.updatedAt` du fournisseur (information). |
| `LastUpdatedAt` | `datetimeoffset` | Dernier changement **effectif** des données locales (CAT-R5.2). |
| `DiscontinuedAt` | `datetimeoffset null` | Date du retrait. |

- Index `IX_Products_Status_Id (Status, Id)` : sert la pagination keyset des produits actifs (seek direct).
- **Choix** : pas de colonne « dernière vérification » par produit. La mettre à jour à chaque synchro écrirait toutes les lignes à chaque run, ce qui contredit l'idempotence (CAT-R4.3). La date de dernière vérification d'un produit actif est celle du dernier run réussi (`SyncRuns`). *La décision D4 de `requirements.md` est corrigée en ce sens.*

### `SyncRuns`
| Colonne | Type | Rôle |
|---|---|---|
| `Id` | `bigint identity` | PK, ordre chronologique (keyset de l'historique). |
| `Trigger` | `tinyint` | `Scheduled`, `Manual`. |
| `Status` | `tinyint` | `Running`, `Succeeded`, `Failed`, `Skipped`. |
| `FailureReason` | `tinyint null` | `ProviderError`, `InvalidSnapshot`, `GuardTriggered`, `DatabaseError`, `Interrupted`. |
| `Forced` | `bit` | Forçage du garde-fou utilisé (CAT-R3.6). |
| `InstanceId` | `nvarchar(100)` | Instance exécutante. |
| `StartedAt`, `FinishedAt` | `datetimeoffset` (null pour `FinishedAt`) | |
| `Fetched`, `Inserted`, `Updated`, `Discontinued`, `Reactivated` | `int` | Compteurs. |
| `ActiveBefore`, `WouldDiscontinue` | `int null` | Renseignés si le garde-fou bloque (diagnostic CAT-R12). |
| `Error` | `nvarchar(1000) null` | Message de cause **assaini** (pas de pile, pas de SQL). |

Index `IX_SyncRuns_Status_Trigger (Status, Trigger, Id DESC)` pour les requêtes du planificateur.

## 2. Composants

```
Shop.Domain/Catalog
  Product                    entité : Create(ProviderProduct, now) ; HasSameData(ProviderProduct) ; ApplyFrom(ProviderProduct, now) ; Discontinue(now) — la décision insertion/mise à jour/réactivation/retrait appartient à SyncPlanner
  ProviderProduct            record : ExternalId, Name, Category, Price, Stock, AvailabilityStatus, ProviderUpdatedAt
  CatalogSnapshot            liste validée de ProviderProduct (complète, sans doublon)
  SyncPlan / SyncPlanner     calcul PUR des changements (insérer / mettre à jour / retirer / réactiver) + compteurs
  DiscontinuationGuard       règle du seuil (CAT-R11)
  SyncRun                    entité : Start, Succeed(counts), Fail(reason, message), Skip ; garde l'historique

Shop.Application/Catalog
  ICatalogProvider           FetchSnapshotAsync() → CatalogSnapshot   (lève ProviderUnavailable / InvalidSnapshot)
  ICatalogSyncLock           TryAcquireAsync() → ILockHandle? (avec HandleLostToken)
  ICatalogStore              ApplyAsync(Func<IReadOnlyList<Product>, Result<SyncCounts>>) : charge, applique, valide en UNE transaction
  ISyncRunStore              Add/Update de SyncRun, état pour le planificateur et pour sync-status, historique paginé
  CatalogSyncService         RunAsync(trigger, force) : orchestration (§3)
  SyncSchedulePolicy         décision PURE : la synchro planifiée est-elle due ? prochaine tentative ? (§4)
  CatalogSyncScheduler       CheckAsync() : appelle la politique puis RunAsync(Scheduled)
  SyncStatusQuery            état pour l'exploitant (§5)
  IProductReader / ProductQueries   liste keyset (§6)

Shop.Infrastructure/Catalog
  DummyJsonCatalogProvider   HttpClient typé + résilience ; validation de complétude
  SqlCatalogSyncLock         DistributedLock.SqlServer
  CatalogStore, SyncRunStore, ProductReader   EF Core
  CatalogSyncHostedService   BackgroundService : PeriodicTimer → scheduler.CheckAsync()
```

## 3. Déroulé d'une synchronisation (`CatalogSyncService.RunAsync`)

```
1. handle ← lock.TryAcquireAsync(timeout: 0)
   └─ null → Scheduled : enregistrer run Skipped (+ log CatalogSyncSkipped) ; Manual : Result sync_already_running (409)
2. (Scheduled uniquement) revérifier SOUS LE VERROU que la synchro est due (SyncSchedulePolicy)
   └─ plus due (une autre instance vient de réussir) → fin sans run (pas de bruit dans l'historique)
3. Marquer `Failed/Interrupted` les runs restés `Running` : on détient le verrou, donc aucun ne s'exécute réellement
   (ils viennent d'une instance arrêtée brutalement).
4. run ← SyncRun.Start(trigger, instance, forced) ; enregistré et validé immédiatement (visible dans l'historique)
5. snapshot ← provider.FetchSnapshotAsync()            ← AUCUNE écriture catalogue pendant le fetch
   └─ erreur → run.Fail(ProviderError | InvalidSnapshot) ; log CatalogSyncFailed ; Manual → 502 provider_unavailable
6. catalogStore.ApplyAsync(products ⇒
       plan ← SyncPlanner.Plan(products, snapshot)                 (pur)
       si !force et DiscontinuationGuard.IsTriggered(plan) → erreur sync_guard_triggered (→ rollback)
       plan.ApplyTo(products, now) ; nouveaux produits ajoutés
   )  → une transaction : SaveChanges + COMMIT, ou ROLLBACK complet
   └─ garde-fou → run.Fail(GuardTriggered, activeBefore, wouldDiscontinue) ; log CatalogSyncGuardTriggered ; Manual → 422
   └─ exception base → run.Fail(DatabaseError) ; log CatalogSyncFailed ; Manual → 503
7. run.Succeed(counts) ; log CatalogSyncSucceeded
8. libération du verrou (dispose), y compris en cas d'exception
```

**Points de conception**
- **Tout-ou-rien (CAT-R8)** : le fetch complet précède toute écriture, et l'application se fait en une seule transaction. Une coupure à n'importe quel moment laisse le catalogue dans l'état du dernier commit. Le run peut rester `Running` en cas d'arrêt brutal : il est corrigé à l'étape 3 du run suivant.
- **Enregistrement du run séparé de la transaction catalogue** : sinon un échec effacerait aussi la trace de l'échec.
- **Perte du verrou** : si la connexion du verrou tombe pendant le run (`HandleLostToken`), le jeton d'annulation de l'étape 6 est déclenché, puis rollback, `Failed/Interrupted` et log `CatalogSyncLockLost`. Cela empêche deux instances d'appliquer en même temps.
- **Verrou** : `SqlDistributedLock("catalog-sync", connectionString)` utilise `sp_getapplock` en portée **session** sur une connexion dédiée. Si le processus meurt, SQL Server libère le verrou à la fermeture de la connexion (CAT-R10.4).
- **Annulation du déclenchement manuel** : le run ne suit pas `HttpContext.RequestAborted`. Un client qui se déconnecte n'interrompt pas la synchro. Seul l'arrêt de l'hôte l'annule (le rollback reste propre).

### SyncPlanner (pur, testé unitairement)
Pour chaque `ProviderProduct` du snapshot, indexé par `ExternalId` :
- inconnu localement → **insertion** ;
- connu et `Active` avec des champs métier différents (nom, catégorie, prix, stock, disponibilité) → **mise à jour**, avec `LastUpdatedAt = now` ;
- connu et `Active` avec des champs identiques → **aucune écriture** (idempotence, CAT-R4.3) ;
- connu et `Discontinued` → **réactivation** + mise à jour des champs, `LastUpdatedAt = now`.

Pour chaque produit local `Active` absent du snapshot → **retrait** (`Status = Discontinued`, `DiscontinuedAt = now`, `LastUpdatedAt = now`).

### DiscontinuationGuard (CAT-R11)
`triggered = activeBefore > 0 && toDiscontinue / activeBefore > MaxDiscontinuedRatio`, avec un seuil **strict** : exactement 50 % passe. Le catalogue vide est le cas `toDiscontinue = activeBefore`.

### Fournisseur DummyJSON
- `GET {BaseUrl}/products?limit={PageSize}&skip={n}&select=title,category,price,stock,availabilityStatus,meta` (l'`id` est toujours renvoyé), `PageSize = 100`.
- Boucle jusqu'à `skip >= total`. **Validation de complétude** : `total` est cohérent d'une page à l'autre, aucune page vide avant `total`, aucun `id` en double, nombre distinct = `total`. Sinon `InvalidSnapshot`.
- Les données mal formées (prix négatif, nom vide) produisent `InvalidSnapshot` : on ne commit jamais un snapshot douteux.
- Résilience : `AddStandardResilienceHandler` (3 nouvelles tentatives exponentielles sur 5xx, 408 et 429, timeout de 10 s par tentative, 60 s au total, circuit breaker). Une fois épuisée → `ProviderUnavailable`.

## 4. Planification (CAT-R2, CAT-R10)

`CatalogSyncHostedService` (sur **chaque** instance) appelle `scheduler.CheckAsync()` toutes les `CatalogSync:TickInterval` (1 min). Le tick est peu coûteux : une requête sur `SyncRuns`.

`SyncSchedulePolicy.Evaluate(now, state)` → `{ IsDue, NextAttemptAt }`, avec `state = { lastSuccessAt, consecutiveScheduledFailures, lastFailureAt }` lu depuis `SyncRuns` :

```
lastSlot  = dernière occurrence du cron ≤ now           (Cronos : occurrences entre now − 2 jours et now)
dueBySlot = lastSuccessAt est null OU lastSuccessAt < lastSlot
backoff   = n = 0 ? 0 : min(BaseRetryDelay × 2^(n−1), MaxRetryDelay)      (15 min, 30, 60, 120, 120…)
IsDue     = dueBySlot ET (n = 0 OU now ≥ lastFailureAt + backoff)
```
- `consecutiveScheduledFailures` = nombre de runs `Scheduled` en `Failed` **après** la dernière réussite (manuelle ou planifiée). Une réussite remet donc le compteur à zéro (CAT-R2.4). Les `Skipped` ne comptent pas.
- **Deux instances en même temps** : les deux voient `IsDue`, une seule obtient le verrou. L'autre enregistre `Skipped`, ou, si elle obtient le verrou juste après, la revérification sous verrou (étape 2) constate la réussite et s'arrête. Résultat : **un seul run réussi par créneau** (CAT-R10.3).
- Première mise en service (aucun run) : `dueBySlot` est vrai, donc le catalogue est synchronisé dès le démarrage.

## 5. État pour l'exploitant (CAT-R12)

`GET /api/admin/catalog/sync-status` →
```json
{
  "lastSuccess": { "runId": 41, "finishedAt": "…", "age": "PT11H55M" },
  "lastRun": { "runId": 43, "trigger": "Scheduled", "status": "Failed", "failureReason": "GuardTriggered", "error": "…", "finishedAt": "…" },
  "consecutiveFailures": 2,
  "nextScheduledAttemptAt": "…",
  "actionRequired": true,
  "reason": "garde-fou de retrait massif déclenché : 194 produits sur 194 seraient retirés",
  "recommendedAction": "Vérifier le fournisseur ; si le retrait est légitime, forcer : POST /api/admin/catalog/sync?force=true"
}
```
Règle de `actionRequired` (pure, testée unitairement) :
1. le dernier run (hors `Skipped`) est bloqué par le garde-fou → raison « garde-fou » ;
2. sinon, aucune réussite depuis plus de `StaleAfter`, ou jamais de réussite alors qu'au moins un run a échoué → raison « catalogue non synchronisé depuis plus de 26 h » ;
3. sinon → `false`.

## 6. Liste des produits (CAT-R6, CAT-R7)

- `GET /api/products?limit=20&cursor=…` → `{ items: [{ id, externalId, name, category, price, stock, availabilityStatus, lastUpdatedAt }], nextCursor, hasMore }`.
- Requête : `Products.AsNoTracking().Where(Status == Active && Id > lastId).OrderBy(Id).Take(limit + 1)`. Le `(limit+1)`-ième élément ne sert qu'à calculer `hasMore`. Il n'y a pas de `COUNT`.
- **Curseur** : base64url de `{"v":1,"id":<dernier id>}`. Le champ `v` permet de faire évoluer le format ou le tri. Un curseur non décodable, d'une version inconnue ou avec un `id` non positif donne `400 validation_failed`.
- Le parcours est exact même si le catalogue change : la condition `Id > dernierId` ne dépend pas de la position (CAT-R6.4). Les nouveaux produits, qui reçoivent un `Id` croissant, apparaissent en fin de parcours.
- Lecture **exclusivement locale** (CAT-R7) ; non bloquée par la transaction de synchro grâce à RCSI.

## 7. Endpoints d'administration

| Méthode | Route | Réponses |
|---|---|---|
| `POST` | `/api/admin/catalog/sync?force=false` | 200 résumé · 403 · 409 `sync_already_running` · 422 `sync_guard_triggered` · 502 `provider_unavailable` · 503 |
| `GET` | `/api/admin/catalog/sync-runs?limit=20&cursor=…` | 200 historique (du plus récent au plus ancien, keyset sur `Id DESC`) · 403 |
| `GET` | `/api/admin/catalog/sync-status` | 200 état (§5) · 403 |

## 8. Configuration

| Clé | Défaut | Rôle |
|---|---|---|
| `CatalogSync:BaseUrl` | `https://dummyjson.com` | Fournisseur. |
| `CatalogSync:PageSize` | `100` | Taille des pages demandées. |
| `CatalogSync:Cron` | `0 2 * * *` (UTC) | Créneau quotidien. |
| `CatalogSync:SchedulerEnabled` | `true` | Planificateur actif sur l'instance. |
| `CatalogSync:TickInterval` | `00:01:00` | Fréquence de vérification. |
| `CatalogSync:BaseRetryDelay` / `MaxRetryDelay` | `00:15:00` / `02:00:00` | Espacement des tentatives (CAT-R2.4). |
| `CatalogSync:MaxDiscontinuedRatio` | `0.5` | Seuil du garde-fou (CAT-R11). |
| `CatalogSync:StaleAfter` | `26:00:00` | Fraîcheur maximale (CAT-R12). |

## 9. Limites assumées et évolutions
- **Catalogue chargé en mémoire et appliqué en une transaction** : adapté à des milliers de produits. Au-delà, il faudrait une table de staging alimentée par `SqlBulkCopy` puis un `MERGE` ensembliste (toujours en une transaction), et un fetch en flux.
- **Déclenchement manuel synchrone** : un mode asynchrone (`202` + suivi du run) devient nécessaire si la durée augmente.
- Historique `SyncRuns` sans purge : quelques lignes par jour. Une purge planifiée est une évolution.
- Multi-fournisseurs : ajouter `Provider` et passer l'unicité à `(Provider, ExternalId)`.

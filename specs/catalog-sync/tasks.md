# Tâches — Catalogue et synchronisation

> Ordre global et conventions : [platform/tasks.md](../platform/tasks.md). Design : [design.md](design.md).

## Bloc 2 — Domaine (partie catalogue)

### T2.3 — Entités et calcul de synchronisation
- [ ] `Product` (`Create`, `ApplyFrom` → `ProductChange`, `Discontinue`), `ProviderProduct`, `CatalogSnapshot`.
- [ ] `SyncPlanner.Plan(existing, snapshot)` → `SyncPlan` (insertions, mises à jour, retraits, réactivations, compteurs) et `SyncPlan.ApplyTo(...)`.
- [ ] `DiscontinuationGuard` (seuil strict, cas du premier import).
- [ ] `SyncRun` (`Start`, `Succeed`, `Fail(reason)`, `Skip`, `Interrupted`).
- **Exigences** : CAT-R4, CAT-R5, CAT-R9, CAT-R11. **Tests unitaires** : données identiques → aucun changement ; changement de champ → mise à jour avec `LastUpdatedAt` ; produit absent → retrait ; produit revenu → réactivation ; seuil 50 % passe et 50 % + 1 bloque ; catalogue vide → bloqué ; catalogue local vide → jamais bloqué.

### T2.4 — Politiques d'Application
- [ ] `SyncSchedulePolicy` (Cronos : dernier créneau échu, espacement 15 min → 2 h, prochaine tentative).
- [ ] Règle `actionRequired` de `sync-status` (garde-fou, catalogue périmé, jamais réussi).
- [ ] `ProductCursor` (encodage base64url `{"v":1,"id":…}`, décodage strict).
- **Exigences** : CAT-R2, CAT-R6, CAT-R12. **Tests unitaires** : tous les cas des scénarios du planificateur de `declenchement.feature` (avant ou après le créneau, déjà réussi, rattrapage après plusieurs jours, 1/3/6 échecs juste avant et juste après le délai) ; curseurs valides, tronqués, d'une version inconnue, avec un id ≤ 0.

---

## Bloc 3 — Catalogue

### T3.1 — Persistance du catalogue
- [ ] Configurations EF `Products` et `SyncRuns` (types, CHECK, `UX_Products_ExternalId`, `IX_Products_Status_Id`, `IX_SyncRuns_Status_Trigger`), migration `Catalog`.
- [ ] `ProductReader` (keyset), `SyncRunStore` (état du planificateur, historique keyset, correction des runs `Running` orphelins).
- **Exigences** : CAT-R4, CAT-R5, CAT-R6. **Tests d'intégration** : l'index unique refuse un doublon d'`ExternalId` ; le plan d'exécution de la liste utilise un seek sur `IX_Products_Status_Id` (vérification consignée).

### T3.2 — Fournisseur DummyJSON
- [ ] `DummyJsonCatalogProvider` : client typé, `select`, pagination jusqu'à `total`, validation de complétude et de données, `AddStandardResilienceHandler`, erreurs `ProviderUnavailable` / `InvalidSnapshot`.
- **Exigences** : CAT-R1, CAT-R7. **Tests d'intégration** (`FakeDummyJsonHandler`) : pagination complète ; page vide prématurée, doublon, total incohérent → `InvalidSnapshot` ; 5xx persistants → `ProviderUnavailable` après les nouvelles tentatives.

### T3.3 — Service de synchronisation
- [ ] `SqlCatalogSyncLock` (DistributedLock.SqlServer, `HandleLostToken`).
- [ ] `CatalogStore.ApplyAsync` (une transaction, rejeu idempotent autorisé).
- [ ] `CatalogSyncService.RunAsync(trigger, force)` : déroulé complet de [design §3](design.md#3-déroulé-dune-synchronisation-catalogsyncservicerunasync), logs `CatalogSync*`, annulation indépendante de la requête HTTP.
- **Exigences** : CAT-R1, CAT-R3, CAT-R8, CAT-R10, CAT-R11, CAT-R12. **Tests d'intégration** : deux runs simultanés → un seul applique ; **coupure de la connexion du verrou → verrou libéré, run suivant possible (CAT-R10.4)** ; panne de base pendant l'application → catalogue inchangé.

### T3.4 — Planificateur
- [ ] `CatalogSyncScheduler.CheckAsync()` (politique + revérification sous verrou), `CatalogSyncHostedService` (`PeriodicTimer`, `SchedulerEnabled`).
- **Exigences** : CAT-R2, CAT-R10. **Tests d'intégration** : deux planificateurs simultanés → un seul run réussi pour le créneau.

### T3.5 — Endpoints
- [ ] `GET /api/products` (limit 1–100, curseur, 400 `validation_failed`).
- [ ] `POST /api/admin/catalog/sync?force=`, `GET /api/admin/catalog/sync-runs`, `GET /api/admin/catalog/sync-status`, avec mapping des résultats vers 200/403/409/422/502/503.
- **Exigences** : CAT-R3, CAT-R6, CAT-R12, PLT-3.

### T3.6 — Scénarios d'acceptation du catalogue
- [ ] Étapes Reqnroll et inclusion de `synchronisation.feature`, `declenchement.feature`, `liste-produits.feature`.
- **Exigences** : CAT-R1 à CAT-R12. **Fin** : tous les scénarios verts sur 2 instances.

**🔍 Fin du bloc 3** : `/code-review high` (attention : verrou, transaction, rollback, runs orphelins) → tri → validation → commit.

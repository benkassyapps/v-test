# Tâches — Plateforme, et ordre d'exécution global

> Convention : chaque tâche indique les exigences couvertes, ce qui est livré, les tests attendus et son critère de fin.
> Une tâche n'est cochée que lorsque `dotnet build` passe sans avertissement et que `dotnet test` est vert.
> La matrice [`../traceability.md`](../traceability.md) est mise à jour à la fin de chaque tâche.

## Ordre d'exécution global

| Bloc | Contenu | Fichier | Fin de bloc |
|---|---|---|---|
| **1. Socle technique** | Solution, outillage agent, hôte Api, persistance de base, infrastructure de test | ce fichier | `/code-review` + validation |
| **2. Domaine** | Règles pures du panier et du catalogue, politiques (planification, garde-fou, curseur) | [cart/tasks.md](../cart/tasks.md) (B2), [catalog-sync/tasks.md](../catalog-sync/tasks.md) (B2) | `/code-review` + validation |
| **3. Catalogue** | Persistance, fournisseur, synchro, planificateur, endpoints produits et admin, scénarios | [catalog-sync/tasks.md](../catalog-sync/tasks.md) (B3) | `/code-review` + validation |
| **4. Panier** | Persistance, transaction, endpoints, stress de concurrence, scénarios | [cart/tasks.md](../cart/tasks.md) (B4) | `/code-review` + **`/security-review`** + validation |
| **5. Exécution locale** | Docker, compose multi-instance, vérification manuelle | ce fichier | `/code-review` + validation |
| **6. Documentation et livraison** | README, DECISIONS, bilan IA, revue finale | ce fichier | **`/security-review` final** + validation |

**Activation progressive des scénarios d'acceptation** : le projet `Shop.AcceptanceTests` liste explicitement les `.feature` qu'il exécute. Un fichier y est ajouté au bloc où ses étapes sont implémentées. Un scénario n'est donc jamais « en attente » dans une exécution verte. Au bloc 6, tous les `.feature` de `specs/` doivent être inclus (vérifié par `TraceabilityTests`).

---

## Bloc 1 — Socle technique

### T1.1 — Solution et conventions
- [ ] `Shop.slnx`, 4 projets `src/` et 4 projets `tests/`, avec les références conformes à [design §2](design.md#2-solution-et-dépendances-entre-projets).
- [ ] `Directory.Build.props` (net10.0, Nullable, TreatWarningsAsErrors, ImplicitUsings), `Directory.Packages.props` (versions **vérifiées sur NuGet** à l'installation, puis figées).
- [ ] `.gitignore`, `.gitattributes` (fins de ligne normalisées), `.editorconfig`.
- **Exigences** : constitution §4, §8. **Fin** : `dotnet build` vert, aucun avertissement.

### T1.2 — Outillage agent
- [ ] `AGENTS.md` (workflow SDD, boucle de revue, conventions de langue, invariants de concurrence, commandes, interdiction des secrets) et `CLAUDE.md` qui y renvoie.
- [ ] `.claude/commands/spec-task.md` (implémenter une tâche) et `.claude/commands/spec-check.md` (couverture).
- [ ] `docs/ai-reviews.md` (modèle de journal des revues).
- **Exigences** : constitution §1, §8. **Fin** : fichiers relus par le candidat.

### T1.3 — Traçabilité automatisée
- [ ] `TraceabilityTests` dans `Shop.UnitTests` ([design §11](design.md#11-traçabilité-automatisée-traceabilitytests)) : exigences ↔ tags `.feature` ↔ `Trait("Spec")`, ID inconnus, exemption `PLT-4`, et `.feature` non inclus dans le projet d'acceptation (contrôle actif au bloc 6 seulement).
- **Exigences** : constitution §1.4. **Tests** : le test lui-même, avec un cas négatif sur des fichiers d'exemple. **Fin** : vert.

### T1.4 — Hôte Api minimal
- [ ] `Program.cs` : configuration, `Instance:Id`, middleware `X-Instance`, `AddProblemDetails` + `IExceptionHandler` + `ProblemMapping` (table complète des codes), `AddValidation()`.
- [ ] Schéma d'authentification `UserId` (`X-User-Id`, regex), `ICurrentUser`, filtre de clé d'administration (`FixedTimeEquals`).
- [ ] OpenAPI + Scalar (`Api:EnableDocs`), `/health`.
- [ ] `LoggerMessage` : catalogue d'`EventId` stables ([design §8](design.md#8-observabilité-plt-6)).
- **Exigences** : PLT-1, PLT-2, PLT-3, PLT-6, PLT-7. **Tests** : unitaires sur `ProblemMapping` et sur la validation de `X-User-Id`. **Fin** : l'application démarre, `/health` et `/openapi/v1.json` répondent.

### T1.5 — Persistance de base et migrateur
- [ ] `ShopDbContext` (Infrastructure), chaîne de connexion obligatoire.
- [ ] Migration `EnableReadCommittedSnapshot` (`ALTER DATABASE CURRENT … WITH ROLLBACK IMMEDIATE`, `suppressTransaction`).
- [ ] Mode `--migrate` (migration puis sortie avec code 0/1) ; option `Database:MigrateOnStartup`.
- [ ] `IHostedService` de contrôle RCSI (avertissement `ReadCommittedSnapshotDisabled`).
- [ ] Stratégie de rejeu explicite réservée aux opérations idempotentes ([design §6](design.md#6-persistance-et-migrations-plt-5)).
- **Exigences** : PLT-5. **Fin** : `dotnet run -- --migrate` sur un SQL Server local crée la base avec RCSI.

### T1.6 — Infrastructure de test partagée
- [ ] `Shop.Testing` : conteneur SQL Server (Testcontainers) unique et migré, remise à zéro des tables, deux `WebApplicationFactory` (`instance-1`, `instance-2`), `FakeTimeProvider`, collecteur de logs, `FaultInjectionInterceptor`, clients HTTP par utilisateur et par instance.
- [ ] `FakeDummyJsonHandler` (catalogue programmable, pagination, panne à partir de la page N, total mensonger, porte de blocage). Il sera utilisé à partir du bloc 3 et créé ici pour stabiliser l'API de test.
- [ ] Tests d'intégration : migrations appliquées et RCSI actif (**PLT-5**) ; deux hôtes répondent avec des `X-Instance` différents.
- [ ] Test unitaire **PLT-8** : aucun secret non vide dans les `appsettings*.json` versionnés.
- **Exigences** : PLT-1, PLT-5, PLT-8. **Fin** : vert, avec Docker requis.

### T1.7 — Projet d'acceptation
- [ ] `Shop.AcceptanceTests` (Reqnroll.xunit.v3), `reqnroll.json` (langue `fr`), mécanisme d'inclusion explicite des `.feature` depuis `specs/`, hooks (remise à zéro avant chaque scénario).
- [ ] Étapes communes : « le service fonctionne avec 2 instances », « la réponse a le statut … », « la réponse est une erreur … de code … ».
- **Fin** : le projet compile et s'exécute (aucun `.feature` inclus pour l'instant).

**🔍 Fin du bloc 1** : `/code-review high` → tri des constats (`docs/ai-reviews.md`) → validation → commit.

---

## Bloc 5 — Exécution locale

### T5.1 — Conteneurisation
- [ ] `src/Shop.Api/Dockerfile` multi-étapes, utilisateur non root.
- [ ] `docker-compose.yml` : `sqlserver` (healthcheck) → `migrator` (`--migrate`) → `api` (`replicas: 2`) → `nginx` (`:8080`).
- [ ] `deploy/nginx.conf` (resolver Docker, round-robin), `.env.example` (valeurs factices), `.env` ignoré par git.
- **Exigences** : PLT-4, PLT-8.

### T5.2 — Vérification manuelle multi-instance
- [ ] `src/Shop.Api/Shop.Api.http` : appels de toutes les opérations, dont le scénario d'Alice.
- [ ] Déroulé vérifié et consigné : `docker compose up --build` ; synchro manuelle (≈194 insertions) puis relance (0 changement) ; `X-Instance` qui alterne ; Alice v4 → v6 ; même ligne → 409 ; `BaseUrl` invalide → `Failed`, produits toujours listés ; `sync-status`.
- **Exigences** : PLT-4 (exemptée de `TraceabilityTests`, vérifiée ici). **Fin** : résultats notés dans le README.

**🔍 Fin du bloc 5** : `/code-review high` → tri → validation → commit.

---

## Bloc 6 — Documentation et livraison

### T6.1 — README.md
- [ ] Prérequis, démarrage (compose et local), tests, synchro manuelle (curl), appels principaux de l'API, identification de l'utilisateur, contrat d'erreurs, configuration.

### T6.2 — DECISIONS.md (≤ 2 pages)
- [ ] Architecture et alternatives écartées, concurrence du panier, synchro (instantané, transaction, garde-fou, verrou), planification, pagination, limites, évolutions. Synthèse des `design.md`, sans les recopier.

### T6.3 — Bilan IA
- [ ] Finaliser `AI_USAGE.md` (outils, démarche, vérifications, journal) et `docs/ai-reviews.md`.
- [ ] Inventaire des fichiers d'outillage réellement utilisés (`AGENTS.md`, `CLAUDE.md`, `.claude/commands/*`), sans secret ni historique de conversation.

### T6.4 — Revue finale
- [ ] Tous les `.feature` inclus ; `TraceabilityTests` vert ; matrice entièrement à ✅.
- [ ] **`/security-review` final** → tri des constats.
- [ ] Relecture complète du dépôt par le candidat.

**🔍 Fin du bloc 6** : validation finale → commit.

# Catalogue de produits et panier personnel — service .NET 10

Service HTTP qui :
- **synchronise chaque jour** le catalogue du fournisseur [DummyJSON](https://dummyjson.com/docs/products) dans une base SQL Server locale (déclenchement manuel possible) et expose une **liste paginée** des produits ;
- gère le **panier personnel** de chaque utilisateur, avec une **version globale** et une **version par ligne**, sans conflit injustifié ni modification écrasée en silence ;
- reste **cohérent avec plusieurs instances** qui traitent des requêtes et des synchronisations en parallèle.

### Par où commencer

1. **Ce README** : démarrer, tester, appeler l'API.
2. **[DECISIONS.md](DECISIONS.md)** : architecture, décisions, compromis, limites et évolutions (2 pages).
3. **[docs/AI_USAGE.md](docs/AI_USAGE.md)** : bilan de l'usage de l'IA et outillage fourni.

Le reste est en **annexe**, à consulter pour le détail d'un point :

| Annexe | Contenu |
|---|---|
| [specs/](specs/) | Spécifications (démarche spec-driven) : constitution, exigences, critères d'acceptation Gherkin **exécutés**, designs, tâches, traçabilité |
| [docs/verification-manuelle.md](docs/verification-manuelle.md) | Vérification multi-instance de bout en bout avec docker compose et le vrai DummyJSON |
| [docs/ai-reviews.md](docs/ai-reviews.md) | Revues de code et de sécurité, et suite donnée à chaque constat |
| [docs/ai-journal.md](docs/ai-journal.md) | Journal complet des propositions de l'IA validées, adaptées ou rejetées |

---

## 1. Démarrer (docker compose, recommandé)

**Prérequis** : Docker (Docker Desktop sous Windows ou macOS).

```bash
cp .env.example .env          # valeurs factices à adapter ; .env n'est jamais versionné
docker compose up --build -d
```

Ordre de démarrage : **SQL Server** → **migrateur** (applique les migrations seul, puis s'arrête) → **2 instances de l'API** → **nginx** sur <http://localhost:8080>, qui répartit les requêtes entre les instances. L'en-tête `X-Instance` de chaque réponse indique l'instance qui l'a traitée.

Au premier démarrage, le planificateur synchronise le catalogue automatiquement (~194 produits, une seule instance le fait). Ensuite : chaque jour à 02:00 UTC.

- Santé : <http://localhost:8080/health>
- Documentation interactive de l'API (Scalar) : <http://localhost:8080/scalar> — description OpenAPI : `/openapi/v1.json`
- Arrêt : `docker compose down` (ajouter `-v` pour effacer aussi la base)
- **Après une mise à jour du code**, sur une pile déjà lancée : `docker compose down` (sans `-v`, la base est conservée) puis `docker compose up --build -d`. Sans reconstruction, les instances gardent l'ancienne image ; et le migrateur ne s'exécute seul, comme il le doit, qu'au démarrage d'une pile arrêtée.

### Sans Docker pour l'API (développement)

```bash
docker compose up -d sqlserver                                  # SQL Server seul (port local 14333)
dotnet user-secrets set "ConnectionStrings:Shop" "Server=localhost,14333;Database=Shop;User Id=sa;Password=<MSSQL_SA_PASSWORD du .env>;TrustServerCertificate=true" --project src/Shop.Api
dotnet user-secrets set "Admin:ApiKey" "<ADMIN_API_KEY du .env>" --project src/Shop.Api
dotnet run --project src/Shop.Api -- --migrate                  # migrations
dotnet run --project src/Shop.Api                               # http://localhost:5038 (Scalar : /scalar)
```

## 2. Lancer les tests

**Prérequis** : SDK .NET 10 et **Docker en marche** (SQL Server réel via Testcontainers).

```bash
dotnet test Shop.slnx
```

| Projet | Contenu | Volume |
|---|---|---|
| `Shop.UnitTests` | Règles du domaine (panier, synchronisation), politiques (planification, garde-fou, curseur), empreinte d'idempotence, contrat d'erreurs, **traçabilité** exigences ↔ tests, absence de secrets | 181 |
| `Shop.IntegrationTests` | SQL Server réel, **2 instances** : verrou distribué (y compris coupure de connexion), barrière de synchronisation, contraintes, plan d'exécution, **stress de concurrence répété** (30 itérations), cas déterministes de verrouillage, purge des clés d'idempotence | 32 |
| `Shop.AcceptanceTests` | **Critères d'acceptation Gherkin** de `specs/**/acceptance/*.feature` (Reqnroll, en français), sur 2 instances | 118 scénarios |

Filtrer par exigence : `dotnet test --filter "Spec=CART-R9"` (tests xUnit) ; les scénarios portent le tag correspondant (`@CART-R9`). Le test `TraceabilityTests` échoue si une exigence n'est couverte par aucun scénario ni test, ou si un `.feature` n'est pas exécuté.

## 3. Synchronisation manuelle

Les endpoints d'administration exigent l'en-tête `X-Admin-Key` (valeur `ADMIN_API_KEY` du `.env`).

```bash
KEY=exemple-cle-admin-locale

# Déclencher : même traitement que la synchronisation quotidienne ; réponse = résumé du run
curl -X POST -H "X-Admin-Key: $KEY" http://localhost:8080/api/admin/catalog/sync

# Forcer un retrait massif légitime (lève le garde-fou, après vérification du fournisseur)
curl -X POST -H "X-Admin-Key: $KEY" "http://localhost:8080/api/admin/catalog/sync?force=true"

# Historique (du plus récent au plus ancien) et état avec action conseillée
curl -H "X-Admin-Key: $KEY" "http://localhost:8080/api/admin/catalog/sync-runs?limit=10"
curl -H "X-Admin-Key: $KEY" http://localhost:8080/api/admin/catalog/sync-status
```

| Réponse | Signification |
|---|---|
| `200` | Run terminé avec succès : compteurs `fetched`, `inserted`, `updated`, `discontinued`, `reactivated` |
| `409 sync_already_running` | Une synchronisation est déjà en cours (sur cette instance ou une autre) |
| `422 sync_guard_triggered` | Plus de 50 % des produits actifs seraient retirés : catalogue inchangé ; vérifier le fournisseur, puis forcer si c'est légitime |
| `502 provider_unavailable` | Fournisseur indisponible ou réponse incomplète : catalogue inchangé |

Simuler une panne du fournisseur : `CATALOG_BASE_URL=http://fournisseur.invalid docker compose up -d api`. La liste des produits et les paniers restent disponibles.

## 4. Appeler l'API

L'**utilisateur courant** est identifié par l'en-tête `X-User-Id` (1 à 64 caractères parmi lettres, chiffres et `. _ - @`, sensible à la casse). Ce n'est pas une authentification : voir [DECISIONS.md](DECISIONS.md). Aucune route ne prend d'identifiant de panier : chacun n'accède qu'au sien. Le fichier [src/Shop.Api/Shop.Api.http](src/Shop.Api/Shop.Api.http) contient tous les appels.

```bash
B=http://localhost:8080

# Produits actifs, pagination par curseur : réutiliser nextCursor tant que hasMore = true
curl "$B/api/products?limit=20"
curl "$B/api/products?limit=20&cursor=<nextCursor>"

# Créer ou récupérer son panier (201 si créé, 200 s'il existait)
curl -X PUT -H "X-User-Id: alice" $B/api/cart

# Ajouter un produit (crée le panier si besoin ; réajouter cumule la quantité)
curl -X POST -H "X-User-Id: alice" -H "Content-Type: application/json" \
     -d '{"productId": 1, "quantity": 2}' $B/api/cart/lines

# Consulter : lignes (lineId, nom et prix conservés, quantité, version), total, version du panier
curl -H "X-User-Id: alice" $B/api/cart

# Remplacer la quantité d'une ligne, à partir de la version de ligne consultée
curl -X PUT -H "X-User-Id: alice" -H "Content-Type: application/json" \
     -d '{"quantity": 5, "expectedVersion": 1}' $B/api/cart/lines/<lineId>

# Supprimer une ligne
curl -X DELETE -H "X-User-Id: alice" "$B/api/cart/lines/<lineId>?expectedVersion=2"
```

### Réessayer sans risque : la clé d'idempotence

Les commandes de ligne (`POST`, `PUT` et `DELETE` sur `/api/cart/lines`) acceptent l'en-tête **`Idempotency-Key`** (recommandé). Générez **une clé par intention** (un UUID, par exemple) et **réutilisez-la à chaque nouvelle tentative** de la même demande. Si la première tentative a été enregistrée mais que sa réponse s'est perdue (coupure, `503`, délai dépassé), la nouvelle tentative **n'est pas réappliquée** : la réponse d'origine est rejouée avec l'en-tête `Idempotent-Replayed: true`.

```bash
curl -X POST -H "X-User-Id: alice" -H "Idempotency-Key: 7f9c2e1a-ajout-mascara" -H "Content-Type: application/json" \
     -d '{"productId": 1, "quantity": 2}' $B/api/cart/lines
```

Une clé est propre à chaque utilisateur et expire après 24 h (`Cart:IdempotencyKeyTtl`). La réutiliser pour une requête **différente** renvoie `422 idempotency_key_reused`.

### Contrat d'erreurs

Toutes les erreurs sont au format **ProblemDetails** (RFC 9457, `application/problem+json`), avec un `title` et un `detail` en français et une extension **`code`** stable.

| Statut | `code` | Situation |
|---|---|---|
| 400 | `validation_failed` | Entrée invalide ; `errors` détaille chaque champ |
| 401 | `user_not_identified` | `X-User-Id` absent ou invalide |
| 403 | `admin_key_invalid` | Clé d'administration absente ou invalide |
| 404 | `cart_not_found`, `cart_line_not_found` | Pas de panier, ou ligne inexistante (ou supprimée) dans **votre** panier |
| 409 | `cart_line_version_conflict` | La ligne a changé depuis votre lecture ; `currentVersion` indique la version actuelle |
| 422 | `product_unavailable`, `quantity_limit_exceeded` | Produit inexistant ou retiré ; quantité cumulée > 999 |
| 422 | `idempotency_key_reused` | Clé d'idempotence déjà utilisée pour une requête différente |
| 503 | `service_unavailable` | Base momentanément indisponible ; réessayer avec la même `Idempotency-Key` est sans risque |

## 5. Configuration

| Clé (variable d'environnement : `:` → `__`) | Défaut | Rôle |
|---|---|---|
| `ConnectionStrings:Shop` | — (obligatoire) | Base SQL Server |
| `Admin:ApiKey` | vide = administration désactivée | Clé de l'en-tête `X-Admin-Key` |
| `Instance:Id` | nom d'hôte | Identifiant d'instance (`X-Instance`, historique des runs) |
| `CatalogSync:Cron` | `0 2 * * *` (UTC) | Créneau quotidien |
| `CatalogSync:SchedulerEnabled` | `true` | Planificateur actif sur l'instance |
| `CatalogSync:MaxDiscontinuedRatio` | `0.5` | Seuil du garde-fou de retrait massif |
| `CatalogSync:StaleAfter` | `26:00:00` | Au-delà sans réussite : `actionRequired` dans `sync-status` |
| `CatalogSync:PageTimeout` | `00:00:30` | Délai pour obtenir une page complète du fournisseur |
| `CatalogSync:Http:*` | 10 s par tentative, 60 s au total | Pipeline de résilience HTTP (nouvelles tentatives, coupe-circuit) |
| `Cart:IdempotencyKeyTtl` | `24:00:00` | Durée de conservation des clés d'idempotence |
| `Api:EnableDocs` | `true` en Development | Expose `/scalar` et `/openapi/v1.json` |

Liste complète : [specs/catalog-sync/design.md §8](specs/catalog-sync/design.md#8-configuration) et [specs/platform/design.md §7](specs/platform/design.md#7-configuration).

## 6. Organisation du dépôt

```
specs/          spécifications (source de vérité) : constitution, requirements, acceptance/*.feature, design, tasks, traçabilité
src/            Shop.Domain (règles pures) ← Shop.Application (cas d'usage) ← Shop.Infrastructure (SQL Server, DummyJSON) / Shop.Api
tests/          Shop.UnitTests, Shop.IntegrationTests, Shop.AcceptanceTests (Reqnroll), Shop.Testing (infrastructure partagée)
deploy/         configuration nginx
docs/           bilan IA (AI_USAGE.md, ai-journal.md), vérification manuelle, journal des revues
DECISIONS.md    architecture, décisions, compromis, limites et évolutions
AGENTS.md       instructions des agents IA (CLAUDE.md y renvoie) ; .claude/commands : commandes /spec-task et /spec-check
```

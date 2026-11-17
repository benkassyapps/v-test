# Décisions d'architecture

> Synthèse. Le détail et les justifications se trouvent dans `specs/*/design.md` ; le suivi des revues dans `docs/ai-reviews.md`.

## 1. Démarche et architecture

- **Spec-driven** : exigences EARS (`CAT-R*`, `CART-R*`, `PLT-*`) → critères Gherkin **exécutés** par Reqnroll → design → tâches. `TraceabilityTests` vérifie à chaque `dotnet test` que chaque exigence est couverte. Quand le code contredisait le design, **la spec a été corrigée d'abord**.
- **Clean Architecture en 4 projets, organisée par fonctionnalité** : `Domain` (règles pures) ← `Application` (cas d'usage, ports) ← `Infrastructure` / `Api`. Écartés : *vertical slices* (défendables, mais la frontière du domaine repose sur la discipline), monolithe modulaire (une seule base et 2 modules : coût supérieur au gain), MediatR et repository générique (indirection sans valeur ici).
- **Bibliothèques éprouvées** plutôt que du code maison : EF Core 10, `DistributedLock.SqlServer`, Cronos, `Microsoft.Extensions.Http.Resilience`.

## 2. Panier : cohérence sous concurrence, multi-instance

**Mécanisme** : chaque commande s'exécute dans une transaction qui commence par **`SELECT … FROM Carts WITH (UPDLOCK, ROWLOCK)`**, puis charge les lignes dans une seconde instruction. Les écritures d'un même panier sont ainsi sérialisées, quelle que soit l'instance, et chacune décide sur l'état validé le plus récent. L'agrégat `Cart` applique les règles et ne compare que la **version de la ligne** visée.
- **Exemple d'Alice** : les deux modifications de lignes différentes passent l'une après l'autre, d'où 2 succès et un panier en v6. Même ligne, même version : la seconde voit la ligne en v2, d'où **409** avec `currentVersion`.
- **Pessimiste plutôt qu'optimiste** : un jeton de concurrence sur le panier ferait échouer à tort des modifications de lignes différentes et imposerait des nouvelles tentatives. Coût assumé : quelques millisecondes d'attente entre écritures d'un **même** panier ; les lectures ne sont jamais bloquées (`READ_COMMITTED_SNAPSHOT`).
- **Création implicite optimiste** : l'index unique `UX_Carts_UserId` arbitre les créations simultanées ; la perdante reprend sa commande. `HOLDLOCK` **écarté après revue** : son verrou de plage bloquait le panier de l'utilisateur voisin (prouvé par un test).
- **Produit retiré pendant un ajout** : lecture du produit en `REPEATABLEREAD`. Ordre des verrous unique (`Carts` → `Products`), donc pas d'interblocage avec la synchronisation.
- **Jamais de rejeu côté serveur, une clé d'idempotence pour le client** : si l'accusé d'un commit est perdu, un rejeu doublerait une quantité. Le serveur et nginx ne rejouent donc jamais une commande (503). Le client réessaie sans risque avec `Idempotency-Key` : clé et réponse sont enregistrées **dans la même transaction** que la commande, et une même clé rejoue la réponse d'origine.
- **Filets de sécurité en base** : CHECK `1–999`, index uniques, versions en jetons de concurrence EF.
- **Nom et prix figés** dans la ligne au premier ajout ; un réajout après suppression crée une **nouvelle** ligne, qu'une demande périmée ne peut donc pas viser.

## 3. Catalogue : synchronisation sûre

- **Instantané complet avant toute écriture** (pagination jusqu'au `total` annoncé, validation), puis application en **une seule transaction** : une panne laisse le catalogue intact, la synchronisation suivante rattrape.
- **Exclusivité multi-instance** : verrou `sp_getapplock` **et barrière sur la ligne du run**. Le verrou pouvant être perdu à l'insu de l'instance, la transaction d'application vérifie que son run est toujours « en cours » (ajouté après revue).
- **Produits disparus** : retrait logique, jamais de suppression ; non listés et non ajoutables, mais visibles dans les paniers ; réactivés s'ils reviennent.
- **Garde-fou** : une synchronisation qui retirerait plus de 50 % des produits actifs est bloquée ; seul un exploitant peut forcer (`force=true`). Signalement par des logs `Error` et par `sync-status` (`actionRequired`, action conseillée).
- **Planification** : sur chaque instance, un `BackgroundService` vérifie chaque minute si le dernier créneau cron est couvert, puis revérifie sous verrou. Une seule réussite par créneau, rattrapage après une panne, espacement progressif des tentatives (15 min → 2 h). Écartés : Hangfire et Quartz (lourds pour un seul job), cron externe.
- **Pagination par curseur** (`Id > dernier`) : exacte même si le catalogue change entre deux pages, à coût constant (vérifié sur le plan d'exécution). Pas de `totalPages`.
- **Clé interne + `ExternalId` unique** : paniers et API indépendants des identifiants du fournisseur.

## 4. API et exploitation

- **Erreurs** : ProblemDetails avec un `code` stable, une table unique code → statut, sans détail interne.
- **Utilisateur** : en-tête `X-User-Id` lu par un `AuthenticationHandler` ; les endpoints ne lisent que le claim `sub`.
- **Migrations** par un **migrateur dédié**, exécuté seul avant les instances : l'activation de `READ_COMMITTED_SNAPSHOT` exige d'être la seule connexion.
- **Tests sur la vraie base** (Testcontainers) avec **2 instances**, validés par **mutation** : chaque défaut introduit (seuil non strict, version du panier comparée, retour de `HOLDLOCK`, délai par page retiré, clé d'idempotence ignorée) fait échouer au moins un test.

## 5. Limites assumées

- `X-User-Id` **n'est pas une authentification** (périmètre du sujet) ; clé d'administration statique et partagée.
- Catalogue chargé en mémoire et appliqué en une transaction : adapté à des milliers de produits, pas à des millions. Synchronisation manuelle **synchrone**.
- Clé d'idempotence facultative : sans elle, une nouvelle tentative après une perte de connexion peut doubler une commande.
- Pas de devise ni de remise.
- nginx peut répondre 502 pendant au plus 10 s après la recréation d'une instance ; pas de contrôle de santé dans compose. Une nouvelle migration suppose d'arrêter d'abord les instances.

## 6. Évolutions possibles

| Si… | Alors |
|---|---|
| Vraie authentification | `AddJwtBearer` à la place du handler (aucun endpoint à modifier) ; rôle « exploitant » |
| Catalogue très volumineux | Staging + `SqlBulkCopy` + `MERGE` ; lecture en flux ; synchronisation manuelle asynchrone (`202` + suivi du run) |
| Plusieurs fournisseurs | Unicité `(Provider, ExternalId)`, un `ICatalogProvider` par fournisseur |
| Forte charge en lecture | Cache de la liste invalidé par la synchronisation ; réplicas en lecture |
| Panier → commande | Événements de domaine et *outbox* dans la même transaction |
| Production | Orchestrateur avec sondes `/health`, métriques OpenTelemetry, alertes sur les événements `CatalogSync*`, migrations « expand/contract » |

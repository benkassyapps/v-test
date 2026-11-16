# Tâches — Panier personnel

> Ordre global et conventions : [platform/tasks.md](../platform/tasks.md). Design : [design.md](design.md).

## Bloc 2 — Domaine (partie panier)

### T2.1 — Socle des résultats et erreurs
- [x] `Result`, `Result<T>`, `DomainError(Code, Message, Extensions)` et catalogue des codes (`Shop.Domain/Common`).
- **Exigences** : PLT-3 (côté domaine). **Tests unitaires** : construction et propagation.

### T2.2 — Agrégat `Cart`
- [x] `Cart`, `CartLine`, `ProductSnapshot`, `CartChange` ; `AddItem`, `ChangeQuantity`, `RemoveLine`, `Total`, selon l'ordre des vérifications de [design §2](design.md#2-domaine-shopdomaincarts--règles-pures).
- **Exigences** : CART-R3 à CART-R11, CART-R14 (montants). **Tests unitaires**, un par règle au minimum :
  - nouvelle ligne : version 1, nom et prix copiés, panier +1 ;
  - réajout : cumul, ligne +1, panier +1, nom et prix inchangés ;
  - bornes 0 / 1 / 999 / 1000 ; cumul 998 + 1 accepté, 998 + 2 refusé **sans aucune modification** (état comparé avant et après) ;
  - produit absent ou retiré → refus ;
  - changement de quantité : remplacement et non addition ; même valeur → `Unchanged` sans version modifiée ; version périmée → conflit avec `currentVersion` ; ligne absente → introuvable ;
  - produit retiré : réduction acceptée, augmentation refusée, suppression acceptée ;
  - suppression : version vérifiée, panier +1 ;
  - toute erreur laisse l'agrégat strictement identique ; le total inclut les produits retirés.

---

## Bloc 4 — Panier

### T4.1 — Persistance du panier
- [x] Configurations EF `Carts` (collation BIN2, `UX_Carts_UserId`, jeton de concurrence) et `CartLines` (CHECK 1–999, `UX_CartLines_CartId_ProductId`, FK `Restrict` vers `Products`, jeton de concurrence), migration `Carts`.
- **Exigences** : CART-R1, CART-R4, CART-R5. **Tests d'intégration** : chaque contrainte refuse l'état invalide par une écriture SQL directe (CHECK, unicités) ; `alice` et `Alice` donnent deux paniers distincts.

### T4.2 — Unité de travail transactionnelle
- [x] `CartStore.ExecuteAsync` : verrou `UPDLOCK, ROWLOCK` PUIS chargement des lignes (deux instructions), création implicite **optimiste** (index unique + reprise de la commande sur collision ; pas de `HOLDLOCK`), lecture du produit en `REPEATABLEREAD` seulement si nécessaire, rollback sur erreur et sur `Unchanged`, **aucun rejeu sur erreur transitoire**, vue construite après le commit.
- [x] `CartStore.GetOrCreateAsync` (collision 2601/2627 → panier existant, sans rejeu) et `CartReader.FindAsync` (panier et lignes en une instruction, rejeu idempotent `TransientRetry`) ; vue unique `CartView.From` pour la lecture et les réponses.
- **Exigences** : CART-R1, CART-R2, CART-R7, CART-R9, CART-R10, CART-R12, CART-R15.

### T4.3 — Service et endpoints
- [x] `CartService` (GetOrCreate, Get, AddItem, ChangeQuantity, RemoveLine) et vue `CartView` (`productAvailable`, montants).
- [x] Endpoints `PUT/GET /api/cart`, `POST /api/cart/lines`, `PUT/DELETE /api/cart/lines/{lineId}`, validation explicite des entrées (messages en français partagés avec le domaine, détail par champ), mapping des erreurs.
- **Exigences** : CART-R13 à CART-R17, PLT-2, PLT-3.

### T4.4 — Stress de concurrence
- [x] Tests d'intégration **répétés** (ex. 50 itérations chacun, requêtes réparties sur les 2 hôtes et démarrées ensemble par une barrière) :
  - créations simultanées → 1 panier ;
  - premiers ajouts simultanés sans panier → 1 panier ;
  - N ajouts simultanés → somme exacte, version +N ;
  - lignes différentes → toujours 2 succès ;
  - même ligne et même version → toujours exactement 1 succès ;
  - suppression ∥ modification → jamais de réapparition ;
  - ajout ∥ synchro qui retire le produit → jamais d'ajout d'un produit déjà retiré.
- [x] Après chaque itération : invariant global vérifié en SQL (un panier par utilisateur, `Version` = nombre de commandes acceptées).
- **Exigences** : CART-R1, CART-R3, CART-R5, CART-R9, CART-R10, PLT-1.

### T4.5 — Scénarios d'acceptation du panier et de la plateforme
- [x] Étapes Reqnroll et inclusion de `creation-consultation.feature`, `ajout.feature`, `modification-suppression.feature`, `concurrence.feature`, `panier-et-synchronisation.feature`, `platform/acceptance/plateforme.feature`.
- [x] Tests de non-régression issus de la revue du bloc 1 (`docs/ai-reviews.md`) : une réponse 503 (panne de base simulée) porte l'en-tête `X-Instance` ; une requête panier sans `X-User-Id` avec `Accept: text/plain` reçoit 401 et non 500.
- **Exigences** : CART-R1 à CART-R17, PLT-1, PLT-2, PLT-3, PLT-6, PLT-7. **Fin** : tous les scénarios verts sur 2 instances.

**🔍 Fin du bloc 4** : `/code-review high` (attention : verrous, ordre des verrous, rollback, création implicite) puis **`/security-review`** (identification, clé admin, isolation des paniers, validation, ProblemDetails, SQL brut paramétré) → tri des constats → validation → commit.

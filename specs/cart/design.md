# Design — Panier personnel

> Répond à : CART-R1 à CART-R17 (et PLT-1 à PLT-3 pour le contrat HTTP). Socle transverse : [platform/design.md](../platform/design.md).

## 1. Modèle de données

### `Carts`
| Colonne | Type | Contraintes / rôle |
|---|---|---|
| `Id` | `uniqueidentifier` | PK. GUID v7 (`Guid.CreateVersion7()`), ordonné dans le temps pour la localité de l'index. |
| `UserId` | `nvarchar(64)` collation `Latin1_General_100_BIN2` | **Index unique** `UX_Carts_UserId` : un panier par utilisateur (CART-R1) ; sensible à la casse. |
| `Version` | `bigint` | Version globale, initialisée à 0 (CART-R7). **Jeton de concurrence EF.** |
| `CreatedAt`, `UpdatedAt` | `datetimeoffset` | |

### `CartLines`
| Colonne | Type | Contraintes / rôle |
|---|---|---|
| `Id` | `uniqueidentifier` | PK = `lineId` exposé par l'API. Nouveau à chaque création de ligne (CART-R10.2). |
| `CartId` | `uniqueidentifier` | FK → `Carts` (suppression en cascade). |
| `ProductId` | `int` | FK → `Products` (suppression interdite : un produit n'est jamais supprimé physiquement). |
| `ProductName` | `nvarchar(200)` | Copié au 1er ajout (CART-R6). |
| `UnitPrice` | `decimal(18,2)` | Copié au 1er ajout (CART-R6). |
| `Quantity` | `int` | **CHECK `Quantity BETWEEN 1 AND 999`** (CART-R4). |
| `Version` | `bigint` | Version de ligne, 1 à la création (CART-R8). **Jeton de concurrence EF.** |

**Index unique** `UX_CartLines_CartId_ProductId` : un produit une seule fois par panier (CART-R5).

Les contraintes CHECK, UNIQUE et les jetons de concurrence **doublent** les règles du domaine : un bug applicatif provoque une erreur, jamais un état incohérent (constitution §2.4).

## 2. Domaine (`Shop.Domain/Carts`) — règles pures

`Cart` est l'agrégat : il est la seule porte d'entrée pour modifier ses lignes. Chaque méthode renvoie un `Result<CartChange>` (`Changed` ou `Unchanged`), ou une `DomainError`. **Une méthode qui échoue ne modifie rien** (les vérifications précèdent toute mutation).

| Méthode | Ordre des vérifications → effet |
|---|---|
| `AddItem(ProductSnapshot? product, int qty, DateTimeOffset now)` | qty ∉ [1, 999] → `validation_failed` · produit null ou inactif → `product_unavailable` · ligne existante et `quantité + qty > 999` → `quantity_limit_exceeded` · ligne existante → `Quantity += qty`, `line.Version++` · sinon nouvelle ligne (`Id` v7, `Version = 1`, nom et prix copiés) · puis `cart.Version++` |
| `ChangeQuantity(Guid lineId, int qty, long expectedVersion, bool productActive)` | qty ∉ [1, 999] → `validation_failed` · ligne absente → `cart_line_not_found` · `line.Version ≠ expectedVersion` → `cart_line_version_conflict` (+ `currentVersion`) · `qty == Quantity` → `Unchanged` · produit inactif et `qty > Quantity` → `product_unavailable` · sinon `Quantity = qty`, `line.Version++`, `cart.Version++` |
| `RemoveLine(Guid lineId, long expectedVersion)` | ligne absente → `cart_line_not_found` · version différente → `cart_line_version_conflict` · sinon suppression, `cart.Version++` |
| `Total` | `Σ line.UnitPrice × line.Quantity` (valeurs conservées, CART-R6.3), y compris les produits retirés |

- `ProductSnapshot(Id, Name, Price, IsActive)` est la vue du produit au moment de l'ajout, lue dans la transaction (§3).
- **Ce que le domaine ne fait jamais** : comparer la version globale du panier. Seule la version de la **ligne** visée est comparée (constitution §3.2 → CART-R9).
- `cart.Version++` n'a lieu qu'une fois par commande acceptée et effective, d'où le « +1 exact » (CART-R7).

## 3. Unité de travail transactionnelle (`Shop.Infrastructure/Carts/CartStore`)

Toutes les commandes passent par **un seul** chemin :

```csharp
Task<Result<CartMutationOutcome>> ICartStore.MutateAsync(
    string userId, bool createIfMissing,
    Func<Cart, CartMutationContext, Result<CartChange>> mutation, CancellationToken ct);
```

```
BEGIN TRANSACTION (READ COMMITTED, RCSI actif)
 1. SELECT … FROM Carts WITH (UPDLOCK, HOLDLOCK, ROWLOCK) WHERE UserId = @userId
      + lignes du panier (même requête EF : FromSql(...).Include(Lines))
    ├─ trouvé     → verrou U sur la ligne Carts : tout autre écrivain du même panier ATTEND ici
    ├─ absent + createIfMissing → INSERT Carts (Version 0)
    │      HOLDLOCK pose un verrou de plage sur la clé absente de UX_Carts_UserId :
    │      deux créations simultanées pour le même utilisateur sont sérialisées et la 2e voit le panier
    └─ absent sinon → cart_not_found (ROLLBACK)
 2. (ajout) SELECT Id, Name, Price, Status FROM Products WITH (REPEATABLEREAD) WHERE Id = @productId
      → verrou S conservé jusqu'au commit : une synchro ne peut pas retirer ce produit entre la vérification et le commit
 3. mutation(cart, ctx)   ← règles du domaine (§2)
    ├─ erreur      → ROLLBACK : rien n'est modifié, pas même la création implicite (CART-R7.3, CART-R15.3)
    ├─ Unchanged   → ROLLBACK (rien à écrire), réponse 200 avec l'état actuel
    └─ Changed     → SaveChanges : UPDATE Carts … WHERE Id = @id AND Version = @old (jeton de concurrence)
                                   + INSERT/UPDATE/DELETE CartLines … AND Version = @old
 4. COMMIT
```

**Pourquoi c'est correct**
- **Invariant** (constitution §3.1) : *toute* écriture d'un panier commence par l'étape 1. Une fois le verrou U obtenu, aucune autre transaction ne peut modifier ce panier, et la lecture des lignes (version validée la plus récente, RCSI) est donc l'état réel. Les décisions du domaine portent sur un état stable.
- **Ordre des verrous unique** (`Carts` → `Products`), et la synchro ne verrouille que `Products` : il n'y a **pas de cycle, donc pas d'interblocage** entre commandes de panier, ni entre panier et synchro.
- La transaction est **courte** : aucune entrée/sortie externe, quelques requêtes indexées. Le temps d'attente d'un second écrivain est de l'ordre de la milliseconde.
- **Nouvelles tentatives** : les commandes de panier ne sont **pas** rejouées automatiquement (`NonRetryingExecutionStrategy` pour cette unité de travail). Si le commit réussit mais que son accusé de réception est perdu (coupure réseau), un rejeu ajouterait **deux fois** la quantité. Une erreur transitoire renvoie donc `503`, et le client décide. Les clés d'idempotence (`Idempotency-Key`) sont l'évolution qui permettrait un rejeu sûr.

**Création explicite (`PUT /api/cart`)** : même étape 1 avec `createIfMissing`, sans mutation, puis COMMIT. La réponse est `201` si l'INSERT a eu lieu, sinon `200`. Filet de sécurité : une violation de `UX_Carts_UserId` (2601/2627), théoriquement impossible avec HOLDLOCK, est interceptée et l'opération relit le panier existant (`200`).

## 4. Lecture (`GET /api/cart`)

`ICartReader.GetViewAsync(userId)` : une **seule requête** SQL en projection (`AsNoTracking`), avec jointure `CartLines → Products` pour `productAvailable`. Sous RCSI, une instruction unique lit un instantané cohérent : la version du panier et ses lignes correspondent toujours au même commit. Elle n'est pas bloquée par un écrivain en cours.

Les réponses des commandes sont construites **après le commit** à partir de l'agrégat en mémoire (état exact validé) et de la disponibilité des produits concernés.

## 5. Contrat HTTP

Toutes les routes exigent `X-User-Id` (401 sinon) et ne visent que le panier de l'utilisateur courant (CART-R2) : il n'existe **aucun identifiant de panier** dans les routes. Une ligne d'un autre panier est introuvable dans la transaction de l'utilisateur courant, d'où `404`.

| Méthode | Route | Corps / paramètres | Succès | Erreurs |
|---|---|---|---|---|
| `PUT` | `/api/cart` | — | `201` créé / `200` existant | 401, 503 |
| `GET` | `/api/cart` | — | `200` | 401, 404 `cart_not_found` |
| `POST` | `/api/cart/lines` | `{ "productId": 12, "quantity": 2 }` | `200` (panier créé implicitement si besoin) | 400, 401, 422 `product_unavailable` / `quantity_limit_exceeded`, 503 |
| `PUT` | `/api/cart/lines/{lineId}` | `{ "quantity": 5, "expectedVersion": 1 }` | `200` | 400, 401, 404 `cart_not_found` / `cart_line_not_found`, 409 `cart_line_version_conflict`, 422, 503 |
| `DELETE` | `/api/cart/lines/{lineId}?expectedVersion=1` | — | `200` | 400, 401, 404, 409, 503 |

Réponse `200`/`201` (panier complet) :
```json
{
  "id": "0193…", "version": 3, "total": 60.00,
  "lines": [
    { "lineId": "0193…", "productId": 12, "productName": "A", "unitPrice": 10.00,
      "quantity": 5, "lineTotal": 50.00, "version": 2, "productAvailable": true }
  ]
}
```
Conflit `409` :
```json
{ "type": "https://errors.shop.local/cart_line_version_conflict", "title": "Conflit de version",
  "status": 409, "detail": "La ligne a été modifiée depuis votre dernière lecture (version actuelle : 2).",
  "code": "cart_line_version_conflict", "currentVersion": 2, "lineId": "0193…" }
```
Validation : `quantity` est `[Required, Range(1, 999)]` et `expectedVersion` est `[Required]`, avec des messages en français. Le domaine revérifie les bornes, car il ne fait pas confiance à l'appelant.

## 6. Démonstration des scénarios de concurrence

| Scénario | Déroulé | Résultat |
|---|---|---|
| **Alice, lignes A et B** (v4, deux instances) | T1 (A) prend le verrou U. T2 (B) attend à l'étape 1. T1 : A v1 → v2, panier v4 → v5, commit. T2 obtient le verrou, relit (panier v5, B toujours v1 = version attendue), modifie B, panier v5 → v6, commit. | **2 succès, panier v6** (CART-R9.1) |
| **Alice, même ligne A, même version** | T1 commit (A v2). T2 relit A en v2 ≠ version attendue 1. | **1 succès + 409** (CART-R9.2) |
| **10 ajouts simultanés de A** | Sérialisés par le verrou ; chacun relit la quantité validée et ajoute 2. | Quantité 20, version +10 (CART-R5.3) |
| **Ajouts au-delà de 999** (995 + 5×2) | Sérialisés : 997 ✓, 999 ✓, puis 3 refus `quantity_limit_exceeded` (rollback). | 999, version +2 (CART-R4.3) |
| **Premiers ajouts sans panier** (même produit ou produits différents, deux onglets) | Les deux cherchent le panier avec `UPDLOCK, HOLDLOCK` ; il est absent, donc verrou sur la clé `UserId`. Le 1er obtient le verrou, crée le panier, ajoute A et valide. Le 2e attendait **avant** de décider de créer ; il relit, **trouve** le panier et y ajoute B. | Un seul panier, 2 lignes, version 2 (CART-R1.3) |
| **Suppression ∥ modification de A** | Celui qui obtient le verrou en premier gagne. Si c'est la suppression, la modification ne trouve plus la ligne (404). Si c'est la modification, A passe en v2 et la suppression en v1 reçoit un 409. | 1 succès ; la ligne ne réapparaît jamais (CART-R10) |
| **Ajout ∥ synchro qui retire le produit** | L'ajout tient un verrou S sur le produit jusqu'au commit ; la synchro attend (ou l'ajout attend la synchro, puis voit le produit retiré). | Jamais d'ajout d'un produit déjà retiré (CART-R3) |

## 7. Limites assumées et évolutions
- **Écritures sérialisées par panier** : c'est voulu, avec une contention négligeable pour un panier personnel. Les paniers différents ne se bloquent pas. Les lectures ne sont jamais bloquées.
- **HOLDLOCK sur une clé absente** : le verrou de plage peut brièvement sérialiser la **première** création de paniers d'utilisateurs voisins dans l'index (aucun impact ensuite).
- **Commandes non rejouées automatiquement** : une erreur transitoire renvoie `503`. Les clés d'idempotence (`Idempotency-Key` sur `POST /api/cart/lines`) permettraient un rejeu sûr.
- **Prix sans devise** et sans remise ; le total est recalculé à chaque lecture (pas de colonne dénormalisée).
- Paniers abandonnés jamais purgés : une purge par ancienneté (`UpdatedAt`) est une évolution.

# Exigences — Panier personnel (`CART`)

> Format : exigences **EARS**. Critères d'acceptation exécutables : [`acceptance/`](acceptance/) (tags `@CART-R*`).
> CART-R1 à CART-R12 reprennent **une pour une** les règles métier du sujet ; CART-R13 à CART-R17 décrivent les opérations.
> L'utilisateur courant est celui identifié par l'en-tête `X-User-Id` (voir PLT-2).

## User stories

- **US-CART-1** — En tant qu'**utilisateur**, je veux disposer d'un panier personnel et y ajouter, modifier ou retirer des produits du catalogue, afin de préparer un achat.
- **US-CART-2** — En tant qu'**utilisateur** travaillant dans plusieurs onglets, je veux que mes modifications sur des articles différents réussissent toutes, et qu'une modification fondée sur une information périmée soit refusée explicitement plutôt qu'écrasée en silence.

## Règles métier

### CART-R1 — Un seul panier par utilisateur
1. Le système DOIT garantir qu'un utilisateur possède **au plus un** panier.
2. QUAND la création du panier est demandée plusieurs fois, y compris simultanément et sur des instances différentes, le système DOIT créer un seul panier et renvoyer ce même panier à chaque demande.
3. Cette garantie DOIT aussi tenir pour la création implicite lors d'un premier ajout (CART-R15.2), y compris quand plusieurs premiers ajouts arrivent simultanément.

### CART-R2 — Panier strictement personnel
1. Le système DOIT limiter toute consultation et toute modification au panier de l'utilisateur courant.
2. Aucune opération NE DOIT accepter d'identifiant de panier fourni par le client : le panier est toujours déterminé à partir de l'utilisateur courant.
3. SI un utilisateur cible une ligne qui n'appartient pas à son panier, ALORS le système DOIT répondre comme si la ligne n'existait pas (`404`), sans révéler l'existence de la ligne.

### CART-R3 — Seuls les produits existants et actifs peuvent être ajoutés
1. SI le produit demandé n'existe pas dans le catalogue local ou est retiré, ALORS le système DOIT refuser l'ajout (`422`, `product_unavailable`) sans modifier le panier.

### CART-R4 — Quantité entre 1 et 999
1. La quantité d'une ligne DOIT toujours être un entier compris entre 1 et 999.
2. SI une quantité demandée (ajout ou modification) est hors de [1, 999], ALORS le système DOIT refuser la demande (`400`) sans modifier le panier.
3. SI un ajout porterait la quantité cumulée d'une ligne au-delà de 999, ALORS le système DOIT refuser l'ajout en entier (`422`, `quantity_limit_exceeded`) sans modifier le panier (pas d'écrêtage à 999).

### CART-R5 — Un produit par ligne, ajouts cumulés
1. Un même produit NE DOIT figurer qu'une seule fois dans un panier.
2. QUAND un produit déjà présent est ajouté de nouveau, le système DOIT augmenter la quantité de la ligne existante de la quantité demandée.
3. QUAND plusieurs ajouts du même produit arrivent simultanément, la quantité finale DOIT être égale à la somme de toutes les quantités acceptées.

### CART-R6 — Nom et prix conservés au premier ajout
1. QUAND une ligne est créée, le système DOIT y copier le nom et le prix unitaire actuels du produit.
2. QUAND le nom ou le prix du produit change ensuite dans le catalogue, les lignes existantes NE DOIVENT PAS changer, y compris lors d'un ajout cumulé sur la ligne.
3. Le montant de chaque ligne et le montant total du panier DOIVENT être calculés à partir des valeurs conservées.

### CART-R7 — Version globale du panier
1. QUAND un panier est créé, sa version DOIT valoir 0.
2. QUAND une modification effective du contenu est acceptée (ajout, changement de quantité, suppression), la version DOIT augmenter d'**exactement 1**.
3. SI une demande est rejetée (validation, règle métier, conflit, erreur technique), ALORS la version NE DOIT PAS changer.
4. SI une modification ne change pas le contenu (remplacement d'une quantité par la même valeur), ALORS la version NE DOIT PAS changer (Décision D4).

### CART-R8 — Version propre de chaque ligne et conflit explicite
1. QUAND une ligne est créée, sa version DOIT valoir 1. QUAND elle est modifiée (ajout cumulé ou changement de quantité), sa version DOIT augmenter d'exactement 1.
2. Toute modification ou suppression d'une ligne DOIT fournir la version de ligne précédemment consultée (`expectedVersion`). SI elle est absente, ALORS le système DOIT répondre `400`.
3. SI la version fournie ne correspond pas à la version actuelle de la ligne, ALORS le système DOIT rejeter la demande avec un conflit explicite (`409`, `cart_line_version_conflict`) indiquant la version actuelle, sans rien modifier.

### CART-R9 — Pas de conflit injustifié entre lignes différentes
1. QUAND deux modifications simultanées portent sur des lignes **différentes** d'un même panier avec des versions de ligne à jour, les deux DOIVENT réussir, même si la version globale du panier a évolué entre-temps, et y compris si elles sont traitées par des instances différentes.
2. QUAND deux modifications simultanées portent sur la **même** ligne à partir de la même version de ligne, **au plus une** DOIT être acceptée ; l'autre DOIT recevoir un conflit.

### CART-R10 — Une ligne supprimée ne réapparaît pas
1. QUAND une modification ou une suppression vise une ligne qui a été supprimée, le système DOIT la rejeter (`404`, `cart_line_not_found`) sans recréer la ligne.
2. QUAND un produit est ajouté de nouveau après la suppression de sa ligne, le système DOIT créer une **nouvelle** ligne (nouvel identifiant, version 1, nom et prix actuels du catalogue). Une demande fondée sur l'ancienne ligne NE DOIT jamais s'appliquer à la nouvelle.

### CART-R11 — Produit retiré dans un panier existant
1. Une ligne dont le produit a été retiré du catalogue DOIT rester consultable avec son nom, son prix conservé et sa quantité, et être signalée comme indisponible (`productAvailable: false`). Elle reste comptée dans le montant total.
2. SI un utilisateur ajoute un produit retiré (nouvelle ligne ou ajout cumulé), ALORS le système DOIT refuser l'ajout (`422`, `product_unavailable`).
3. La ligne d'un produit retiré DOIT pouvoir être supprimée, et sa quantité DOIT pouvoir être **réduite**. SI une modification augmente sa quantité, ALORS le système DOIT la refuser (`422`, `product_unavailable`) (Décision D5).

### CART-R12 — Cohérence en cas d'échec
1. SI une opération échoue à n'importe quelle étape (erreur de base, arrêt de l'instance, exception), ALORS le panier, ses lignes et ses versions DOIVENT rester dans leur état antérieur à la demande.
2. Le système DOIT répondre par une erreur générique (`500` ou `503`) qui ne révèle aucun détail interne.

## Opérations

### CART-R13 — Créer ou récupérer son panier
1. QUAND l'utilisateur appelle `PUT /api/cart` sans panier existant, le système DOIT créer le panier et répondre `201` avec le panier.
2. QUAND le panier existe déjà, le système DOIT répondre `200` avec le panier existant, sans le modifier.

### CART-R14 — Consulter son panier
1. QUAND l'utilisateur appelle `GET /api/cart`, le système DOIT renvoyer : l'identifiant et la version du panier, et pour chaque ligne son identifiant, l'identifiant du produit, le nom et le prix unitaire conservés, la quantité, le montant de la ligne, la version de la ligne et l'indicateur `productAvailable` ; ainsi que le montant total.
2. SI l'utilisateur n'a pas de panier, ALORS le système DOIT répondre `404` (`cart_not_found`).

### CART-R15 — Ajouter un article
1. QUAND l'utilisateur appelle `POST /api/cart/lines` avec un produit et une quantité valides, le système DOIT ajouter la ligne ou cumuler la quantité (CART-R5) et renvoyer le panier mis à jour (`200`).
2. SI l'utilisateur n'a pas encore de panier, ALORS le système DOIT le créer implicitement (version 0), puis appliquer l'ajout dans la même opération et renvoyer le panier (`200`, version 1) (Décision D2).
3. SI l'ajout est refusé (validation, produit indisponible, erreur), ALORS le système NE DOIT PAS laisser de panier créé implicitement : la création et l'ajout réussissent ou échouent ensemble.

### CART-R16 — Modifier une quantité
1. QUAND l'utilisateur appelle `PUT /api/cart/lines/{lineId}` avec une quantité et la version attendue, le système DOIT **remplacer** la quantité de la ligne et renvoyer le panier mis à jour (`200`).

### CART-R17 — Supprimer un article
1. QUAND l'utilisateur appelle `DELETE /api/cart/lines/{lineId}?expectedVersion=N` avec la version à jour, le système DOIT retirer la ligne et renvoyer le panier mis à jour (`200`).

## Décisions (ambiguïtés du sujet tranchées, à valider)

| # | Question | Décision | Justification |
|---|---|---|---|
| D1 | Code HTTP d'une ligne supprimée visée par une demande obsolète ? | `404 cart_line_not_found`. | La ligne n'existe plus ; elle n'est jamais recréée. Une ligne d'un autre utilisateur produit la même réponse (pas de fuite d'information). |
| D2 | Ajouter un article sans panier existant ? | Le panier est **créé implicitement** dans la même transaction que l'ajout. Un ajout refusé ne laisse aucun panier vide. `PUT /api/cart` reste disponible pour une création explicite et idempotente. | Simplifie le parcours du client, qui n'a pas besoin d'un appel préalable. La création implicite repose sur le même mécanisme idempotent que `PUT /api/cart` (index unique `UserId`), donc CART-R1 tient aussi en cas de premiers ajouts simultanés. *Décision de l'utilisateur, qui remplace la proposition initiale (`404 cart_not_found`).* |
| D3 | Ajout dépassant 999 ? | Refus complet (`422`), pas d'écrêtage. | Le sujet indique « refusé sans altérer le panier ». |
| D4 | Remplacer une quantité par la même valeur ? | `200`, aucune version ne change (y compris celle de la ligne). La version attendue est tout de même vérifiée. | Le sujet ne fait évoluer la version que pour une modification **effective**. |
| D5 | Modifier la quantité d'un produit retiré ? | Réduction et suppression autorisées ; augmentation refusée (`422`). | « Il ne peut plus être ajouté » : augmenter la quantité revient à en ajouter. Réduire ou supprimer reste possible pour que le panier soit utilisable. |
| D6 | Versions initiales ? | Panier : 0. Ligne : 1 à sa création. | Version 0 imposée par le sujet pour le panier. Pour la ligne, sa création est sa première modification. |
| D7 | Comment le client transmet-il la version attendue ? | Corps JSON (`expectedVersion`) pour `PUT`, paramètre de requête pour `DELETE`. | Explicite et lisible. `If-Match`/ETag impliquerait un `412` alors que le sujet parle de conflit (`409`). |
| D8 | Comment sont identifiées les lignes dans l'API ? | Par un identifiant de ligne (`lineId`, GUID), pas par l'identifiant produit. | Une ligne recréée a un nouvel identifiant : une demande obsolète ne peut pas s'appliquer à la nouvelle ligne (CART-R10). |
| D9 | Montants ? | Décimal à 2 décimales ; montant de ligne = prix conservé × quantité ; total = somme des lignes, produits retirés inclus. | Le sujet impose le calcul à partir des valeurs conservées. Pas de devise (voir CAT D8). |

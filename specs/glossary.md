# Glossaire

| Terme | Définition |
|---|---|
| **Fournisseur** | Source externe du catalogue : DummyJSON (`https://dummyjson.com/products`). |
| **Identifiant fournisseur** (`externalId`) | Identifiant du produit chez le fournisseur. Sert à reconnaître un produit d'une synchronisation à l'autre. |
| **Identifiant produit** (`productId`) | Identifiant interne du produit dans notre base. C'est celui qu'utilisent l'API et les paniers. |
| **Catalogue local** | Copie des produits du fournisseur conservée dans notre base SQL Server. Toutes les lectures de produits s'y font. |
| **Produit actif** | Produit présent lors de la dernière synchronisation réussie. Il est listé et peut être ajouté à un panier. |
| **Produit retiré** (`Discontinued`) | Produit absent de la dernière synchronisation réussie. Il n'est plus listé ni ajoutable, mais reste visible dans les paniers qui le contiennent. Il redevient actif s'il réapparaît chez le fournisseur. |
| **Disponibilité indicative** | Stock et statut de disponibilité (`availabilityStatus`) annoncés par le fournisseur. Information de consultation seulement : aucune réservation de stock. |
| **Instantané** | Liste complète des produits du fournisseur obtenue en parcourant toutes ses pages. Il est **complet** quand le nombre de produits distincts reçus est égal au total annoncé par le fournisseur. |
| **Synchronisation** (ou **run**) | Exécution qui récupère un instantané et l'applique au catalogue local. Chaque run est historisé avec son déclencheur, son instance, son statut et ses compteurs. |
| **Statut d'un run** | `Running` (en cours), `Succeeded` (appliqué), `Failed` (échec, catalogue inchangé), `Skipped` (non exécuté car une autre synchronisation était en cours ou déjà faite pour le créneau). |
| **Déclencheur** | `Scheduled` (planificateur quotidien) ou `Manual` (appel de l'endpoint d'administration). |
| **Créneau quotidien** | Instant de référence de la synchronisation automatique, défini par une expression cron (par défaut 02:00 UTC). |
| **Instance** | Un processus de l'application. Plusieurs instances partagent la même base et peuvent traiter des requêtes simultanément. |
| **Utilisateur courant** | Utilisateur identifié par l'en-tête HTTP `X-User-Id` de la requête. |
| **Panier** | Panier personnel d'un utilisateur. Au plus un par utilisateur. |
| **Ligne** (`lineId`) | Entrée d'un panier pour un produit donné. Un produit apparaît au plus une fois par panier. Une ligne supprimée puis un produit réajouté donnent une **nouvelle** ligne avec un nouvel identifiant. |
| **Prix et nom conservés** | Nom et prix unitaire du produit copiés dans la ligne lors de sa création. Ils ne suivent plus les changements du catalogue. |
| **Version globale** (`version` du panier) | Compteur du panier, initialisé à 0, augmenté d'exactement 1 à chaque modification effective et acceptée de son contenu. |
| **Version de ligne** (`version` de la ligne) | Compteur propre à une ligne, initialisé à 1 à sa création, augmenté d'exactement 1 à chaque modification de cette ligne. |
| **Version attendue** (`expectedVersion`) | Version de ligne que le client a lue et sur laquelle il base sa modification ou sa suppression. |
| **Conflit** | Rejet d'une modification ou d'une suppression dont la version attendue ne correspond plus à la version actuelle de la ligne. |
| **Modification effective** | Modification qui change réellement le contenu du panier. Remplacer une quantité par la même valeur n'en est pas une. |
| **Curseur** | Jeton opaque renvoyé par la liste des produits pour obtenir la page suivante. |

# language: fr
Fonctionnalité: Effets de la synchronisation du catalogue sur les paniers existants
  Afin que le contenu d'un panier reste celui que l'utilisateur a choisi,
  une synchronisation réelle du catalogue modifie les produits sans jamais modifier les lignes de panier :
  le nom et le prix restent ceux du premier ajout, et un produit retiré reste visible dans le panier.

  Contexte:
    Étant donné que le fournisseur propose les produits suivants :
      | externalId | nom     | catégorie | prix  |
      | 1          | Mascara | beauty    | 9.99  |
      | 2          | Parfum  | fragrance | 49.99 |
      | 3          | Lampe   | furniture | 19.50 |
    Et qu'une synchronisation a déjà réussi
    Et que "alice" a ajouté 2 "Mascara" à son panier

  @CART-R6 @CAT-R4
  Scénario: Un changement de nom et de prix chez le fournisseur ne modifie pas la ligne du panier
    Étant donné que le fournisseur modifie le produit 1 avec le nom "Mascara Pro" et le prix 12.50
    Quand une synchronisation manuelle est déclenchée
    Alors la synchronisation réussit avec 3 produits récupérés, 0 insérés, 1 mis à jour, 0 retirés et 0 réactivés
    Et le produit 1 du catalogue local a le nom "Mascara Pro" et le prix 12.50
    Quand "alice" consulte son panier
    Alors le panier est en version 1 avec les lignes suivantes :
      | produit | prix unitaire | quantité | montant | version | disponible |
      | Mascara | 9.99          | 2        | 19.98   | 1       | oui        |
    Et le montant total du panier est 19.98

  @CART-R6 @CART-R5
  Scénario: Un réajout après la synchronisation cumule la quantité sans changer le nom ni le prix conservés
    Étant donné que le fournisseur modifie le produit 1 avec le nom "Mascara Pro" et le prix 12.50
    Et qu'une synchronisation manuelle a réussi
    Quand "alice" ajoute 1 "Mascara Pro" à son panier
    Alors le panier est en version 2 avec les lignes suivantes :
      | produit | prix unitaire | quantité | montant | version | disponible |
      | Mascara | 9.99          | 3        | 29.97   | 2       | oui        |
    Et le montant total du panier est 29.97

  @CART-R6 @CART-R10
  Scénario: Un produit réajouté après suppression de sa ligne prend le nom et le prix synchronisés
    Étant donné que le fournisseur modifie le produit 1 avec le nom "Mascara Pro" et le prix 12.50
    Et qu'une synchronisation manuelle a réussi
    Et que "alice" a supprimé la ligne "Mascara" avec la version 1
    Quand "alice" ajoute 1 "Mascara Pro" à son panier
    Alors le panier est en version 3 avec les lignes suivantes :
      | produit     | prix unitaire | quantité | montant | version | disponible |
      | Mascara Pro | 12.50         | 1        | 12.50   | 1       | oui        |

  @CART-R11 @CAT-R9
  Scénario: Un produit retiré par la synchronisation reste visible et compté dans le panier
    Étant donné que le fournisseur ne propose plus le produit 1
    Quand une synchronisation manuelle est déclenchée
    Alors la synchronisation réussit avec 2 produits récupérés, 0 insérés, 0 mis à jour, 1 retirés et 0 réactivés
    Quand "alice" consulte son panier
    Alors le panier est en version 1 avec les lignes suivantes :
      | produit | prix unitaire | quantité | montant | version | disponible |
      | Mascara | 9.99          | 2        | 19.98   | 1       | non        |
    Et le montant total du panier est 19.98
    Quand "alice" ajoute 1 "Mascara" à son panier
    Alors la réponse est une erreur 422 de code "product_unavailable"

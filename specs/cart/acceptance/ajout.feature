# language: fr
Fonctionnalité: Ajout d'articles au panier
  Afin de constituer son panier,
  l'utilisateur ajoute des produits actifs du catalogue ; un même produit n'occupe qu'une ligne
  dont le nom et le prix sont figés au premier ajout.

  Contexte:
    Étant donné les produits suivants dans le catalogue local :
      | nom    | prix  | statut       |
      | A      | 10.00 | Active       |
      | B      | 2.50  | Active       |
      | Retiré | 7.00  | Discontinued |
    Et que "alice" possède un panier vide

  @CART-R15 @CART-R6 @CART-R7 @CART-R8
  Scénario: Ajout d'un nouvel article
    Quand "alice" ajoute 2 "A" à son panier
    Alors la réponse a le statut 200
    Et le panier est en version 1 avec les lignes suivantes :
      | produit | prix unitaire | quantité | montant | version | disponible |
      | A       | 10.00         | 2        | 20.00   | 1       | oui        |
    Et le montant total du panier est 20.00

  @CART-R5 @CART-R7 @CART-R8
  Scénario: Réajouter un produit augmente la quantité de la ligne existante
    Étant donné que "alice" a ajouté 2 "A" à son panier
    Quand "alice" ajoute 3 "A" à son panier
    Alors la réponse a le statut 200
    Et le panier est en version 2 avec les lignes suivantes :
      | produit | prix unitaire | quantité | montant | version | disponible |
      | A       | 10.00         | 5        | 50.00   | 2       | oui        |

  @CART-R3
  Scénario: Ajout d'un produit inexistant
    Quand "alice" ajoute 1 produit d'identifiant inexistant à son panier
    Alors la réponse est une erreur 422 de code "product_unavailable"
    Et le panier d'"alice" est inchangé

  @CART-R3 @CART-R11
  Scénario: Ajout d'un produit retiré du catalogue
    Quand "alice" ajoute 1 "Retiré" à son panier
    Alors la réponse est une erreur 422 de code "product_unavailable"
    Et le panier d'"alice" est inchangé

  @CART-R4
  Plan du scénario: Une quantité hors bornes est refusée
    Quand "alice" ajoute <quantité> "A" à son panier
    Alors la réponse est une erreur 400 de code "validation_failed"
    Et le panier d'"alice" est inchangé

    Exemples:
      | quantité |
      | 0        |
      | -1       |
      | 1000     |

  @CART-R4
  Scénario: La quantité cumulée peut atteindre exactement 999
    Étant donné que "alice" a ajouté 998 "A" à son panier
    Quand "alice" ajoute 1 "A" à son panier
    Alors la réponse a le statut 200
    Et la ligne "A" du panier d'"alice" a la quantité 999

  @CART-R4 @CART-R7
  Scénario: Un ajout qui dépasserait 999 est refusé sans altérer le panier
    Étant donné que "alice" a ajouté 998 "A" à son panier
    Quand "alice" ajoute 2 "A" à son panier
    Alors la réponse est une erreur 422 de code "quantity_limit_exceeded"
    Et le panier d'"alice" est inchangé

  @CART-R6
  Scénario: Le nom et le prix d'une ligne restent ceux du premier ajout
    Étant donné que "alice" a ajouté 2 "A" à son panier
    Et que le produit "A" est renommé "A nouvelle formule" et passe au prix 15.00 dans le catalogue
    Quand "alice" ajoute 1 "A" à son panier
    Alors le panier est en version 2 avec les lignes suivantes :
      | produit | prix unitaire | quantité | montant | version | disponible |
      | A       | 10.00         | 3        | 30.00   | 2       | oui        |
    Et le montant total du panier est 30.00

  @CART-R15 @CART-R1
  Scénario: Le premier ajout crée le panier implicitement
    Étant donné que "bob" n'a pas de panier
    Quand "bob" ajoute 1 "A" à son panier
    Alors la réponse a le statut 200
    Et le panier est en version 1 avec les lignes suivantes :
      | produit | prix unitaire | quantité | montant | version | disponible |
      | A       | 10.00         | 1        | 10.00   | 1       | oui        |
    Et "bob" possède exactement un panier

  @CART-R15
  Scénario: Un premier ajout refusé ne crée pas de panier
    Étant donné que "bob" n'a pas de panier
    Quand "bob" ajoute 1 "Retiré" à son panier
    Alors la réponse est une erreur 422 de code "product_unavailable"
    Et "bob" n'a pas de panier

  @CAT-R7
  Scénario: Le panier reste utilisable quand le fournisseur est indisponible
    Étant donné que le fournisseur est indisponible
    Quand "alice" ajoute 2 "A" à son panier
    Alors la réponse a le statut 200

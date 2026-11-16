# language: fr
Fonctionnalité: Modification et suppression des lignes du panier
  Afin d'ajuster son panier sans écraser une modification faite ailleurs,
  l'utilisateur indique la version de la ligne qu'il a consultée ; une version périmée provoque un conflit explicite.

  Contexte:
    Étant donné les produits suivants dans le catalogue local :
      | nom | prix  | statut |
      | A   | 10.00 | Active |
      | B   | 2.50  | Active |
    Et que "alice" possède un panier contenant :
      | produit | quantité |
      | A       | 2        |
      | B       | 4        |

  @CART-R16 @CART-R7 @CART-R8
  Scénario: La nouvelle quantité remplace l'ancienne
    Quand "alice" remplace la quantité de la ligne "A" par 5 avec la version 1
    Alors la réponse a le statut 200
    Et le panier est en version 3 avec les lignes suivantes :
      | produit | prix unitaire | quantité | montant | version | disponible |
      | A       | 10.00         | 5        | 50.00   | 2       | oui        |
      | B       | 2.50          | 4        | 10.00   | 1       | oui        |

  @CART-R7
  Scénario: Remplacer une quantité par la même valeur ne change aucune version
    Quand "alice" remplace la quantité de la ligne "A" par 2 avec la version 1
    Alors la réponse a le statut 200
    Et le panier d'"alice" est inchangé

  @CART-R8 @CART-R7
  Scénario: Une modification fondée sur une version périmée est rejetée
    Étant donné que "alice" a remplacé la quantité de la ligne "A" par 3 avec la version 1
    Quand "alice" remplace la quantité de la ligne "A" par 7 avec la version 1
    Alors la réponse est une erreur 409 de code "cart_line_version_conflict" indiquant la version actuelle 2
    Et le panier d'"alice" est inchangé

  @CART-R8
  Scénario: La version attendue est obligatoire
    Quand "alice" remplace la quantité de la ligne "A" par 5 sans indiquer de version
    Alors la réponse est une erreur 400 de code "validation_failed"
    Quand "alice" supprime la ligne "A" sans indiquer de version
    Alors la réponse est une erreur 400 de code "validation_failed"
    Et le panier d'"alice" est inchangé

  @CART-R4
  Plan du scénario: Une nouvelle quantité hors bornes est refusée
    Quand "alice" remplace la quantité de la ligne "A" par <quantité> avec la version 1
    Alors la réponse est une erreur 400 de code "validation_failed"
    Et le panier d'"alice" est inchangé

    Exemples:
      | quantité |
      | 0        |
      | 1000     |

  @CART-R17 @CART-R7
  Scénario: Suppression d'une ligne
    Quand "alice" supprime la ligne "A" avec la version 1
    Alors la réponse a le statut 200
    Et le panier est en version 3 avec les lignes suivantes :
      | produit | prix unitaire | quantité | montant | version | disponible |
      | B       | 2.50          | 4        | 10.00   | 1       | oui        |
    Et le montant total du panier est 10.00

  @CART-R8 @CART-R17
  Scénario: Une suppression fondée sur une version périmée est rejetée
    Étant donné que "alice" a remplacé la quantité de la ligne "A" par 3 avec la version 1
    Quand "alice" supprime la ligne "A" avec la version 1
    Alors la réponse est une erreur 409 de code "cart_line_version_conflict" indiquant la version actuelle 2
    Et le panier d'"alice" est inchangé

  @CART-R10
  Scénario: Une modification obsolète ne fait pas réapparaître une ligne supprimée
    Étant donné qu'"alice" a mémorisé la ligne "A" en version 1
    Et que "alice" a supprimé la ligne "A" avec la version 1
    Quand "alice" remplace la quantité de la ligne mémorisée par 5 avec la version 1
    Alors la réponse est une erreur 404 de code "cart_line_not_found"
    Et le panier d'"alice" ne contient pas de ligne "A"

  @CART-R10 @CART-R6
  Scénario: Un produit réajouté après suppression forme une nouvelle ligne insensible aux demandes obsolètes
    Étant donné qu'"alice" a mémorisé la ligne "A" en version 1
    Et que "alice" a supprimé la ligne "A" avec la version 1
    Et que le produit "A" passe au prix 12.00 dans le catalogue
    Quand "alice" ajoute 1 "A" à son panier
    Alors la ligne "A" a un nouvel identifiant, la version 1, la quantité 1 et le prix unitaire 12.00
    Quand "alice" remplace la quantité de la ligne mémorisée par 5 avec la version 1
    Alors la réponse est une erreur 404 de code "cart_line_not_found"
    Et la ligne "A" du panier d'"alice" a la quantité 1

  @CART-R11 @CAT-R9
  Scénario: Une ligne dont le produit a été retiré reste consultable et comptée
    Étant donné que le produit "A" est retiré du catalogue
    Quand "alice" consulte son panier
    Alors le panier est en version 2 avec les lignes suivantes :
      | produit | prix unitaire | quantité | montant | version | disponible |
      | A       | 10.00         | 2        | 20.00   | 1       | non        |
      | B       | 2.50          | 4        | 10.00   | 1       | oui        |
    Et le montant total du panier est 30.00

  @CART-R11
  Scénario: La quantité d'un produit retiré peut être réduite mais pas augmentée
    Étant donné que "alice" a remplacé la quantité de la ligne "A" par 5 avec la version 1
    Et que le produit "A" est retiré du catalogue
    Quand "alice" remplace la quantité de la ligne "A" par 6 avec la version 2
    Alors la réponse est une erreur 422 de code "product_unavailable"
    Et le panier d'"alice" est inchangé
    Quand "alice" ajoute 1 "A" à son panier
    Alors la réponse est une erreur 422 de code "product_unavailable"
    Quand "alice" remplace la quantité de la ligne "A" par 1 avec la version 2
    Alors la réponse a le statut 200
    Et la ligne "A" du panier d'"alice" a la quantité 1

  @CART-R11
  Scénario: La ligne d'un produit retiré peut être supprimée
    Étant donné que le produit "A" est retiré du catalogue
    Quand "alice" supprime la ligne "A" avec la version 1
    Alors la réponse a le statut 200
    Et le panier d'"alice" ne contient pas de ligne "A"

  @CART-R12 @CART-R7
  Plan du scénario: Une panne de la base pendant une opération laisse le panier intact
    Étant donné que la base de données échoue lors du prochain enregistrement du panier
    Quand "alice" <opération>
    Alors la réponse est une erreur 503 de code "service_unavailable"
    Et le panier d'"alice" est inchangé

    Exemples:
      | opération                                                  |
      | ajoute 1 "A" à son panier                                  |
      | remplace la quantité de la ligne "B" par 9 avec la version 1 |
      | supprime la ligne "A" avec la version 1                    |

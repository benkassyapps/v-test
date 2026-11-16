# language: fr
Fonctionnalité: Création et consultation du panier personnel
  Afin de préparer un achat,
  chaque utilisateur dispose d'au plus un panier, qu'il est le seul à pouvoir consulter et modifier.

  Contexte:
    Étant donné les produits suivants dans le catalogue local :
      | nom | prix  | statut |
      | A   | 10.00 | Active |
      | B   | 2.50  | Active |
    Et que le service fonctionne avec 2 instances

  @CART-R13 @CART-R7
  Scénario: Création du panier
    Quand "alice" demande la création de son panier
    Alors la réponse a le statut 201
    Et le panier renvoyé est vide, en version 0, avec un montant total de 0.00

  @CART-R13
  Scénario: Une nouvelle demande de création renvoie le panier existant sans le modifier
    Étant donné que "alice" possède un panier contenant :
      | produit | quantité |
      | A       | 2        |
    Quand "alice" demande la création de son panier
    Alors la réponse a le statut 200
    Et le panier renvoyé est le panier existant d'"alice", inchangé

  @CART-R1 @PLT-1
  Scénario: Des demandes de création simultanées sur plusieurs instances ne créent qu'un panier
    Quand "alice" demande 20 fois simultanément la création de son panier, en alternant les instances 1 et 2
    Alors toutes les réponses ont le statut 200 ou 201
    Et exactement une réponse a le statut 201
    Et toutes les réponses renvoient le même identifiant de panier
    Et "alice" possède exactement un panier

  @CART-R14 @CART-R6
  Scénario: Consultation du panier avec ses lignes, ses versions et son montant total
    Étant donné que "alice" possède un panier contenant :
      | produit | quantité |
      | A       | 2        |
      | B       | 4        |
    Quand "alice" consulte son panier
    Alors la réponse a le statut 200
    Et le panier est en version 2 avec les lignes suivantes :
      | produit | prix unitaire | quantité | montant | version | disponible |
      | A       | 10.00         | 2        | 20.00   | 1       | oui        |
      | B       | 2.50          | 4        | 10.00   | 1       | oui        |
    Et le montant total du panier est 30.00

  @CART-R14
  Scénario: Consultation sans panier
    Quand "alice" consulte son panier
    Alors la réponse est une erreur 404 de code "cart_not_found"

  @CART-R2
  Scénario: Un utilisateur ne voit pas le panier d'un autre
    Étant donné que "alice" possède un panier contenant :
      | produit | quantité |
      | A       | 2        |
    Quand "bob" consulte son panier
    Alors la réponse est une erreur 404 de code "cart_not_found"
    Quand "bob" demande la création de son panier
    Alors la réponse a le statut 201
    Et le panier renvoyé est vide et différent de celui d'"alice"

  @CART-R2
  Scénario: Un utilisateur ne peut pas modifier ni supprimer une ligne du panier d'un autre
    Étant donné que "alice" possède un panier contenant :
      | produit | quantité |
      | A       | 2        |
    Et que "bob" possède un panier vide
    Quand "bob" remplace la quantité de la ligne "A" d'"alice" par 5 avec la version 1
    Alors la réponse est une erreur 404 de code "cart_line_not_found"
    Quand "bob" supprime la ligne "A" d'"alice" avec la version 1
    Alors la réponse est une erreur 404 de code "cart_line_not_found"
    Et le panier d'"alice" est inchangé

# language: fr
Fonctionnalité: Clé d'idempotence des commandes du panier
  Afin qu'une commande déjà validée ne soit jamais appliquée deux fois quand le client réessaie
  (réponse perdue, coupure, délai dépassé), le client peut accompagner chaque commande d'une clé d'idempotence :
  une même clé, pour une même requête, est exécutée au plus une fois et rejoue la réponse d'origine.

  Contexte:
    Étant donné les produits suivants dans le catalogue local :
      | nom | prix  | statut |
      | A   | 10.00 | Active |
      | B   | 2.50  | Active |
    Et que le service fonctionne avec 2 instances

  @CART-R18
  Scénario: La confirmation d'un ajout est perdue ; la nouvelle tentative avec la même clé ne double pas la quantité
    Étant donné que la confirmation du prochain enregistrement du panier sera perdue
    Quand "alice" ajoute 2 "A" à son panier avec la clé "ajout-1"
    Alors la réponse est une erreur 503 de code "service_unavailable"
    Quand "alice" ajoute 2 "A" à son panier avec la clé "ajout-1"
    Alors la réponse a le statut 200
    Et la réponse est un rejeu de la réponse d'origine
    Et le panier est en version 1 avec les quantités suivantes :
      | produit | quantité |
      | A       | 2        |

  @CART-R18
  Scénario: Sans clé d'idempotence, la même situation double la quantité
    Étant donné que la confirmation du prochain enregistrement du panier sera perdue
    Quand "alice" ajoute 2 "A" à son panier
    Alors la réponse est une erreur 503 de code "service_unavailable"
    Quand "alice" ajoute 2 "A" à son panier
    Alors le panier est en version 2 avec les quantités suivantes :
      | produit | quantité |
      | A       | 4        |

  @CART-R18
  Scénario: Une demande renvoyée avec la même clé rejoue la réponse d'origine sans rien modifier
    Quand "alice" ajoute 2 "A" à son panier avec la clé "ajout-2"
    Alors la réponse a le statut 200
    Et la réponse n'est pas un rejeu
    Quand "alice" ajoute 2 "A" à son panier avec la clé "ajout-2"
    Alors la réponse a le statut 200
    Et la réponse est un rejeu de la réponse d'origine
    Et le panier est en version 1 avec les quantités suivantes :
      | produit | quantité |
      | A       | 2        |

  @CART-R18
  Scénario: La modification et la suppression d'une ligne acceptent aussi une clé d'idempotence
    Étant donné que "alice" a ajouté 2 "A" à son panier
    Quand "alice" remplace la quantité de la ligne "A" par 5 avec la version 1 et la clé "modif-1"
    Et "alice" remplace la quantité de la ligne "A" par 5 avec la version 1 et la clé "modif-1"
    Alors la réponse a le statut 200
    Et la réponse est un rejeu de la réponse d'origine
    Quand "alice" supprime la ligne "A" avec la version 2 et la clé "suppr-1"
    Et "alice" supprime la ligne "A" avec la version 2 et la clé "suppr-1"
    Alors la réponse a le statut 200
    Et la réponse est un rejeu de la réponse d'origine
    Et le panier d'"alice" ne contient pas de ligne "A"

  @CART-R18
  Scénario: Une clé réutilisée pour une requête différente est refusée
    Étant donné que "alice" a ajouté 2 "A" à son panier avec la clé "ajout-3"
    Quand "alice" ajoute 5 "B" à son panier avec la clé "ajout-3"
    Alors la réponse est une erreur 422 de code "idempotency_key_reused"
    Et le panier d'"alice" est inchangé

  @CART-R18 @CART-R4
  Scénario: Un refus métier est rejoué à l'identique, même si le panier a changé entre-temps
    Étant donné que "alice" a ajouté 998 "A" à son panier
    Quand "alice" ajoute 2 "A" à son panier avec la clé "ajout-4"
    Alors la réponse est une erreur 422 de code "quantity_limit_exceeded"
    Étant donné que "alice" a remplacé la quantité de la ligne "A" par 1 avec la version 1
    Quand "alice" ajoute 2 "A" à son panier avec la clé "ajout-4"
    Alors la réponse est une erreur 422 de code "quantity_limit_exceeded"
    Et la réponse est un rejeu de la réponse d'origine
    Et la ligne "A" du panier d'"alice" a la quantité 1

  @CART-R18 @PLT-1
  Scénario: Des envois simultanés de la même clé sur deux instances n'appliquent la commande qu'une fois
    Quand "alice" envoie simultanément 6 fois l'ajout de 2 "A" avec la clé "ajout-5", en alternant les instances 1 et 2
    Alors les 6 demandes réussissent avec le même panier en réponse
    Et exactement 1 demande n'est pas un rejeu
    Et le panier est en version 1 avec les quantités suivantes :
      | produit | quantité |
      | A       | 2        |

  @CART-R18
  Scénario: Les clés sont propres à chaque utilisateur
    Quand "alice" ajoute 1 "A" à son panier avec la clé "commune"
    Et "bob" ajoute 3 "B" à son panier avec la clé "commune"
    Alors la réponse a le statut 200
    Et la réponse n'est pas un rejeu
    Et la ligne "B" du panier de "bob" a la quantité 3

  @CART-R18
  Scénario: Une clé expirée peut être réutilisée
    Étant donné que "alice" a ajouté 2 "A" à son panier avec la clé "ajout-6"
    Et qu'il s'est écoulé 25 heures
    Quand "alice" ajoute 2 "A" à son panier avec la clé "ajout-6"
    Alors la réponse a le statut 200
    Et la réponse n'est pas un rejeu
    Et la ligne "A" du panier d'"alice" a la quantité 4

  @CART-R18
  Scénario: Une clé d'idempotence au format invalide est refusée
    Quand "alice" ajoute 1 "A" à son panier avec la clé "clé invalide !"
    Alors la réponse est une erreur 400 de code "validation_failed"
    Et "alice" n'a pas de panier

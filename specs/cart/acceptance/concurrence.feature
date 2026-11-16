# language: fr
Fonctionnalité: Modifications simultanées du panier sur plusieurs instances
  Afin qu'aucune modification ne soit perdue ni écrasée en silence,
  les demandes simultanées sur un même panier sont traitées de façon cohérente,
  même lorsqu'elles arrivent sur des instances différentes.

  Contexte:
    Étant donné les produits suivants dans le catalogue local :
      | nom | prix  | statut |
      | A   | 10.00 | Active |
      | B   | 2.50  | Active |
    Et que le service fonctionne avec 2 instances

  @CART-R9 @PLT-1
  Scénario: Exemple d'Alice - deux onglets modifient des articles différents
    Étant donné qu'"alice" possède un panier en version 4 contenant les articles "A" et "B"
    Et qu'"alice" consulte son panier
    Quand "alice" envoie simultanément, à partir de cette lecture :
      | instance | ligne | nouvelle quantité |
      | 1        | A     | 3                 |
      | 2        | B     | 7                 |
    Alors les 2 demandes réussissent
    Et le panier est en version 6 avec les quantités suivantes :
      | produit | quantité |
      | A       | 3        |
      | B       | 7        |

  @CART-R9 @CART-R8 @PLT-1
  Scénario: Exemple d'Alice - deux onglets modifient le même article à partir de la même version
    Étant donné qu'"alice" possède un panier en version 4 contenant les articles "A" et "B"
    Et qu'"alice" consulte son panier
    Quand "alice" envoie simultanément, à partir de cette lecture :
      | instance | ligne | nouvelle quantité |
      | 1        | A     | 3                 |
      | 2        | A     | 8                 |
    Alors exactement 1 demande réussit et 1 demande est rejetée avec le code "cart_line_version_conflict"
    Et le panier est en version 5
    Et la quantité de la ligne "A" est celle de la demande acceptée

  @CART-R5 @CART-R7 @PLT-1
  Scénario: Des ajouts simultanés du même produit conservent toutes les quantités
    Étant donné que "alice" possède un panier vide
    Quand "alice" ajoute simultanément 10 fois 2 "A" à son panier, en alternant les instances 1 et 2
    Alors les 10 demandes réussissent
    Et le panier est en version 10 avec les quantités suivantes :
      | produit | quantité |
      | A       | 20       |

  @CART-R1 @CART-R5 @CART-R15 @PLT-1
  Scénario: Des premiers ajouts simultanés sans panier ne créent qu'un panier et conservent toutes les quantités
    Étant donné que "alice" n'a pas de panier
    Quand "alice" ajoute simultanément 10 fois 2 "A" à son panier, en alternant les instances 1 et 2
    Alors les 10 demandes réussissent
    Et "alice" possède exactement un panier
    Et le panier est en version 10 avec les quantités suivantes :
      | produit | quantité |
      | A       | 20       |

  @CART-R1 @CART-R15 @PLT-1
  Scénario: Deux onglets ajoutent simultanément des produits différents alors qu'aucun panier n'existe
    Étant donné que "alice" n'a pas de panier
    Quand "alice" envoie simultanément l'ajout de 1 "A" via l'instance 1 et l'ajout de 2 "B" via l'instance 2
    Alors les 2 demandes réussissent
    Et "alice" possède exactement un panier
    Et le panier est en version 2 avec les quantités suivantes :
      | produit | quantité |
      | A       | 1        |
      | B       | 2        |

  @CART-R1 @CART-R15
  Scénario: Le deuxième onglet ajoute un produit au panier créé par le premier
    Étant donné que "alice" n'a pas de panier
    Et que "alice" a ajouté 1 "A" à son panier via l'instance 1
    Quand "alice" ajoute 2 "B" à son panier via l'instance 2
    Alors la réponse a le statut 200
    Et "alice" possède exactement un panier
    Et le panier est en version 2 avec les quantités suivantes :
      | produit | quantité |
      | A       | 1        |
      | B       | 2        |

  @CART-R5 @CART-R4 @PLT-1
  Scénario: Des ajouts simultanés qui dépasseraient 999 ne sont acceptés que dans la limite
    Étant donné que "alice" possède un panier contenant :
      | produit | quantité |
      | A       | 995      |
    Quand "alice" ajoute simultanément 5 fois 2 "A" à son panier, en alternant les instances 1 et 2
    Alors exactement 2 demandes réussissent et 3 demandes sont rejetées avec le code "quantity_limit_exceeded"
    Et le panier est en version 3 avec les quantités suivantes :
      | produit | quantité |
      | A       | 999      |

  @CART-R10 @PLT-1
  Scénario: Une suppression et une modification simultanées de la même ligne
    Étant donné que "alice" possède un panier contenant :
      | produit | quantité |
      | A       | 2        |
    Et qu'"alice" consulte son panier
    Quand "alice" envoie simultanément, à partir de cette lecture, la suppression de la ligne "A" via l'instance 1 et la quantité 5 pour la ligne "A" via l'instance 2
    Alors exactement 1 demande réussit
    Et l'autre demande est rejetée avec le code "cart_line_version_conflict" ou "cart_line_not_found"
    Et l'état final du panier correspond à la seule demande acceptée

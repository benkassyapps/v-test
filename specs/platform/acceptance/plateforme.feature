# language: fr
Fonctionnalité: Exigences transverses de la plateforme
  Afin que le service soit exploitable et que ses réponses soient prévisibles,
  il identifie l'utilisateur courant, répond aux erreurs selon un contrat unique et expose son état.

  Contexte:
    Étant donné que le service fonctionne avec 2 instances

  @PLT-2
  Plan du scénario: Les opérations de panier exigent un identifiant utilisateur valide
    Quand une requête "<requête>" est envoyée avec l'en-tête X-User-Id "<identifiant>"
    Alors la réponse est une erreur 401 de code "user_not_identified"

    Exemples:
      | requête          | identifiant                                                        |
      | GET /api/cart    | <absent>                                                           |
      | PUT /api/cart    |                                                                    |
      | PUT /api/cart    | alice dupont                                                       |
      | GET /api/cart    | a234567890123456789012345678901234567890123456789012345678901234X |

  @PLT-2
  Scénario: Un identifiant utilisateur au bon format est accepté
    Quand une requête "PUT /api/cart" est envoyée avec l'en-tête X-User-Id "alice.dupont-01@exemple"
    Alors la réponse a le statut 201

  @PLT-3
  Scénario: Les erreurs respectent le contrat ProblemDetails sans divulguer de détail interne
    Quand "alice" consulte son panier
    Alors la réponse est une erreur 404 de code "cart_not_found"
    Et la réponse est au format "application/problem+json" avec un type, un titre, un statut et un détail en français
    Et la réponse ne contient ni pile d'appels ni requête SQL

  @PLT-3
  Scénario: Les erreurs de validation détaillent les champs en cause
    Étant donné que "alice" possède un panier vide
    Quand "alice" ajoute 0 produit d'identifiant inexistant à son panier
    Alors la réponse est une erreur 400 de code "validation_failed"
    Et la réponse détaille l'erreur du champ "quantity"

  @PLT-1
  Scénario: Chaque réponse indique l'instance qui l'a traitée
    Quand le client liste les produits via l'instance 1 puis via l'instance 2
    Alors les deux réponses portent un en-tête X-Instance avec des valeurs différentes

  @PLT-6
  Scénario: L'endpoint de santé vérifie l'accès à la base
    Quand le client consulte l'état de santé du service
    Alors la réponse a le statut 200
    Et l'état indiqué est "Healthy"

  @PLT-7
  Scénario: La description OpenAPI est publiée
    Quand le client récupère la description OpenAPI du service
    Alors la réponse a le statut 200
    Et la description contient les chemins "/api/products", "/api/cart", "/api/cart/lines" et "/api/admin/catalog/sync"

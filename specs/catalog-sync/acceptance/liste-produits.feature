# language: fr
Fonctionnalité: Liste paginée des produits du catalogue local
  Afin de présenter les produits disponibles,
  le client de l'API parcourt les produits actifs page par page à l'aide d'un curseur,
  sans dépendre de la disponibilité du fournisseur.

  Contexte:
    Étant donné les produits suivants dans le catalogue local :
      | nom | prix  | statut       |
      | P1  | 10.00 | Active       |
      | P2  | 20.00 | Active       |
      | P3  | 30.00 | Discontinued |
      | P4  | 40.00 | Active       |
      | P5  | 50.00 | Active       |
      | P6  | 60.00 | Active       |

  @CAT-R6
  Scénario: Parcours complet des produits actifs page par page
    Quand le client liste les produits avec une limite de 2
    Alors la page contient les produits "P1, P2" et indique qu'il en reste d'autres
    Quand le client demande la page suivante
    Alors la page contient les produits "P4, P5" et indique qu'il en reste d'autres
    Quand le client demande la page suivante
    Alors la page contient les produits "P6" et indique qu'il n'en reste pas d'autres

  @CAT-R6 @CAT-R9
  Scénario: Les produits retirés ne sont pas listés
    Quand le client liste les produits avec une limite de 100
    Alors la page contient les produits "P1, P2, P4, P5, P6" et indique qu'il n'en reste pas d'autres

  @CAT-R6
  Scénario: La limite par défaut est de 20 produits
    Étant donné que le catalogue local contient 25 produits actifs supplémentaires
    Quand le client liste les produits sans préciser de limite
    Alors la page contient 20 produits et indique qu'il en reste d'autres

  @CAT-R6
  Scénario: Le parcours reste exact quand le catalogue change entre deux pages
    Quand le client liste les produits avec une limite de 2
    Alors la page contient les produits "P1, P2" et indique qu'il en reste d'autres
    Étant donné que le produit "P1" est retiré du catalogue
    Et que le produit "P7" au prix 70.00 est ajouté au catalogue
    Quand le client demande la page suivante
    Alors la page contient les produits "P4, P5" et indique qu'il en reste d'autres
    Quand le client demande la page suivante
    Alors la page contient les produits "P6, P7" et indique qu'il n'en reste pas d'autres
    Et aucun produit n'a été renvoyé deux fois au cours du parcours

  @CAT-R6
  Plan du scénario: Les paramètres de pagination invalides sont refusés
    Quand le client liste les produits avec les paramètres "<paramètres>"
    Alors la réponse est une erreur 400 de code "validation_failed"

    Exemples:
      | paramètres             |
      | limit=0                |
      | limit=101              |
      | limit=abc              |
      | cursor=pas-un-curseur  |

  @CAT-R7
  Scénario: Les produits restent consultables quand le fournisseur est indisponible
    Étant donné que le fournisseur est indisponible
    Quand le client liste les produits avec une limite de 100
    Alors la page contient les produits "P1, P2, P4, P5, P6" et indique qu'il n'en reste pas d'autres

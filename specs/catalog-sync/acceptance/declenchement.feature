# language: fr
Fonctionnalité: Déclenchement de la synchronisation, planification et exclusivité
  Afin que le catalogue soit synchronisé chaque jour une seule fois, même avec plusieurs instances,
  et que l'exploitant puisse vérifier le fonctionnement de la synchronisation,
  le service planifie la synchronisation, permet de la déclencher manuellement et garantit qu'une seule s'exécute à la fois.

  Contexte:
    Étant donné que le fournisseur propose les produits suivants :
      | externalId | nom     | catégorie | prix |
      | 1          | Mascara | beauty    | 9.99 |
      | 2          | Parfum  | fragrance | 49.99 |
    Et que le service fonctionne avec 2 instances

  # --- Déclenchement manuel ---

  @CAT-R3
  Scénario: Le déclenchement manuel renvoie le résumé du run
    Quand l'exploitant déclenche une synchronisation sur l'instance 1 avec la clé d'administration
    Alors la réponse a le statut 200
    Et le résumé indique le statut "Succeeded", le déclencheur "Manual", l'instance 1, les dates de début et de fin et 2 produits récupérés

  @CAT-R3
  Plan du scénario: Le déclenchement manuel est refusé sans clé d'administration valide
    Quand l'exploitant déclenche une synchronisation sur l'instance 1 avec la clé "<clé>"
    Alors la réponse est une erreur 403 de code "admin_key_invalid"
    Et aucune synchronisation n'a été exécutée

    Exemples:
      | clé            |
      |                |
      | mauvaise-cle   |

  @CAT-R3
  Scénario: Le déclenchement manuel s'exécute même si la synchronisation du jour a déjà réussi
    Étant donné qu'une synchronisation planifiée a réussi aujourd'hui
    Quand l'exploitant déclenche une synchronisation sur l'instance 1 avec la clé d'administration
    Alors la réponse a le statut 200
    Et le résumé indique le statut "Succeeded" et le déclencheur "Manual"

  @CAT-R3 @CAT-R7
  Scénario: Le déclenchement manuel signale une panne du fournisseur
    Étant donné que le fournisseur est indisponible
    Quand l'exploitant déclenche une synchronisation sur l'instance 1 avec la clé d'administration
    Alors la réponse est une erreur 502 de code "provider_unavailable"
    Et l'historique contient un run "Failed" dont la cause est renseignée

  @CAT-R11 @CAT-R3
  Scénario: Le déclenchement manuel signale un blocage par le garde-fou
    Étant donné qu'une synchronisation a déjà réussi
    Et que le fournisseur ne propose aucun produit
    Quand l'exploitant déclenche une synchronisation sur l'instance 1 avec la clé d'administration
    Alors la réponse est une erreur 422 de code "sync_guard_triggered"
    Et le détail de l'erreur indique 2 produits concernés et la possibilité de forcer

  @CAT-R11 @CAT-R2
  Scénario: Le planificateur ne force jamais un retrait massif
    Étant donné que la dernière synchronisation réussie date du "2026-11-15T02:05:00Z"
    Et que le fournisseur ne propose aucun produit
    Et qu'il est "2026-11-16T02:10:00Z"
    Quand le planificateur de l'instance 1 effectue sa vérification
    Alors l'historique contient un run "Scheduled" en échec bloqué par le garde-fou
    Et le catalogue local est identique à son état avant la synchronisation

  @CAT-R2
  Plan du scénario: Le planificateur espace progressivement les tentatives après des échecs consécutifs
    Étant donné que la dernière synchronisation réussie date du "2026-11-15T02:05:00Z"
    Et que <échecs> synchronisations planifiées ont échoué consécutivement, la dernière le "2026-11-16T03:00:00Z"
    Et qu'il est "<maintenant>"
    Quand le planificateur de l'instance 1 effectue sa vérification
    Alors <résultat>

    Exemples:
      | échecs | maintenant           | résultat                                                       |
      | 1      | 2026-11-16T03:14:00Z | aucune nouvelle synchronisation n'a été exécutée               |
      | 1      | 2026-11-16T03:15:00Z | une synchronisation "Scheduled" a réussi sur l'instance 1      |
      | 3      | 2026-11-16T03:59:00Z | aucune nouvelle synchronisation n'a été exécutée               |
      | 3      | 2026-11-16T04:00:00Z | une synchronisation "Scheduled" a réussi sur l'instance 1      |
      | 6      | 2026-11-16T04:59:00Z | aucune nouvelle synchronisation n'a été exécutée               |
      | 6      | 2026-11-16T05:00:00Z | une synchronisation "Scheduled" a réussi sur l'instance 1      |

  # --- Signalement à l'exploitant ---

  @CAT-R12 @CAT-R11
  Scénario: Un blocage par le garde-fou est signalé par un log et par l'état de la synchronisation
    Étant donné qu'une synchronisation a déjà réussi
    Et que le fournisseur ne propose aucun produit
    Quand le planificateur de l'instance 1 effectue sa vérification avec le créneau du jour échu
    Alors un log d'erreur "CatalogSyncGuardTriggered" est émis avec 2 produits actifs et 2 produits qui seraient retirés
    Quand l'exploitant consulte l'état de la synchronisation
    Alors l'état indique qu'une action est requise pour la raison "garde-fou de retrait massif déclenché"
    Et l'action conseillée invite à vérifier le fournisseur avant de forcer la synchronisation

  @CAT-R12
  Scénario: Un échec de synchronisation est signalé par un log
    Étant donné que le fournisseur est indisponible
    Quand l'exploitant déclenche une synchronisation sur l'instance 1 avec la clé d'administration
    Alors un log d'erreur "CatalogSyncFailed" est émis avec la cause de l'échec

  @CAT-R12
  Scénario: Un catalogue qui n'a pas été synchronisé depuis trop longtemps demande une action
    Étant donné que la dernière synchronisation réussie date du "2026-11-15T02:05:00Z"
    Et que les synchronisations suivantes ont échoué
    Et qu'il est "2026-11-16T05:00:00Z"
    Quand l'exploitant consulte l'état de la synchronisation
    Alors l'état indique qu'une action est requise pour la raison "aucune synchronisation réussie depuis plus de 26 h"
    Et l'état indique le nombre d'échecs consécutifs et la prochaine tentative planifiée

  @CAT-R12
  Scénario: Aucune action n'est requise quand la synchronisation est à jour
    Étant donné que la dernière synchronisation réussie date du "2026-11-16T02:05:00Z"
    Et qu'il est "2026-11-16T14:00:00Z"
    Quand l'exploitant consulte l'état de la synchronisation
    Alors l'état indique qu'aucune action n'est requise
    Et l'âge de la dernière synchronisation réussie est de 11 h 55 min

  @CAT-R12
  Scénario: L'état de la synchronisation est réservé à l'exploitant
    Quand l'état de la synchronisation est consulté sans clé d'administration
    Alors la réponse est une erreur 403 de code "admin_key_invalid"

  @CAT-R3
  Scénario: L'historique liste les runs du plus récent au plus ancien
    Étant donné qu'une synchronisation planifiée a réussi aujourd'hui
    Et qu'une synchronisation manuelle a réussi ensuite
    Quand l'exploitant consulte l'historique des synchronisations
    Alors l'historique contient dans l'ordre un run "Manual" puis un run "Scheduled"

  # --- Planification quotidienne (créneau par défaut : tous les jours à 02:00 UTC) ---

  @CAT-R2
  Scénario: Le planificateur lance la synchronisation une fois le créneau du jour échu
    Étant donné que la dernière synchronisation réussie date du "2026-11-15T02:05:00Z"
    Et qu'il est "2026-11-16T02:10:00Z"
    Quand le planificateur de l'instance 1 effectue sa vérification
    Alors une synchronisation "Scheduled" a réussi sur l'instance 1

  @CAT-R2
  Scénario: Le planificateur ne relance pas une synchronisation déjà réussie pour le créneau
    Étant donné que la dernière synchronisation réussie date du "2026-11-16T02:05:00Z"
    Et qu'il est "2026-11-16T14:00:00Z"
    Quand le planificateur de l'instance 1 effectue sa vérification
    Alors aucune nouvelle synchronisation n'a été exécutée

  @CAT-R2
  Scénario: Le planificateur n'anticipe pas le créneau du jour
    Étant donné que la dernière synchronisation réussie date du "2026-11-15T02:05:00Z"
    Et qu'il est "2026-11-16T01:30:00Z"
    Quand le planificateur de l'instance 1 effectue sa vérification
    Alors aucune nouvelle synchronisation n'a été exécutée

  @CAT-R2 @CAT-R8
  Scénario: Le planificateur rattrape une synchronisation échouée
    Étant donné que la dernière synchronisation réussie date du "2026-11-15T02:05:00Z"
    Et qu'une synchronisation planifiée a échoué le "2026-11-16T02:01:00Z"
    Et qu'il est "2026-11-16T02:15:00Z"
    Quand le planificateur de l'instance 1 effectue sa vérification
    Alors une synchronisation "Scheduled" a réussi sur l'instance 1

  @CAT-R2
  Scénario: Le planificateur rattrape le créneau manqué après un arrêt du service
    Étant donné que la dernière synchronisation réussie date du "2026-11-13T02:05:00Z"
    Et qu'il est "2026-11-16T09:00:00Z"
    Quand le planificateur de l'instance 1 effectue sa vérification
    Alors une synchronisation "Scheduled" a réussi sur l'instance 1

  # --- Exclusivité multi-instance ---

  @CAT-R10
  Scénario: Une synchronisation manuelle est refusée pendant qu'une autre est en cours sur une autre instance
    Étant donné qu'une synchronisation est en cours sur l'instance 1
    Quand l'exploitant déclenche une synchronisation sur l'instance 2 avec la clé d'administration
    Alors la réponse est une erreur 409 de code "sync_already_running"
    Et une fois la synchronisation de l'instance 1 terminée, l'historique contient un seul run "Succeeded"

  @CAT-R10
  Scénario: Une synchronisation planifiée est ignorée pendant qu'une autre est en cours
    Étant donné qu'une synchronisation est en cours sur l'instance 1
    Et que le créneau du jour est échu sans synchronisation réussie
    Quand le planificateur de l'instance 2 effectue sa vérification
    Alors l'instance 2 enregistre un run "Skipped"
    Et une fois la synchronisation de l'instance 1 terminée, l'historique contient un seul run "Succeeded"

  @CAT-R10
  Scénario: Deux planificateurs qui se déclenchent en même temps ne synchronisent qu'une fois
    Étant donné que le créneau du jour est échu sans synchronisation réussie
    Quand les planificateurs des instances 1 et 2 effectuent leur vérification simultanément
    Alors l'historique contient un seul run "Succeeded" pour le créneau du jour

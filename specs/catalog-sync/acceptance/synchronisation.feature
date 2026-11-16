# language: fr
Fonctionnalité: Synchronisation du catalogue avec le fournisseur
  Afin de proposer des produits à jour sans dépendre du fournisseur à chaque consultation,
  le service copie l'ensemble du catalogue du fournisseur dans sa base locale,
  sans doublon et sans jamais laisser le catalogue dans un état partiel.

  Contexte:
    Étant donné que le fournisseur renvoie au plus 2 produits par page

  @CAT-R1 @CAT-R4 @CAT-R5
  Scénario: Première synchronisation de l'ensemble des produits
    Étant donné que le fournisseur propose les produits suivants :
      | externalId | nom     | catégorie  | prix  | stock | disponibilité |
      | 1          | Mascara | beauty     | 9.99  | 99    | In Stock      |
      | 2          | Parfum  | fragrances | 49.99 | 3     | Low Stock     |
      | 3          | Lampe   | furniture  | 19.50 | 0     | Out of Stock  |
    Quand une synchronisation manuelle est déclenchée
    Alors la synchronisation réussit avec 3 produits récupérés, 3 insérés, 0 mis à jour, 0 retirés et 0 réactivés
    Et le catalogue local contient exactement les produits suivants :
      | externalId | nom     | catégorie  | prix  | stock | disponibilité | statut |
      | 1          | Mascara | beauty     | 9.99  | 99    | In Stock      | Active |
      | 2          | Parfum  | fragrances | 49.99 | 3     | Low Stock     | Active |
      | 3          | Lampe   | furniture  | 19.50 | 0     | Out of Stock  | Active |
    Et chaque produit a pour date de dernière mise à jour la date de la synchronisation

  @CAT-R4
  Scénario: Une synchronisation répétée avec les mêmes données ne modifie rien
    Étant donné que le fournisseur propose les produits suivants :
      | externalId | nom     | catégorie | prix |
      | 1          | Mascara | beauty    | 9.99 |
      | 2          | Parfum  | fragrance | 49.99 |
    Et qu'une synchronisation a déjà réussi
    Quand une synchronisation manuelle est déclenchée
    Alors la synchronisation réussit avec 2 produits récupérés, 0 insérés, 0 mis à jour, 0 retirés et 0 réactivés
    Et le catalogue local contient 2 produits

  @CAT-R4 @CAT-R5
  Scénario: Un produit modifié chez le fournisseur est mis à jour sans créer de doublon
    Étant donné que le fournisseur propose les produits suivants :
      | externalId | nom     | catégorie | prix |
      | 1          | Mascara | beauty    | 9.99 |
      | 2          | Parfum  | fragrance | 49.99 |
    Et qu'une synchronisation a déjà réussi
    Et que le fournisseur modifie le produit 1 avec le nom "Mascara Pro" et le prix 12.50
    Quand une synchronisation manuelle est déclenchée
    Alors la synchronisation réussit avec 2 produits récupérés, 0 insérés, 1 mis à jour, 0 retirés et 0 réactivés
    Et le catalogue local contient 2 produits
    Et le produit 1 du catalogue local a le nom "Mascara Pro" et le prix 12.50
    Et la date de dernière mise à jour du produit 1 est la date de cette synchronisation
    Et la date de dernière mise à jour du produit 2 est inchangée

  @CAT-R9
  Scénario: Un produit absent chez le fournisseur est retiré sans être supprimé
    Étant donné que le fournisseur propose les produits suivants :
      | externalId | nom     | catégorie | prix |
      | 1          | Mascara | beauty    | 9.99 |
      | 2          | Parfum  | fragrance | 49.99 |
    Et qu'une synchronisation a déjà réussi
    Et que le fournisseur ne propose plus le produit 2
    Quand une synchronisation manuelle est déclenchée
    Alors la synchronisation réussit avec 1 produits récupérés, 0 insérés, 0 mis à jour, 1 retirés et 0 réactivés
    Et le produit 2 du catalogue local a le statut "Discontinued"
    Et le produit 2 n'apparaît plus dans la liste des produits

  @CAT-R9
  Scénario: Un produit retiré qui réapparaît chez le fournisseur est réactivé
    Étant donné que le fournisseur propose les produits suivants :
      | externalId | nom     | catégorie | prix |
      | 1          | Mascara | beauty    | 9.99 |
    Et que le catalogue local contient le produit 2 "Parfum" au statut "Discontinued"
    Et que le fournisseur propose de nouveau le produit 2 "Parfum" au prix 45.00
    Quand une synchronisation manuelle est déclenchée
    Alors la synchronisation réussit avec 2 produits récupérés, 0 insérés, 0 mis à jour, 0 retirés et 1 réactivés
    Et le produit 2 du catalogue local a le statut "Active" et le prix 45.00

  @CAT-R1 @CAT-R8
  Scénario: Une panne du fournisseur en cours de récupération laisse le catalogue inchangé
    Étant donné que le fournisseur propose les produits suivants :
      | externalId | nom     | catégorie | prix |
      | 1          | Mascara | beauty    | 9.99 |
      | 2          | Parfum  | fragrance | 49.99 |
      | 3          | Lampe   | furniture | 19.50 |
    Et qu'une synchronisation a déjà réussi
    Et que le fournisseur modifie le produit 1 avec le nom "Mascara Pro" et le prix 12.50
    Et que le fournisseur ajoute le produit 4 "Chaise" au prix 80.00
    Et que le fournisseur échoue à partir de la page 2
    Quand une synchronisation manuelle est déclenchée
    Alors la synchronisation échoue
    Et le catalogue local est identique à son état avant la synchronisation

  @CAT-R1
  Scénario: Un instantané incomplet est rejeté
    Étant donné que le catalogue local contient des produits synchronisés
    Et que le fournisseur annonce 4 produits mais n'en renvoie que 3
    Quand une synchronisation manuelle est déclenchée
    Alors la synchronisation échoue
    Et le catalogue local est identique à son état avant la synchronisation

  @CAT-R11
  Scénario: Un catalogue vide chez le fournisseur ne retire pas tous les produits
    Étant donné que le catalogue local contient des produits synchronisés
    Et que le fournisseur ne propose aucun produit
    Quand une synchronisation manuelle est déclenchée
    Alors la synchronisation est bloquée par le garde-fou de retrait massif
    Et le catalogue local est identique à son état avant la synchronisation

  @CAT-R11
  Plan du scénario: Le garde-fou bloque un retrait au-delà du seuil
    Étant donné que le seuil de retrait maximal est de 50 %
    Et que le catalogue local contient 10 produits actifs synchronisés
    Et que le fournisseur ne propose plus que <restants> de ces produits
    Quand une synchronisation manuelle est déclenchée
    Alors <résultat>

    Exemples:
      | restants | résultat                                                                    |
      | 5        | la synchronisation réussit avec 5 produits récupérés, 0 insérés, 0 mis à jour, 5 retirés et 0 réactivés |
      | 4        | la synchronisation est bloquée par le garde-fou de retrait massif           |

  @CAT-R11 @CAT-R3
  Scénario: L'exploitant peut forcer un retrait massif légitime
    Étant donné que le catalogue local contient 10 produits actifs synchronisés
    Et que le fournisseur ne propose plus que 1 de ces produits
    Quand une synchronisation manuelle forcée est déclenchée
    Alors la synchronisation réussit avec 1 produits récupérés, 0 insérés, 0 mis à jour, 9 retirés et 0 réactivés
    Et le run indique que le forçage a été utilisé

  @CAT-R11
  Scénario: La première synchronisation n'est pas soumise au garde-fou
    Étant donné que le catalogue local est vide
    Et que le fournisseur propose les produits suivants :
      | externalId | nom     | catégorie | prix |
      | 1          | Mascara | beauty    | 9.99 |
    Quand une synchronisation manuelle est déclenchée
    Alors la synchronisation réussit avec 1 produits récupérés, 1 insérés, 0 mis à jour, 0 retirés et 0 réactivés

  @CAT-R8
  Scénario: Une erreur de base pendant l'application annule toute la synchronisation
    Étant donné que le fournisseur propose les produits suivants :
      | externalId | nom     | catégorie | prix |
      | 1          | Mascara | beauty    | 9.99 |
      | 2          | Parfum  | fragrance | 49.99 |
    Et qu'une synchronisation a déjà réussi
    Et que le fournisseur modifie le produit 1 avec le nom "Mascara Pro" et le prix 12.50
    Et que le fournisseur ajoute le produit 3 "Lampe" au prix 19.50
    Et que la base de données échoue pendant l'application de la synchronisation
    Quand une synchronisation manuelle est déclenchée
    Alors la synchronisation échoue
    Et le catalogue local est identique à son état avant la synchronisation

  @CAT-R8
  Scénario: Une synchronisation ultérieure rétablit la situation après un échec
    Étant donné que le fournisseur propose les produits suivants :
      | externalId | nom     | catégorie | prix |
      | 1          | Mascara | beauty    | 9.99 |
    Et que le fournisseur échoue à partir de la page 1
    Et qu'une synchronisation manuelle a échoué
    Et que le fournisseur fonctionne de nouveau
    Quand une synchronisation manuelle est déclenchée
    Alors la synchronisation réussit avec 1 produits récupérés, 1 insérés, 0 mis à jour, 0 retirés et 0 réactivés
    Et l'historique contient un run "Failed" suivi d'un run "Succeeded"

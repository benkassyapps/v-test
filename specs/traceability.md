# Matrice de traçabilité

> Exigence → où elle est vérifiée. Mise à jour à la fin de chaque tâche.
> La couverture est contrôlée automatiquement par `TraceabilityTests` (tags `@ID` des `.feature` et `Trait("Spec", "ID")` des tests xUnit).
> Statut : ⏳ spécifié, pas encore implémenté · ✅ implémenté et vert.

## Catalogue et synchronisation

| Exigence | Scénarios d'acceptation | Tests xUnit | Statut |
|---|---|---|---|
| CAT-R1 Récupération complète | synchronisation.feature (première synchro, panne en page 2, instantané incomplet) | — | ⏳ |
| CAT-R2 Synchro quotidienne | declenchement.feature (scénarios du planificateur, espacement des tentatives) | Unitaires : calcul du créneau échu (Cronos), calcul de l'espacement | ⏳ |
| CAT-R3 Déclenchement manuel | declenchement.feature (résumé, 403, forçage, 502, historique) | — | ⏳ |
| CAT-R4 Sans doublon | synchronisation.feature (première synchro, idempotence, mise à jour) | Intégration : index unique `ExternalId` | ⏳ |
| CAT-R5 Informations conservées | synchronisation.feature (première synchro, mise à jour) | — | ⏳ |
| CAT-R6 Liste paginée | liste-produits.feature (parcours, retirés, défaut, stabilité, paramètres invalides) | Unitaires : encodage/décodage du curseur | ⏳ |
| CAT-R7 Indépendance fournisseur | liste-produits.feature, ajout.feature, declenchement.feature (502) | — | ⏳ |
| CAT-R8 Cohérence et rattrapage | synchronisation.feature (panne fournisseur, erreur de base, rattrapage), declenchement.feature (rattrapage planifié) | — | ⏳ |
| CAT-R9 Produits retirés | synchronisation.feature (retrait, réactivation), liste-produits.feature, modification-suppression.feature | — | ⏳ |
| CAT-R11 Garde-fou de retrait massif | synchronisation.feature (catalogue vide, seuil, forçage, première synchro), declenchement.feature (422, planificateur) | Unitaires : calcul du seuil | ⏳ |
| CAT-R12 Signalement à l'exploitant | declenchement.feature (log du garde-fou, log d'échec, catalogue périmé, état à jour, 403) | Unitaires : calcul de `actionRequired` | ⏳ |
| CAT-R10 Exécution exclusive | declenchement.feature (409, Skipped, planificateurs simultanés) | Intégration : libération du verrou à la coupure de connexion (CAT-R10.4) | ⏳ |

## Panier

| Exigence | Scénarios d'acceptation | Tests xUnit | Statut |
|---|---|---|---|
| CART-R1 Un seul panier | creation-consultation.feature (20 créations simultanées), ajout.feature (création implicite), concurrence.feature (premiers ajouts simultanés) | Intégration : stress répété ; index unique `UserId` | ⏳ |
| CART-R2 Panier personnel | creation-consultation.feature (consultation, modification d'une ligne d'autrui) | — | ⏳ |
| CART-R3 Produit existant et actif | ajout.feature (inexistant, retiré) | Unitaires : agrégat `Cart` | ⏳ |
| CART-R4 Quantité 1–999 | ajout.feature, modification-suppression.feature, concurrence.feature | Unitaires : agrégat ; Intégration : contrainte CHECK | ⏳ |
| CART-R5 Une ligne par produit, cumul | ajout.feature, concurrence.feature | Unitaires : agrégat ; Intégration : index unique (CartId, ProductId), stress répété | ⏳ |
| CART-R6 Nom et prix conservés | ajout.feature, modification-suppression.feature, creation-consultation.feature | Unitaires : agrégat | ⏳ |
| CART-R7 Version globale | ajout.feature, modification-suppression.feature, concurrence.feature | Unitaires : agrégat | ⏳ |
| CART-R8 Version de ligne et conflit | modification-suppression.feature, concurrence.feature | Unitaires : agrégat | ⏳ |
| CART-R9 Lignes différentes sans conflit | concurrence.feature (exemple d'Alice ×2) | Intégration : stress répété | ⏳ |
| CART-R10 Pas de réapparition | modification-suppression.feature, concurrence.feature | Unitaires : agrégat | ⏳ |
| CART-R11 Produit retiré dans un panier | modification-suppression.feature, ajout.feature | Unitaires : agrégat | ⏳ |
| CART-R12 Cohérence en cas d'échec | modification-suppression.feature (panne de base ×3) | — | ⏳ |
| CART-R13 Créer / récupérer | creation-consultation.feature | — | ⏳ |
| CART-R14 Consulter | creation-consultation.feature | Unitaires : calcul des montants | ⏳ |
| CART-R15 Ajouter (dont création implicite) | ajout.feature, concurrence.feature | — | ⏳ |
| CART-R16 Modifier une quantité | modification-suppression.feature | — | ⏳ |
| CART-R17 Supprimer | modification-suppression.feature | — | ⏳ |

## Plateforme

| Exigence | Scénarios d'acceptation | Tests xUnit / autre | Statut |
|---|---|---|---|
| PLT-1 Multi-instance | plateforme.feature (X-Instance), scénarios de concurrence à 2 instances | — | ⏳ |
| PLT-2 Identification | plateforme.feature | — | ⏳ |
| PLT-3 Contrat d'erreurs | plateforme.feature + codes vérifiés dans tous les scénarios | — | ⏳ |
| PLT-4 Démarrage reproductible | — | **Vérification manuelle** (`docker compose up --build`, procédure du README). Exemptée dans `TraceabilityTests`. | ⏳ |
| PLT-5 Migrations et RCSI | — | Intégration : migrations appliquées, RCSI actif | ⏳ |
| PLT-6 Observabilité | plateforme.feature (santé) | — | ⏳ |
| PLT-7 OpenAPI | plateforme.feature | — | ⏳ |
| PLT-8 Secrets | — | Unitaires : aucun secret non vide dans les fichiers de configuration versionnés ; `/security-review` final | ⏳ |

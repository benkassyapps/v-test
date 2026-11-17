# Matrice de traçabilité

> Exigence → où elle est vérifiée. Mise à jour à la fin de chaque tâche.
> La couverture est contrôlée automatiquement par `TraceabilityTests` (tags `@ID` des `.feature` et `Trait("Spec", "ID")` des tests xUnit).
> Statut : ⏳ spécifié, pas encore implémenté · 🟡 règles implémentées et testées unitairement, intégration ou acceptation à venir · ✅ implémenté et vert de bout en bout.

## Catalogue et synchronisation

| Exigence | Scénarios d'acceptation | Tests xUnit | Statut |
|---|---|---|---|
| CAT-R1 Récupération complète | synchronisation.feature (première synchro, panne en page 2, instantané incomplet) | Unitaires : validation de l'instantané ; Intégration : pagination du fournisseur, total incohérent | ✅ |
| CAT-R2 Synchro quotidienne | declenchement.feature (scénarios du planificateur, espacement des tentatives) | Unitaires : `SyncSchedulePolicyTests` (créneau, rattrapage, espacement, débordement, cron rare) | ✅ |
| CAT-R3 Déclenchement manuel | declenchement.feature (résumé, 403, forçage, 502, historique) | Unitaires : comparaison de la clé d'administration | ✅ |
| CAT-R4 Sans doublon | synchronisation.feature (première synchro, idempotence, mise à jour) | Unitaires : idempotence, arrondi des prix ; Intégration : index unique `ExternalId` | ✅ |
| CAT-R5 Informations conservées | synchronisation.feature (première synchro, mise à jour) | Unitaires : `SyncPlannerTests` | ✅ |
| CAT-R6 Liste paginée | liste-produits.feature (parcours, retirés, défaut, stabilité, paramètres invalides) | Unitaires : curseur ; Intégration : plan d'exécution (recherche sur l'index clusterisé) | ✅ |
| CAT-R7 Indépendance fournisseur | liste-produits.feature, ajout.feature, declenchement.feature (502) | Intégration : panne du fournisseur renvoyée comme échec explicite | ✅ |
| CAT-R8 Cohérence et rattrapage | synchronisation.feature (panne fournisseur, erreur de base, rattrapage), declenchement.feature (rattrapage planifié) | Unitaires : cycle de vie de `SyncRun` ; Intégration : panne de base pendant l'application | ✅ |
| CAT-R9 Produits retirés | synchronisation.feature (retrait, réactivation), liste-produits.feature, modification-suppression.feature | Unitaires : `SyncPlannerTests` | ✅ |
| CAT-R10 Exécution exclusive | declenchement.feature (409, Skipped, planificateurs simultanés) | Intégration : verrou exclusif entre instances, libération à la coupure de connexion (`KILL`), synchros manuelles simultanées | ✅ |
| CAT-R11 Garde-fou de retrait massif | synchronisation.feature (catalogue vide, seuil, forçage, première synchro), declenchement.feature (422, planificateur) | Unitaires : seuil strict, catalogue vide, première synchro | ✅ |
| CAT-R12 Signalement à l'exploitant | declenchement.feature (log du garde-fou, log d'échec, catalogue périmé, état à jour, 403) | Unitaires : `actionRequired`, prochaine tentative, diagnostic du run | ✅ |

## Panier

| Exigence | Scénarios d'acceptation | Tests xUnit | Statut |
|---|---|---|---|
| CART-R1 Un seul panier | creation-consultation.feature (20 créations simultanées), ajout.feature (création implicite), concurrence.feature (premiers ajouts simultanés) | Intégration : stress répété (30 itérations, 2 instances), index unique `UserId` | ✅ |
| CART-R2 Panier personnel | creation-consultation.feature (consultation, modification d'une ligne d'autrui) | Unitaires : ligne absente du panier introuvable | ✅ |
| CART-R3 Produit existant et actif | ajout.feature (inexistant, retiré) | Unitaires : agrégat `Cart` | ✅ |
| CART-R4 Quantité 1–999 | ajout.feature, modification-suppression.feature, concurrence.feature | Unitaires : agrégat ; Intégration : contrainte CHECK | ✅ |
| CART-R5 Une ligne par produit, cumul | ajout.feature, concurrence.feature | Unitaires : agrégat ; Intégration : index unique (CartId, ProductId), stress répété | ✅ |
| CART-R6 Nom et prix conservés | ajout.feature, modification-suppression.feature, creation-consultation.feature, **panier-et-synchronisation.feature (bout en bout avec une vraie synchro)** | Unitaires : agrégat | ✅ |
| CART-R7 Version globale | ajout.feature, modification-suppression.feature, concurrence.feature | Unitaires : agrégat (+1 exact, refus sans effet, même quantité) | ✅ |
| CART-R8 Version de ligne et conflit | modification-suppression.feature, concurrence.feature | Unitaires : agrégat | ✅ |
| CART-R9 Lignes différentes sans conflit | concurrence.feature (exemple d'Alice ×2) | Unitaires : agrégat ; Intégration : stress répété | ✅ |
| CART-R10 Pas de réapparition | modification-suppression.feature, concurrence.feature | Unitaires : agrégat | ✅ |
| CART-R11 Produit retiré dans un panier | modification-suppression.feature, ajout.feature, panier-et-synchronisation.feature | Unitaires : agrégat | ✅ |
| CART-R12 Cohérence en cas d'échec | modification-suppression.feature (panne de base ×3) | Intégration : 503 avec en-tête X-Instance | ✅ |
| CART-R13 Créer / récupérer | creation-consultation.feature | Unitaires : panier créé vide en version 0 | ✅ |
| CART-R14 Consulter | creation-consultation.feature | Unitaires : calcul des montants | ✅ |
| CART-R15 Ajouter (dont création implicite) | ajout.feature, concurrence.feature | Unitaires : agrégat | ✅ |
| CART-R16 Modifier une quantité | modification-suppression.feature | Unitaires : agrégat | ✅ |
| CART-R17 Supprimer | modification-suppression.feature | Unitaires : agrégat | ✅ |
| CART-R18 Clé d'idempotence | idempotence.feature (confirmation perdue, rejeu, autre requête, refus rejoué, envois simultanés, par utilisateur, expiration, format) | Unitaires : empreinte et format de clé ; intégration : purge par lots | ✅ |

## Plateforme

| Exigence | Scénarios d'acceptation | Tests xUnit / autre | Statut |
|---|---|---|---|
| PLT-1 Multi-instance | plateforme.feature (X-Instance), scénarios de concurrence à 2 instances | Intégration : `HostingTests` (2 instances, `X-Instance`) | ✅ |
| PLT-2 Identification | plateforme.feature | Unitaires : format de `X-User-Id` | ✅ |
| PLT-3 Contrat d'erreurs | plateforme.feature + codes vérifiés dans tous les scénarios | Unitaires : `ProblemMappingTests`, `ResultTests` ; Intégration : 404 au format ProblemDetails | ✅ |
| PLT-4 Démarrage reproductible | — | **Vérification manuelle** du 2026-11-16 ([docs/verification-manuelle.md](../docs/verification-manuelle.md)) : `docker compose up --build`, 2 instances derrière nginx, synchro réelle DummyJSON, scénario d'Alice, panne du fournisseur. Exemptée dans `TraceabilityTests`. | ✅ |
| PLT-5 Migrations et RCSI | — | Intégration : `DatabaseTests` (migrations appliquées, RCSI actif) | ✅ |
| PLT-6 Observabilité | plateforme.feature (santé) | Intégration : `/health` sur les 2 instances | ✅ |
| PLT-7 OpenAPI | plateforme.feature | Intégration : document OpenAPI publié | ✅ |
| PLT-8 Secrets | — | Unitaires : `NoSecretsInRepositoryTests` (appsettings, docker-compose, .gitignore) ; `/security-review` final | ✅ |

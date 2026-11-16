# Bilan de l'usage de l'IA

> Document en cours de rédaction : il est complété au fil du projet. La version finale sera relue avant livraison.

## Outils employés

- **Claude Code** (extension VS Code) : cadrage, comparaison d'options d'architecture, rédaction des specs, génération du code et des tests, revues (`/code-review`, `/security-review`).

## Démarche

- **Spec-driven development** : constitution, exigences EARS et critères d'acceptation Gherkin (`specs/`) **avant** tout code. Les critères d'acceptation sont exécutés par Reqnroll.
- **Validation humaine à chaque phase** (exigences → design → tâches et implémentation) : l'IA propose, le candidat challenge, tranche et valide.
- **Revue systématique** : `/code-review` à la fin de chaque bloc, `/security-review` après le bloc Api et avant livraison. Les constats sont consignés dans `docs/ai-reviews.md`.

## Journal des propositions de l'IA validées, adaptées ou rejetées

| # | Phase | Proposition de l'IA | Décision du candidat | Résultat |
|---|---|---|---|---|
| 1 | Cadrage | Planifier la synchro avec un `BackgroundService` et un **verrou distribué écrit à la main** (`sp_getapplock`). | **Adaptée** : le candidat préfère une bibliothèque éprouvée à du code de verrou maison à écrire et à tester. | `DistributedLock.SqlServer` (détection de perte de connexion, keepalive) et `Cronos` pour le créneau. La logique de planification (table `SyncRuns`, rattrapage) reste propre au projet. |
| 2 | Cadrage | Code, **commentaires et messages d'API en anglais**, documentation en français. | **Modifiée** : seuls les identifiants du code sont en anglais ; commentaires, messages d'API, logs et docs en français. | Règle inscrite dans `specs/constitution.md` (§6). |
| 3 | Cadrage | Recommandation de la Clean Architecture (A) plutôt que des Vertical Slices (B) ou d'un monolithe modulaire (C). | **Validée après challenge** : le candidat a demandé pourquoi pas B ou C ; l'IA a reconnu que B était très défendable. | Clean Archi en 4 projets **organisée par fonctionnalité** (`Catalog/`, `Carts/` dans chaque couche), sans MediatR ni repository générique. |
| 4 | Cadrage | Le candidat suggère des tests **SpecFlow** pour exécuter les critères Given/When/Then. | **Adaptée par l'IA** : SpecFlow est en fin de vie (arrêt fin 2024). L'IA propose Reqnroll, son successeur open source compatible. | Les `.feature` sous `specs/` sont la source unique des critères et sont exécutés par `Shop.AcceptanceTests`. |
| 5 | Cadrage | Vérification de couverture des exigences par une commande `/spec-check` **exécutée par l'agent**. | **Adaptée** : à la question du candidat, l'IA reconnaît qu'une vérification par LLM n'est ni déterministe ni exécutable en CI. | Test xUnit déterministe `TraceabilityTests`. `/spec-check` n'est plus qu'une aide qui lance ce test et explique les trous. |
| 6 | Cadrage | Migrations EF appliquées **au démarrage de chaque instance** et RCSI activé par une migration. | **Corrigée** : la question du candidat (« pourquoi RCSI par une migration ? ») a révélé un défaut. `ALTER DATABASE … READ_COMMITTED_SNAPSHOT` exige d'être la seule connexion, ce qui est incompatible avec plusieurs instances qui démarrent ensemble. | Migrateur dédié qui s'exécute seul avant les instances, RCSI dans la migration initiale et contrôle au démarrage. Rappel documenté : la correction des règles métier ne dépend pas de RCSI. |
| 7 | Exigences | Ajout d'un article **sans panier existant** : réponse `404 cart_not_found`, le client devant d'abord appeler `PUT /api/cart`. | **Rejetée** : le candidat préfère créer le panier implicitement pour simplifier le parcours de l'utilisateur. | CART-R15.2 et R15.3 : création implicite dans la même transaction que l'ajout ; un ajout refusé ne laisse aucun panier vide ; CART-R1.3 garantit l'unicité en cas de premiers ajouts simultanés. Deux scénarios d'acceptation ajoutés. |
| 8 | Exigences | Refuser une synchro uniquement quand le fournisseur renvoie un **catalogue vide**. | **Adaptée après challenge** : le candidat demande pourquoi ne pas tout retirer. En argumentant, l'IA relève la limite de sa propre règle : un catalogue anormalement réduit (3 produits au lieu de 194) passerait. | CAT-R11 : seuil de retrait configurable (50 %), dont le catalogue vide est un cas particulier ; forçage manuel par l'exploitant (`?force=true`) ; le planificateur ne force jamais. |
| 9 | Exigences | Garde-fou CAT-R11 spécifié **sans moyen de prévenir l'exploitant** : un blocage à 02:00 n'était visible qu'en consultant soi-même l'historique. Relance du planificateur à chaque tick. | **Trou relevé par le candidat** (« comment l'utilisateur saura-t-il qu'il doit forcer ? »). L'IA propose 5 moyens de signalement ; le candidat retient les logs et l'endpoint d'état, et **rejette le health check `Degraded`**. En analysant ce cas, l'IA relève un second trou : ~96 runs en échec par jour. | CAT-R12 : logs `Error` avec identifiants d'événement stables, `GET /api/admin/catalog/sync-status` avec `actionRequired`, raison et action conseillée, détection d'un catalogue périmé (26 h). CAT-R2.4 : espacement progressif des tentatives (15 min → 2 h). |

## Vérifications effectuées

*(à compléter au fil de l'implémentation : tests de concurrence, revue du SQL généré, exécution multi-instance réelle, constats de revue traités…)*

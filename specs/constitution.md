# Constitution du projet

Principes **non négociables** qui s'appliquent à toute spécification, tout design et tout code du dépôt.
Un écart à l'un de ces principes doit être explicitement justifié dans `docs/DECISIONS.md` et validé avant d'être implémenté.

## 1. Démarche : la spécification d'abord
1. Aucun comportement n'est implémenté sans exigence identifiée (`CAT-R*`, `CART-R*`, `PLT-*`).
2. Si un comportement doit changer, on modifie **d'abord** la spec (`requirements.md`, `.feature`, `design.md`), puis le code.
3. Les critères d'acceptation sont écrits en Gherkin (`specs/**/acceptance/*.feature`) et **exécutés** par les tests d'acceptation : ils sont la source unique de vérité du comportement attendu.
4. Chaque exigence est couverte par au moins un scénario ou un test qui la référence (tag `@ID` ou `Trait("Spec", "ID")`). Le test `TraceabilityTests` le vérifie à chaque `dotnet test`.

## 2. Cohérence avant tout, y compris en multi-instance
1. Les instances applicatives sont **sans état partagé** : aucune règle métier ne repose sur la mémoire d'un processus, un cache local ou un verrou en mémoire.
2. **SQL Server est l'unique point de coordination** entre instances (transactions, verrous, contraintes, verrous applicatifs `sp_getapplock`).
3. Toute opération qui modifie des données est **atomique** : elle réussit entièrement ou n'a **aucun effet**. Une demande rejetée ne modifie rien, pas même une version.
4. Les contraintes de base (unicité, CHECK, clés étrangères) **doublent** les règles du domaine : un bug applicatif ne doit pas pouvoir produire un état incohérent.

## 3. Invariants de concurrence (ne jamais casser)
1. **Panier** : toute transaction qui modifie un panier ou ses lignes commence par verrouiller la ligne `Carts` correspondante (`UPDLOCK, ROWLOCK`). Aucune écriture sur `CartLines` ne contourne ce verrou.
2. **Panier** : un conflit n'est détecté que sur la **version de la ligne** visée, jamais sur la version globale du panier.
3. **Catalogue** : au plus une synchronisation s'exécute à un instant donné, toutes instances confondues (verrou distribué).
4. **Catalogue** : une synchronisation n'écrit qu'à partir d'un **instantané complet** du fournisseur, appliqué dans **une seule transaction**.

## 4. Architecture
1. Clean Architecture en 4 projets : `Shop.Domain` → `Shop.Application` → `Shop.Infrastructure` / `Shop.Api`. Les dépendances pointent vers le domaine.
2. `Shop.Domain` n'a **aucune dépendance** (ni EF Core, ni ASP.NET). Les règles métier vivent dans les agrégats, pas dans les endpoints ni dans le SQL.
3. Organisation par fonctionnalité (`Catalog/`, `Carts/`) à l'intérieur de chaque couche.
4. Simplicité : pas de repository générique, pas de MediatR. Toute nouvelle dépendance est justifiée (avantage, coût, limite) dans `docs/DECISIONS.md`.
5. Le temps est injecté (`TimeProvider`) et toutes les dates sont en **UTC**.

## 5. Contrat HTTP
1. Toutes les erreurs sont renvoyées au format **ProblemDetails (RFC 9457)**, avec une extension `code` stable et lisible par une machine (ex. `cart_line_version_conflict`).
2. Les codes HTTP suivent la table du contrat d'erreurs (`specs/platform/requirements.md`, PLT-3). Une même situation produit toujours le même code.
3. Les réponses d'erreur ne divulguent aucun détail interne (pile d'appels, SQL, chaîne de connexion).

## 6. Langue
1. **Code source en anglais** : identifiants (types, membres, variables), routes, noms de tables et colonnes, valeurs `code` des erreurs.
2. **Tout le reste en français** : commentaires, messages d'API (`title`, `detail`, messages de validation), logs, noms lisibles des tests, specs et documentation.

## 7. Sécurité
1. Aucun secret (mot de passe, clé, jeton) n'est commité. La configuration d'exemple utilise des valeurs factices (`.env.example`).
2. Les entrées HTTP sont validées avant tout traitement. Le SQL brut est toujours paramétré.
3. Les limites de sécurité assumées par le périmètre de l'exercice (identification par en-tête) sont documentées.

## 8. Qualité et revue
1. Le code compile sans avertissement (`TreatWarningsAsErrors`).
2. À la fin de chaque bloc de travail : tests verts, puis `/code-review`, et `/security-review` après le bloc Api et avant livraison.
3. Chaque constat de revue est **corrigé**, **rejeté avec justification** ou **reporté**, et consigné dans `docs/ai-reviews.md`.

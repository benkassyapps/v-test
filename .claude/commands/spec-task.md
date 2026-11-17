---
description: Implémente une tâche de specs/*/tasks.md en suivant le workflow spec-driven
argument-hint: <identifiant de tâche ou d'évolution, ex. T2.2 ou E1>
---

Implémente la tâche **$ARGUMENTS** en respectant strictement `AGENTS.md` et `specs/constitution.md`.

> Ce fichier est la **seule** définition du déroulé d'une tâche ; `AGENTS.md` et les `tasks.md` y renvoient.
> Hors Claude Code (autre agent, lecture par le candidat), `$ARGUMENTS` désigne l'identifiant de la tâche.

1. **Lire avant d'écrire**
   - Trouve $ARGUMENTS dans `specs/*/tasks.md` (tâches numérotées ou « Évolutions reportées ») et liste les exigences qu'elle couvre.
   - Lis ces exigences dans les `requirements.md`, les scénarios `.feature` qui portent leurs tags, et les sections concernées des `design.md`.
   - Si une information manque ou si la spec est ambiguë ou contradictoire : **arrête-toi et pose la question**, ne devine pas.

2. **Implémenter**
   - Code source en anglais ; commentaires, messages d'API et logs en français.
   - Respecte les invariants de concurrence d'`AGENTS.md` et relis sa section « Pièges connus » : ne réintroduis aucune erreur déjà rencontrée.
   - Si l'implémentation révèle qu'un comportement spécifié doit changer : applique la règle 3 de la démarche d'`AGENTS.md` (spec d'abord, validation, puis `/spec-check`) avant de coder.

3. **Tester**
   - Écris les tests listés dans la tâche, tracés par exigence : `[Trait("Spec", "<ID>")]` en xUnit, tag `@<ID>` en Gherkin (étapes Reqnroll des scénarios concernés).
   - Un nouveau `.feature` doit être ajouté au `.csproj` de `tests/Shop.AcceptanceTests`, sinon il n'est pas exécuté.
   - `dotnet build Shop.slnx` doit afficher 0 avertissement ; `dotnet test Shop.slnx` doit être vert. Si `TraceabilityTests` échoue, lance `/spec-check` pour en comprendre la cause.

4. **Clore**
   - Coche les cases de la tâche dans son `tasks.md`, seulement si l'étape 3 est verte (build sans avertissement, tests verts).
   - Mets à jour `specs/traceability.md` : ajoute **tous** les scénarios (fichier `.feature` et cas couverts) et tests xUnit (unitaires, intégration) écrits pour chaque exigence dans les colonnes correspondantes, puis le statut (✅ si l'exigence est entièrement couverte).
   - Lance `/spec-check` pour vérifier la couverture et la cohérence de la matrice.
   - Résume : ce qui a été fait, les tests ajoutés, les choix faits et ce qui reste éventuellement ouvert.
   - Ne committe pas sans demande explicite.

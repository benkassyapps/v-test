---
description: Implémente une tâche de specs/*/tasks.md en suivant le workflow spec-driven
argument-hint: <identifiant de tâche, ex. T2.2>
---

Implémente la tâche **$ARGUMENTS** en respectant strictement `AGENTS.md` et `specs/constitution.md`.

1. **Lire avant d'écrire**
   - Trouve la tâche $ARGUMENTS dans `specs/*/tasks.md` et liste les exigences qu'elle couvre.
   - Lis ces exigences dans les `requirements.md`, les scénarios `.feature` qui portent leurs tags, et les sections concernées des `design.md`.
   - Si une information manque ou si la spec est ambiguë ou contradictoire : **arrête-toi et pose la question**, ne devine pas.

2. **Implémenter**
   - Code source en anglais ; commentaires, messages d'API et logs en français.
   - Respecte les invariants de concurrence d'`AGENTS.md`.
   - Si l'implémentation révèle qu'un comportement spécifié doit changer : propose d'abord la modification de la spec, attends la validation.

3. **Tester**
   - Écris les tests listés dans la tâche, avec `[Trait("Spec", "<ID>")]` (xUnit) ou les étapes Reqnroll des scénarios concernés.
   - `dotnet build Shop.slnx` doit afficher 0 avertissement ; `dotnet test Shop.slnx` doit être vert.

4. **Clore**
   - Coche les cases de la tâche dans son `tasks.md`.
   - Mets à jour `specs/traceability.md` (statut ✅ des exigences entièrement couvertes).
   - Résume : ce qui a été fait, les tests ajoutés, les choix faits et ce qui reste éventuellement ouvert.
   - Ne committe pas sans demande explicite.

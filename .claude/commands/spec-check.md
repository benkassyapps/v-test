---
description: Vérifie la couverture des exigences par les scénarios et les tests, puis explique les trous
---

1. Lance la vérification déterministe :
   ```bash
   dotnet test tests/Shop.UnitTests --filter "FullyQualifiedName~TraceabilityTests"
   ```
   C'est ce test qui fait foi ; ton rôle est seulement d'expliquer son résultat.

2. Si le test échoue, pour chaque problème signalé :
   - **exigence non couverte** : relis l'exigence dans son `requirements.md` et propose le scénario Gherkin (en français, avec le tag) ou le test xUnit (avec `Trait("Spec", …)`) qui la vérifierait ;
   - **ID inconnu** dans un tag ou un trait : indique s'il s'agit d'une faute de frappe ou d'une exigence supprimée ;
   - **`.feature` non exécuté** : indique quel fichier n'est pas inclus dans `tests/Shop.AcceptanceTests`.

3. Compare avec `specs/traceability.md` et signale les écarts de la matrice.

N'écris aucun code ni aucun test sans validation : propose seulement.

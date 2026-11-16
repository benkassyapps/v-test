# AGENTS.md — Instructions pour les agents IA

Ce fichier guide tout agent IA (Claude Code, Copilot, Codex…) qui travaille sur ce dépôt. Il est lu **avant** toute modification.

## Le projet en bref
Service .NET 10 : catalogue de produits synchronisé chaque jour depuis DummyJSON et panier personnel par utilisateur.
SQL Server, **plusieurs instances en parallèle**. Le sujet est dans `EXERCICE_CANDIDAT.md`.

## Démarche : spec-driven development (obligatoire)
1. **La spec est la source de vérité** : `specs/constitution.md` (principes non négociables), puis, par fonctionnalité, `requirements.md` → `acceptance/*.feature` → `design.md` → `tasks.md`.
2. **Aucun code sans exigence identifiée** (`CAT-R*`, `CART-R*`, `PLT-*`). Si une demande ne correspond à aucune exigence, le signaler au lieu de coder.
3. **Si un comportement doit changer, modifier la spec d'abord** (exigence, scénario, design), la faire valider, puis coder.
4. On travaille **tâche par tâche** dans l'ordre de `specs/platform/tasks.md`. À la fin d'une tâche :
   - `dotnet build` sans avertissement et `dotnet test` vert ;
   - tests tracés : `[Trait("Spec", "CART-R9")]` en xUnit, tag `@CART-R9` en Gherkin ;
   - case de la tâche cochée et `specs/traceability.md` mis à jour.
5. Les `.feature` vivent sous `specs/` et sont **exécutés** par `tests/Shop.AcceptanceTests` (Reqnroll, Gherkin français). Ne jamais les dupliquer dans le projet de test.

## Boucle de revue
- Fin de chaque bloc : `/code-review high` sur le diff du bloc.
- Fin du bloc 4 et avant livraison : `/security-review`.
- Chaque constat est **corrigé**, **rejeté avec justification** ou **reporté** (ajouté à un `tasks.md`), puis consigné dans `docs/ai-reviews.md`. Aucune correction appliquée sans être comprise.
- Les propositions de l'IA modifiées ou rejetées par le candidat sont consignées dans `AI_USAGE.md` (journal).

## Conventions
- **Langue** : seul le code source est en anglais (types, membres, variables, routes, tables et colonnes, valeurs `code` des erreurs). **Tout le reste est en français** : commentaires, messages d'API (`title`, `detail`, validation), logs, noms lisibles des tests, documentation.
- **Architecture** : `Shop.Domain` (pur, sans dépendance) ← `Shop.Application` (cas d'usage, ports par cas d'usage) ← `Shop.Infrastructure` / `Shop.Api`. Organisation par fonctionnalité (`Catalog/`, `Carts/`). **Ni MediatR, ni repository générique.**
- **Erreurs** : cas métier = `Result` / `DomainError` (pas d'exception) ; sortie HTTP = ProblemDetails avec une extension `code` (table dans `specs/platform/design.md` §5).
- **Temps** : toujours `TimeProvider` injecté, toujours UTC.
- Versions de paquets centralisées dans `Directory.Packages.props` (ligne 10.0.x pour Microsoft, xunit.v3 3.x).

## Invariants de concurrence — à ne jamais casser
1. Toute transaction qui modifie un panier commence par `SELECT … FROM Carts WITH (UPDLOCK, HOLDLOCK, ROWLOCK)`. Aucune écriture sur `CartLines` ne contourne ce verrou.
2. Un conflit ne se détecte que sur la **version de la ligne**, jamais sur la version globale du panier.
3. Ordre des verrous : `Carts` → `Products`. La synchro ne verrouille que `Products`.
4. Les commandes de panier **ne sont pas rejouées automatiquement** (risque de double application si l'accusé de commit est perdu).
5. Synchro : verrou distribué `catalog-sync`, instantané complet **avant** toute écriture, application en **une seule transaction**.
6. Aucune règle métier ne repose sur la mémoire d'un processus : les instances sont sans état.

## Commandes
```bash
dotnet build Shop.slnx                    # doit afficher 0 avertissement
dotnet test Shop.slnx                     # Docker requis (SQL Server via Testcontainers)
dotnet test --filter "Spec=CART-R9"       # tests xUnit d'une exigence
docker compose up --build                 # SQL Server + migrateur + 2 instances + nginx (:8080)
```

## Interdits
- Jamais de secret (mot de passe, clé, jeton) dans le dépôt : utiliser `.env` (ignoré par git) ou `dotnet user-secrets`.
- Ne pas désactiver un test, un avertissement ou une règle d'analyse pour « faire passer » le build sans le justifier dans le code et dans la revue.
- Ne pas committer sans demande explicite du candidat.

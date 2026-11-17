# AGENTS.md — Instructions pour les agents IA

Ce fichier guide tout agent IA (Claude Code, Copilot, Codex…) qui travaille sur ce dépôt. Il est lu **avant** toute modification.

## Le projet en bref
Service .NET 10 : catalogue de produits synchronisé chaque jour depuis DummyJSON et panier personnel par utilisateur.
SQL Server, **plusieurs instances en parallèle**. Le sujet est dans `docs/EXERCICE_CANDIDAT.md`.

## Démarche : spec-driven development (obligatoire)
1. **La spec est la source de vérité** : `specs/constitution.md` (principes non négociables), puis, par fonctionnalité, `requirements.md` → `acceptance/*.feature` → `design.md` → `tasks.md`.
2. **Aucun code sans exigence identifiée** (`CAT-R*`, `CART-R*`, `PLT-*`). Si une demande ne correspond à aucune exigence, le signaler au lieu de coder.
3. **Si un comportement doit changer, modifier la spec d'abord** (exigence, scénario, design), la faire valider, puis coder. Après la modification, lancer `/spec-check` : il transforme les exigences devenues non couvertes en liste de scénarios et de tests à écrire.
4. On travaille **tâche par tâche** dans l'ordre de `specs/platform/tasks.md`. À la fin d'une tâche :
   - `dotnet build` sans avertissement et `dotnet test` vert ; si `TraceabilityTests` échoue, `/spec-check` en explique la cause ;
   - tests tracés : `[Trait("Spec", "CART-R9")]` en xUnit, tag `@CART-R9` en Gherkin ;
   - case de la tâche cochée et `specs/traceability.md` mis à jour.
5. Les `.feature` vivent sous `specs/` et sont **exécutés** par `tests/Shop.AcceptanceTests` (Reqnroll, Gherkin français). Ne jamais les dupliquer dans le projet de test.

## Boucle de revue
- Fin de chaque bloc : `/spec-check` (couverture des exigences **et** cohérence de la matrice `specs/traceability.md`, que le test ne vérifie pas), puis `/code-review high` sur le diff du bloc.
- Avant livraison : `/spec-check`, puis `/security-review`.
- Fin du bloc 4 (premier bloc qui expose l'API du panier) : `/security-review`.
- Chaque constat est **corrigé**, **rejeté avec justification** ou **reporté** (ajouté à un `tasks.md`), puis consigné dans `docs/ai-reviews.md`. Aucune correction appliquée sans être comprise.
- Les propositions de l'IA modifiées ou rejetées par le candidat sont consignées dans `docs/ai-journal.md` ; les plus significatives sont reprises dans le bilan `docs/AI_USAGE.md`.

## Conventions
- **Langue** : seul le code source est en anglais (types, membres, variables, routes, tables et colonnes, valeurs `code` des erreurs). **Tout le reste est en français** : commentaires, messages d'API (`title`, `detail`, validation), logs, noms lisibles des tests, documentation.
- **Architecture** : `Shop.Domain` (pur, sans dépendance) ← `Shop.Application` (cas d'usage, ports par cas d'usage) ← `Shop.Infrastructure` / `Shop.Api`. Organisation par fonctionnalité (`Catalog/`, `Carts/`). **Ni MediatR, ni repository générique.**
- **Erreurs** : cas métier = `Result` / `DomainError` (pas d'exception) ; sortie HTTP = ProblemDetails avec une extension `code` (table dans `specs/platform/design.md` §5).
- **Temps** : toujours `TimeProvider` injecté, toujours UTC.
- Versions de paquets centralisées dans `Directory.Packages.props` (ligne 10.0.x pour Microsoft, xunit.v3 3.x).

## Invariants de concurrence — à ne jamais casser
1. Toute transaction qui modifie un panier existant commence par `SELECT Id FROM Carts WITH (UPDLOCK, ROWLOCK)`, PUIS charge les lignes (deux instructions). Aucune écriture sur `CartLines` ne contourne ce verrou. Création de panier optimiste (index unique + reprise), jamais de `HOLDLOCK` (verrou de plage sur le panier voisin).
2. Un conflit ne se détecte que sur la **version de la ligne**, jamais sur la version globale du panier.
3. Ordre des verrous : `Carts` → `Products`. La synchro ne verrouille que `Products`.
4. Les commandes de panier **ne sont pas rejouées automatiquement** (risque de double application si l'accusé de commit est perdu).
5. Synchro : verrou distribué `catalog-sync`, instantané complet **avant** toute écriture, application en **une seule transaction**.
6. Aucune règle métier ne repose sur la mémoire d'un processus : les instances sont sans état.

## Pièges connus
Erreurs réellement rencontrées sur ce projet (détails dans `docs/ai-reviews.md` et `docs/verification-manuelle.md`). Chacune a coûté du temps : ne pas les réintroduire.

**Base de données et concurrence**
- **`HOLDLOCK` sur une clé absente** verrouille aussi la clé suivante de l'index, c'est-à-dire le panier d'un autre utilisateur. Création de panier : index unique + reprise de la commande.
- **RCSI** : une lecture sans verrou voit l'instantané de *son* instruction. Verrouiller et charger dans la même instruction peut donc lire un état périmé : verrou d'abord, chargement ensuite.
- **Rejeu automatique d'une commande** (`TransientRetry`, Polly, `proxy_next_upstream` de nginx) : si l'accusé de commit est perdu, la commande est appliquée deux fois. Rejeu réservé aux opérations idempotentes ; côté client, c'est la clé d'idempotence (CART-R18), enregistrée **dans la même transaction** que la commande.
- **Verrou `sp_getapplock` perdu sans que l'instance le sache** (connexion coupée) : une seconde synchro peut démarrer. Toujours écrire sous la barrière de la ligne `SyncRuns` (`WHERE Status = Running`, dans la transaction d'application).
- **Suppression en masse** (purge) en une seule instruction : escalade en verrou de table qui bloque les commandes. Supprimer par lots (`DELETE TOP (n)`, n < 5 000).
- **Une reprise sur violation d'unicité ne suffit pas** quand deux contraintes peuvent entrer en collision successivement (panier, puis clé d'idempotence) : compter les reprises nécessaires.

**HTTP, fournisseur, proxy**
- **Le `Timeout` de `HttpClient` ne protège pas d'un corps de réponse figé** : la synchro restait bloquée indéfiniment. Délai par page (`CatalogSync:PageTimeout`) qui couvre la lecture du corps.
- **nginx dans Docker** : le résolveur musl ne renvoie qu'une adresse (→ `resolver 127.0.0.11` + variable pour une résolution dynamique) ; le relais est en HTTP/1.0 par défaut (→ `proxy_http_version 1.1`, sinon un `POST` sans corps est refusé) ; `proxy_next_upstream off` sur `/api/cart`.
- **`AddStandardResilienceHandler(section)`** lève une exception si la section est vide : utiliser la surcharge avec action (`options => section.Bind(options)`).
- **Validation par expression régulière** : `$` accepte un `\n` final. Toujours ancrer avec `\A…\z`.
- **JSON du curseur** : fixer les noms avec `JsonPropertyName`. Sans eux, la casse produite à la sérialisation ne correspondait pas au format documenté, et la désérialisation, sensible à la casse, refusait un curseur valide.

**Plateforme .NET**
- **`InvariantGlobalization`** est incompatible avec `Microsoft.Data.SqlClient` : ne pas l'activer.
- **`WebApplicationFactory`** : la configuration de test n'est appliquée qu'à la construction de l'hôte. Un service qui lit la configuration à l'enregistrement (`InstanceIdentity`) doit être une fabrique différée.

**Tests**
- **`FakeTimeProvider` ne recule pas** : les scénarios fixent des instants arbitraires, d'où `TestClock` (minuteries en temps réel).
- **Doubles de test globaux** (intercepteurs EF) partagés par les 2 instances et par les services d'arrière-plan : un commit de la synchro peut consommer une panne armée pour le panier. Filtrer sur le contexte concerné.
- **Nouveau `.feature`** : il doit être ajouté au `.csproj` de `Shop.AcceptanceTests`, sinon il n'est pas exécuté (`TraceabilityTests` le détecte). Le code généré par Reqnroll va dans `obj/` (`ReqnrollUseIntermediateOutputPathForCodeBehind`), jamais dans `specs/`.
- **Tests par mutation** : une mutation qui rend une condition constante est refusée par le compilateur (`TreatWarningsAsErrors`) ; muter avec une condition non triviale.

**Outillage (Windows)**
- Pas de Python sur le poste. `sed`/`perl` sur des `.csproj` ou des modifications multilignes ont déjà corrompu des fichiers (échappement) : utiliser l'éditeur, puis relire le diff.
- Docker Desktop doit être démarré et à jour pour `dotnet test` (Testcontainers).

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

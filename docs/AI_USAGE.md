# Bilan de l'usage de l'IA

## Outils et démarche

- **Claude Code** (extension VS Code), seul outil IA : comparaison d'options, specs, code et tests, diagnostic, revues `/code-review` et `/security-review`. Ni serveur MCP ni hook.
- **Spec-driven development** : exigences EARS et critères Gherkin rédigés **avant** le code (`specs/`), puis exécutés par Reqnroll : la spec ne peut pas diverger du code.
- **Validation humaine à chaque phase** (exigences, design, tâches, chaque bloc d'implémentation). L'IA propose des options argumentées ; le candidat challenge, tranche et valide chaque commit.
- **Boucle de revue** : `/code-review high` à la fin de chaque bloc, `/security-review` après le bloc panier et avant livraison. Chaque constat est corrigé, rejeté avec justification ou reporté, et consigné dans [ai-reviews.md](ai-reviews.md).

## Outillage fourni

| Fichier | Rôle |
|---|---|
| [AGENTS.md](../AGENTS.md), et [CLAUDE.md](../CLAUDE.md) qui y renvoie | Workflow SDD, boucle de revue, conventions, **invariants de concurrence**, **pièges connus**, interdits |
| [/spec-task](../.claude/commands/spec-task.md) | **Seule définition** du déroulé d'une tâche : exigences et scénarios, code, tests tracés, matrice |
| [/spec-check](../.claude/commands/spec-check.md) | Expliquer le résultat de `TraceabilityTests`, qui fait foi |
| [specs/](../specs/) | Contexte permanent de l'agent : constitution, exigences, designs, tâches |

**Décalage relevé à la relecture finale** : `/spec-task` devait servir à chaque tâche, mais elle n'a jamais été invoquée. L'agent a suivi le même déroulé, recopié dans `AGENTS.md`. Le résultat est le même tant que les deux textes restent identiques, mais rien n'empêchait qu'ils divergent. Correction pour les évolutions suivantes : le déroulé n'est plus défini que dans `spec-task.md`, vers lequel `AGENTS.md` et les `tasks.md` renvoient. L'agent (`/spec-task <id>`) et le candidat suivent ainsi le même fichier. Son étape de clôture précise désormais de reporter **tous** les scénarios et tests dans la matrice, puis de lancer `/spec-check`.

## Vérifications

- **331 tests automatisés** : 181 unitaires, 32 d'intégration (SQL Server réel, 2 instances), 118 scénarios d'acceptation. `TraceabilityTests` garantit que chaque exigence est couverte et que chaque `.feature` est exécuté.
- **Mutations manuelles** : seuil du garde-fou non strict, version du **panier** comparée au lieu de celle de la ligne, retour de `HOLDLOCK`, délai par page retiré, clé d'idempotence ignorée. Chacune fait échouer au moins un test.
- **Concurrence testée de façon déterministe**, en plus du stress répété : attente du verrou, panier voisin non bloqué, run de synchronisation repris, session SQL tuée. Plan d'exécution de la pagination vérifié par un test.
- **Vérification manuelle de bout en bout** (docker compose, 2 instances, vrai DummyJSON, [verification-manuelle.md](verification-manuelle.md)) : 3 défauts qu'aucun test ni aucune revue n'avait vus, dont une synchronisation bloquée indéfiniment.
- **Revues IA** : 58 constats, 50 corrigés, 3 corrigés en partie, 2 reportés ou documentés, 3 rejetés avec justification. Aucune vulnérabilité haute ou moyenne.

## Propositions de l'IA validées, adaptées ou rejetées

Sélection ; le journal complet (18 entrées) est dans [ai-journal.md](ai-journal.md), dont il garde la numérotation.

| # | Proposition de l'IA | Décision | Résultat |
|---|---|---|---|
| 1 | Verrou distribué de la synchronisation **écrit à la main** (`sp_getapplock`). | **Adaptée** : le candidat préfère une bibliothèque éprouvée à du code de verrou à écrire et à tester. | `DistributedLock.SqlServer` et `Cronos`. |
| 6 | Migrations **au démarrage de chaque instance**. | **Corrigée** : la question du candidat a révélé que l'activation de RCSI exige d'être la seule connexion. | Migrateur dédié, exécuté seul avant les instances. |
| 8 | Ajout sans panier existant → `404`. | **Rejetée** : le candidat préfère une création implicite. | CART-R15 et CART-R1.3 (un seul panier lors de premiers ajouts simultanés). |
| 12 | Création du panier protégée par **`HOLDLOCK`**. | **Rejetée après revue** : le verrou de plage bloquait le panier de l'utilisateur voisin. | Création optimiste ; un test échoue si `HOLDLOCK` revient. |
| 13 | Affirmation : sous RCSI, verrouiller et lire dans **une même requête** relirait un état périmé. | **Infirmée par un test** sur SQL Server 2022. | Deux instructions conservées par prudence, mais le défaut n'est plus présenté comme certain. |
| 14 | Délais de résilience HTTP jugés suffisants pour borner la synchronisation. | **Infirmée par la vérification manuelle** : la lecture du corps n'était pas couverte. | Délai par page, prouvé par un test. |
| 15 | **Clé d'idempotence** enregistrée dans la même transaction que la commande. | **Validée** après examen du design détaillé par le candidat. | CART-R18 ; l'accusé de commit perdu est simulé par un test. |

**À retenir pour l'entretien** : l'IA a accéléré l'exploration des options, la rédaction et les tests. Les défauts les plus sérieux ont pourtant été trouvés par les questions du candidat (n° 6, et n° 9 et 10 du journal), par des revues dédiées (n° 12, verrou de synchronisation perdu sans barrière) et par l'exécution réelle (n° 14). Les affirmations de l'IA ont été vérifiées par des tests plutôt que crues sur parole (n° 13).

# Journal des revues IA

> Une entrée par revue (`/code-review`, `/security-review`) à la fin de chaque bloc.
> Chaque constat est **corrigé**, **rejeté** (avec justification) ou **reporté** (ajouté à un `tasks.md`).

## Bloc 1 — Socle technique — `/code-review high` — 2026-11-16

Revue sur le diff indexé (67 fichiers). 10 constats, vérifiés un par un avant décision.

| # | Constat | Décision | Justification / correction |
|---|---|---|---|
| 1 | `X-Instance` posé avant `UseExceptionHandler` : le gestionnaire vide les en-têtes, donc les réponses 500/503 n'indiquent plus l'instance (PLT-1). | **Corrigé** | En-tête posé dans `Response.OnStarting`. Test de non-régression à ajouter au bloc 4, avec le premier endpoint capable de produire un 503 (panne de base simulée). |
| 2 | Le challenge 401 utilise `IProblemDetailsService.WriteAsync`, qui lève une exception si le client n'accepte pas le JSON : le 401 devient un 500. | **Corrigé** | `TryWriteAsync` : 401 sans corps dans ce cas. Test de non-régression (`Accept: text/plain`) au bloc 4, avec les endpoints panier. |
| 3 | Remise à zéro des tests : `DBCC CHECKIDENT RESEED 0` donne un premier identifiant à 0 sur une table jamais alimentée et à 1 sinon, donc des identifiants dépendants de l'ordre des tests. | **Corrigé** | Reseed seulement si `sys.identity_columns.last_value` n'est pas nul. |
| 4 | Tâche T1.5 « stratégie de rejeu » cochée sans implémentation. | **Reporté** | Aucun consommateur encore : la tâche est décochée et reportée à T3.3 (synchro) et T4.2 (création du panier, lectures), où elle sera testée. |
| 5 | `TraceabilityTests` compte les tags de `.feature` pas encore exécutés : une exigence paraît couverte sans test qui s'exécute. | **Rejeté** | Choix assumé et documenté (inclusion progressive des `.feature`, `specs/platform/tasks.md`) : le contrôle d'exécution de tous les `.feature` est un test explicitement ignoré jusqu'à T6.4, puis bloquant. Exiger l'exécution dès maintenant rendrait la suite rouge jusqu'au bloc 4. |
| 6 | Détection des secrets dans `docker-compose` : sensible à la casse ; rate `Password=…` en fin de chaîne de connexion ; faux positif sur `Password=${VAR};…=true`. | **Corrigé** | Deux règles (mot de passe en ligne et clé sensible) insensibles à la casse, qui ignorent les variables `${…}`. Ajout d'un test paramétré couvrant exactement les cas signalés. |
| 7 | Épuisement du pool de connexions (`InvalidOperationException` sans exception interne) classé comme non transitoire : 500 au lieu de 503. | **Corrigé** | Reconnu par son message (seul indice fourni par SqlClient), commenté. |
| 8 | `BadHttpRequestException` toujours renvoyée en 400 : perte des statuts 413, 408 et 431. | **Corrigé** | Le statut porté par l'exception est conservé ; le code reste `validation_failed`. |
| 9 | Le design annonce un port `ICurrentUser`, le code fournit une extension `User.GetUserId()`. | **Corrigé (spec)** | Le code est plus simple : l'utilisateur est **passé en paramètre** aux services d'Application (cas d'usage explicites, testables sans HTTP). `platform/design.md` §4 et la tâche T1.4 sont alignés sur le code. |
| 10 | Le contrôle RCSI (`IHostedService.StartAsync`) retarde le démarrage de l'instance si la base est lente ou injoignable. | **Corrigé** | Transformé en `BackgroundService` qui cède immédiatement la main. |

**Résultat après corrections** : build sans avertissement ; 75 tests unitaires et 5 tests d'intégration verts (1 test ignoré volontairement jusqu'à T6.4).

## Bloc 2 — Domaine — `/code-review high` — 2026-11-16

Revue sur le diff indexé (20 fichiers : agrégat panier, domaine de la synchro, politiques, curseur). 10 constats.

| # | Constat | Décision | Justification / correction |
|---|---|---|---|
| 1 | `RetryDelay` : `15 min × 2^30` dépasse `TimeSpan.MaxValue` → `OverflowException` à chaque tick après 31 échecs consécutifs (~2,5 jours de panne) : le planificateur ne repartirait plus seul. | **Corrigé** | Doublement itératif comparé au plafond avant de doubler. Test : 5, 31, 64 et `int.MaxValue` échecs → 2 h, sans exception. |
| 2 | Curseur sérialisé en `{"V":…,"Id":…}` au lieu du format documenté `{"v":…,"id":…}` ; les tests de rejet passaient pour une mauvaise raison (casse). | **Corrigé** | `JsonPropertyName("v"/"id")`. Test du format documenté exact (décodage et encodage identiques). |
| 3 | La validation de l'instantané ne vérifie ni les textes nuls ni leur longueur : un produit invalide passerait et ferait échouer l'écriture en base à chaque synchro. | **Corrigé** | Longueurs partagées avec le schéma (`Product.Max*Length`), valeurs nulles refusées : `InvalidSnapshot` avant toute écriture. |
| 4 | Prix à plus de 2 décimales : stocké arrondi (`decimal(18,2)`), puis jugé différent à chaque synchro (fausse mise à jour quotidienne). | **Corrigé** | Prix arrondi à 2 décimales dans l'instantané. Test d'idempotence avec 9.999. |
| 5 | Fenêtre fixe de 35 jours : un cron rare (au-delà d'un mois) ne redeviendrait jamais dû. | **Corrigé** | Fenêtres croissantes (1 h, 2 j, 400 j, 5 ans). Test avec un cron annuel (29 février). |
| 6 | Tâches T2.x cochées sans mise à jour de `traceability.md`. | **Corrigé** | Nouveau statut 🟡 (règles testées unitairement, intégration à venir) ; matrice mise à jour. |
| 7 | Le design annonce `ApplyFrom → ProductChange`, absent du code. | **Corrigé (spec)** | La décision insertion/mise à jour/réactivation/retrait appartient à `SyncPlanner` ; design et tâche alignés sur le code. |
| 8 | `Cart.Lines` renvoie la `List` interne : un appelant peut la reconvertir et modifier les lignes hors de l'agrégat. | **Corrigé** | Vue `ReadOnlyCollection` créée une fois dans le constructeur. |
| 9 | Coût : énumération de toutes les occurrences sur 35 jours à chaque tick (cron fréquent). | **Corrigé** | Couvert par la correction 5 (la plus petite fenêtre suffit). |
| 10 | `AddItem(productId, product?)` : paramètre redondant et exception si incohérents ; le design annonce `AddItem(product?)`. | **Rejeté** | L'identifiant demandé est nécessaire pour signaler un produit inexistant (`product` est alors nul) ; l'incohérence entre les deux est une erreur de programmation, d'où l'exception. Le design est aligné sur cette signature. |

**Résultat après corrections** : build sans avertissement ; 169 tests unitaires et 5 tests d'intégration verts.

**Correction de spec relevée pendant l'implémentation (hors revue)** : le scénario « Le planificateur rattrape une synchronisation échouée » (échec à 02:01, vérification à 02:15) contredisait l'espacement de 15 min ajouté ensuite (CAT-R2.4) ; l'échec est placé à 02:00.

## Bloc 3 — Catalogue — `/code-review high` — 2026-11-16

Revue sur le diff indexé (42 fichiers : synchronisation, fournisseur, verrou, planificateur, endpoints, scénarios). 10 constats.

| # | Constat | Décision | Justification / correction |
|---|---|---|---|
| 1 | Après le commit du catalogue, l'écriture du run « réussi » est séparée et non protégée : si elle échoue, le client reçoit 503 « aucune modification » alors que le catalogue a changé, et le run sera déclaré interrompu. | **Corrigé** | La réussite du run est écrite **dans la transaction du catalogue** (`ExecuteUpdate` avant le commit). |
| 2 | Erreur du fournisseur enveloppée deux fois (message dupliqué) et `reason` toujours `provider_error`, même pour un instantané invalide. | **Corrigé** | L'erreur du fournisseur est renvoyée telle quelle (motif `invalid_snapshot` conservé). |
| 3 | **Le verrou n'est pas une barrière** : la perte du verrou n'est détectée qu'au keepalive (jusqu'à 30 s) ; une autre instance peut reprendre le verrou, déclarer le run interrompu et appliquer, pendant que la première valide aussi (et réécrit « réussi » par-dessus « interrompu »). | **Corrigé** | Barrière sur la ligne du run (`UPDLOCK`) au début de la transaction d'application ; interruption des orphelins et fins de run en mises à jour ensemblistes conditionnelles (`WHERE Status = Running`). Deux tests d'intégration : application abandonnée si le run a été repris ; run terminé jamais réécrit. Design §3 mis à jour. |
| 4 | Une annulation due à la perte du verrou peut lever une `SqlException` (et non `OperationCanceledException`) : le run serait classé « erreur de base » sans log `CatalogSyncLockLost`. | **Corrigé** | Le filtre « verrou perdu » s'applique à toute exception. |
| 5 | Sur perte du verrou, message « la base a échoué pendant l'application », même pendant la récupération des produits. | **Corrigé** | Erreur dédiée `SyncInterrupted` (« verrou de synchronisation perdu »). |
| 6 | Pipeline de résilience aux valeurs par défaut de la bibliothèque (30 s au total) alors que le design prévoit 10 s par tentative et 60 s au total. | **Corrigé** | Valeurs du design posées dans le code, surchargeables par `CatalogSync:Http`. |
| 7 | Délai de rejeu `Task.Delay` sans `TimeProvider`, contraire à la convention du dépôt. | **Corrigé** | `Task.Delay(delay, TimeProvider, …)` ; l'horloge de test (`TestClock`) garde des minuteries réelles. |
| 8 | `GetStatusAsync` fait 3 allers-retours séquentiels dont une requête en double. | **Rejeté** | Endpoint d'administration rarement appelé, requêtes indexées sur une petite table : la clarté de requêtes indépendantes prime sur un gain négligeable. |
| 9 | Ré-emballage fragile de l'erreur de l'instantané, fondé sur le texte du message. | **Corrigé** | Le résultat de `CatalogSnapshot.Create` est renvoyé directement. |
| 10 | Identifiants de log 1006–1008 (et 2003, 9001) absents du registre `LogEvents` et sans `EventName`. | **Corrigé** | Registre complété et commenté (plages par domaine) ; tous les `[LoggerMessage]` utilisent `EventName = nameof(LogEvents.X)`. |

**Résultat après corrections** : build sans avertissement ; 169 tests unitaires, 16 tests d'intégration et 50 scénarios d'acceptation verts.

**Changements de design décidés pendant l'implémentation (hors revue)** :
- Index `IX_Products_Status_Id` retiré : non couvrant, il aurait imposé une recherche par ligne ; la pagination utilise la clé primaire clusterisée (vérifié par un test du plan d'exécution).
- Rejeu idempotent par une boucle explicite `TransientRetry` sur un contexte EF neuf à chaque tentative, plutôt que `SqlServerRetryingExecutionStrategy` (qui, sur un contexte unique, rejouerait sur des entités déjà modifiées en mémoire).
- Horloge de test `TestClock` réglable dans les deux sens (`FakeTimeProvider` interdit de revenir en arrière, ce que les scénarios du planificateur exigent).
- Code généré par Reqnroll redirigé vers `obj/` (`ReqnrollUseIntermediateOutputPathForCodeBehind`) pour garder `specs/` propre.
- Vérification par mutation : rendre le seuil du garde-fou non strict (`>=`) fait échouer 2 scénarios.

## Bloc 4 — Panier — `/code-review high` — 2026-11-16

Revue sur le diff indexé (24 fichiers : transaction du panier, lecture, service, endpoints, scénarios). 10 constats.

| # | Constat | Décision | Justification / correction |
|---|---|---|---|
| 1 | Ordre des lignes différent entre les réponses des commandes et `GET /api/cart` (tri par GUID côté SQL, ordre d'insertion côté mémoire). | **Corrigé** | Ordre stable unique (nom conservé, puis identifiant) dans `CartView.From`. |
| 2 | **`HOLDLOCK` sur une clé absente** pose un verrou de plage qui couvre aussi la clé SUIVANTE de l'index : la première création du panier d'Alice bloque le panier de Bob, y compris pendant l'attente d'un produit verrouillé par une synchronisation. | **Corrigé** | Création **optimiste** : insertion arbitrée par l'index unique, reprise complète de la commande après une collision (rien n'a été validé). Test déterministe : Bob n'est pas bloqué ; **avec `HOLDLOCK`, ce test échoue** (vérifié). Design §3, AGENTS.md mis à jour. |
| 3 | `GetOrCreateAsync` rejoué par `TransientRetry` : après un commit dont l'accusé est perdu, répondrait 200 au lieu de 201. | **Corrigé** | Plus de rejeu automatique pour la création ; collision → panier existant (200). |
| 4 | `ChangeQuantity` lit et verrouille toujours le produit, même pour une baisse ou une version périmée (attente inutile d'une synchronisation). | **Corrigé** | Lecture du produit seulement si une augmentation est possible (ligne présente, version à jour, quantité supérieure). |
| 5 | Vue de la réponse construite avant le commit : verrous tenus plus longtemps. | **Corrigé** | Vue construite après le commit. |
| 6 | Validation écrite à la main alors que `AddValidation()` est enregistré ; message de quantité dupliqué avec le domaine. | **Rejeté en partie** | Validation explicite conservée (messages et format `errors` maîtrisés) ; `AddValidation()` inutilisé retiré ; message unique partagé (`CartRules.QuantityRangeMessage`). |
| 7 | Deux définitions des montants et de la disponibilité (projection SQL de la lecture, agrégat pour les commandes). | **Corrigé** | La lecture charge l'agrégat et passe par la même construction `CartView.From`. |
| 8 | `CartCommandOutcome.Created/Change` jamais lus. | **Corrigé** | `ExecuteAsync` renvoie directement `Result<CartView>`. |
| 9 | Tâches T4.2/T4.3 cochées avec des noms et comportements périmés. | **Corrigé** | Tâches et design alignés sur le code. |
| 10 | `Guid.CreateVersion7()` lit l'horloge système au lieu du `TimeProvider` injecté. | **Corrigé** | `Guid.CreateVersion7(clock.GetUtcNow())`. |

**Hypothèse de design vérifiée puis nuancée (hors revue)** : le design initial verrouillait le panier et chargeait ses lignes dans une même requête. J'ai supposé que, sous RCSI, une requête qui attend le verrou relirait les lignes d'avant le commit concurrent, et j'ai séparé verrou et lecture. Un test déterministe (transaction concurrente qui tient le verrou et modifie la ligne) montre que **la variante en une requête passe aussi** sur SQL Server 2022 avec le plan observé. Les deux instructions sont conservées (aucune dépendance à un comportement non documenté), mais les commentaires et le design ne présentent plus le défaut comme certain.

**Vérifications par mutation** : comparer la version du panier au lieu de celle de la ligne fait échouer les 2 scénarios d'Alice ; remettre `HOLDLOCK` fait échouer le test de non-blocage du panier voisin.

**Résultat après corrections** : build sans avertissement ; 170 tests unitaires (dont le contrôle d'exécution de tous les `.feature`, activé), 30 tests d'intégration et 108 scénarios d'acceptation verts.

## Blocs 1 à 4 — `/security-review` — 2026-11-16

Revue de sécurité de toute la branche (commits des blocs 1 à 3 et changements indexés du bloc 4) : injection SQL, isolation des paniers, identification, clé d'administration, fuite d'informations, validation, secrets, données du fournisseur.

**Aucune vulnérabilité de gravité haute ou moyenne.** SQL brut toujours paramétré (`SqlQuery(FormattableString)`), paniers isolés par construction (aucun identifiant de panier dans les routes, lignes cherchées dans le panier de l'utilisateur courant), clé d'administration comparée en temps constant, erreurs génériques sans détail interne.

**Limites assumées à documenter dans DECISIONS.md** : `X-User-Id` n'est pas une authentification (usurpation possible, assumée par le sujet ; remplacement par JWT sans toucher aux endpoints) ; clé d'administration statique et partagée (évolution : rôle porté par un fournisseur d'identité).

## Bloc 5 — Exécution locale — `/code-review high` — 2026-11-16

Revue sur le diff indexé (Dockerfile, docker-compose, nginx, `.env.example`, `.http`). 8 constats.

| # | Constat | Décision | Justification / correction |
|---|---|---|---|
| 1 | **nginx rejoue PUT et DELETE** sur l'autre instance après une erreur ou un délai : une commande de panier déjà validée reçoit un 409/404 trompeur (contraire à l'invariant n° 4). | **Corrigé** | `proxy_next_upstream off` pour `/api/cart` ; comportement par défaut conservé ailleurs (le POST de synchronisation n'est jamais rejoué). |
| 2 | SQL Server publié sur toutes les interfaces avec `sa` et le mot de passe d'exemple : accessible depuis le réseau local. | **Corrigé** | Port lié à `127.0.0.1`. |
| 3 | Un `docker compose up` sur une pile en marche relance le migrateur pendant que les anciennes instances tournent. | **Documenté** | Procédure explicite dans `docker-compose.yml` (arrêter les instances avant une nouvelle migration) ; en production, la migration est une étape du pipeline de déploiement. Migrations « expand/contract » en évolution. |
| 4 | Clé d'administration du `.http` figée sur la valeur d'exemple. | **Corrigé en partie** | Avertissement explicite en tête du fichier (doit égaler `ADMIN_API_KEY` du `.env`). |
| 5 | Pas de contrôle de santé ni de politique de redémarrage pour `api`. | **Corrigé en partie** | `restart: unless-stopped` ajouté. Contrôle de santé non ajouté : l'image d'exécution ne contient aucun client HTTP (en ajouter un élargit la surface d'attaque) ; en production, l'orchestrateur sonde `/health`. |
| 6 | Mot de passe `sa` passé en argument de `sqlcmd` (visible dans la liste des processus du conteneur). | **Corrigé** | Variable `SQLCMDPASSWORD`. |
| 7 | Image construite deux fois (migrateur et instances). | **Corrigé** | Seul le migrateur construit ; `api` réutilise l'image (`pull_policy: never`). |
| 8 | En-têtes `X-Forwarded-*` envoyés mais jamais exploités par l'API. | **Corrigé** | En-têtes retirés (aucun besoin de l'adresse du client dans cette version). |

**Trouvés pendant la vérification manuelle (hors revue)**, détails dans [verification-manuelle.md](verification-manuelle.md) :
- répartition inopérante (résolution DNS musl à une seule adresse) → résolution dynamique nginx ;
- `POST` sans corps refusé (nginx en HTTP/1.0) → `proxy_http_version 1.1` ;
- **synchronisation bloquée indéfiniment par un corps de réponse figé** → délai par page, prouvé par un test qui reste bloqué sans le correctif.

**Résultat** : build sans avertissement ; 170 tests unitaires, 31 tests d'intégration et 108 scénarios d'acceptation verts ; pile docker compose vérifiée de bout en bout.

## Livraison (blocs 1 à 6) — `/security-review` final — 2026-11-16

Revue de toute la branche, y compris conteneurisation, nginx et documentation : secrets versionnés, exposition réseau, conteneur, configuration du proxy, administration, fournisseur, fuite d'information.

**Aucune vulnérabilité de gravité haute ou moyenne.** Secrets uniquement via `.env` (non versionné, vérifié par test), SQL Server limité à `127.0.0.1`, instances joignables seulement via nginx, conteneur non root, mot de passe hors de la ligne de commande, commandes du panier jamais rejouées par le proxy.

**Points d'attention non bloquants (documentés)** : valeurs d'exemple de `.env.example` réservées au poste local ; `TrustServerCertificate=true` acceptable sur le réseau Docker local mais pas en production ; documentation OpenAPI exposée dans compose pour la démonstration (désactivée par défaut hors Development).

## Bloc 7 — Clé d'idempotence — `/code-review high` — 2026-11-16

Revue sur le diff de la clé d'idempotence (CART-R18) : transaction de commande, rejeu, purge, en-têtes, tests. 10 constats.

| # | Constat | Décision | Justification / correction |
|---|---|---|---|
| 1 | Purge des clés expirées en **une seule** suppression : sur un gros volume, escalade en verrou de table qui bloque les commandes du panier, et risque d'interblocage avec la suppression d'une clé expirée réutilisée. | **Corrigé** | Suppression par lots de 1 000 (`DELETE TOP`), transactions courtes ; un interblocage éventuel est absorbé (purge reprise au tick suivant). Test d'intégration sur plusieurs lots. |
| 2 | Le message du 503 ne disait pas au client qu'il peut réessayer sans risque. | **Corrigé** | Le message indique que l'opération a pu être enregistrée et invite à renvoyer la même demande avec la même `Idempotency-Key`. |
| 3 | Sans clé, la vue du panier était construite **avant** le commit : verrous conservés plus longtemps sans raison. | **Corrigé** | Vue construite après le commit quand aucune clé n'est fournie ; avant le commit seulement si elle doit être enregistrée avec la clé. |
| 4 | Une seule reprise sur violation d'unicité : une collision sur la création du panier **puis** sur la clé fait échouer la commande en 500. | **Corrigé** | 3 tentatives (deux contraintes successives possibles), justifié en commentaire. |
| 5 | La clé était vérifiée **après** le chargement du panier : chargement inutile pour un rejeu, et création implicite préparée pour rien. | **Corrigé** | Verrou du panier, puis vérification de la clé, puis seulement chargement ou création (méthodes `LockAsync` / `LoadOrCreateAsync`). |
| 6 | L'intercepteur « accusé de commit perdu » était global : un commit concurrent (synchronisation, planificateur) pouvait consommer l'armement et rendre le test aléatoire. | **Corrigé** | Il ne se déclenche que pour un contexte qui suit un panier ou une clé d'idempotence. |
| 7 | Noms d'en-têtes codés en dur dans les tests. | **Corrigé** | Constantes `IdempotencyKeyFormat.HeaderName` et `ReplayedHeaderName`. |
| 8 | Conversion implicite inutilisée. | **Corrigé** | Retirée. |
| 9 | Configuration EF de `IdempotencyKeys` rangée avec l'entité, et non avec les autres configurations. | **Corrigé** | Déplacée dans `Persistence/Configurations/Carts`. |
| 10 | Étapes de test qui lisaient le panier en ignorant le résultat (`_ = await …OrNull`) pour un effet de bord (utilisateur courant). | **Corrigé** | Méthode explicite `FocusOnAsync`. |

**Résultat après corrections** : build sans avertissement ; 181 tests unitaires, 32 tests d'intégration et 118 scénarios d'acceptation verts.

## Bloc 7 et simplifications — `/security-review` — 2026-11-17

Revue de toute la branche, centrée sur la nouvelle surface : clé d'idempotence (en-tête, réponse enregistrée puis rejouée, purge) et liste des produits renvoyant directement la vue de lecture.

**Aucune vulnérabilité de gravité haute ou moyenne.** Clé propre à chaque utilisateur (clé primaire `(UserId, Key)`, collation binaire, utilisateur toujours issu de l'identification), clé validée avant usage (expression régulière ancrée, longueur alignée sur la colonne), clé réutilisée pour une autre requête refusée sans divulguer la réponse enregistrée, SQL brut toujours paramétré, désérialisation `System.Text.Json` vers un type fermé (aucun polymorphisme), 503 sans détail interne.

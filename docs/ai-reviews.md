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

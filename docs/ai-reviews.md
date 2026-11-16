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

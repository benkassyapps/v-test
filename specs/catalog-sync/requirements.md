# Exigences — Catalogue et synchronisation (`CAT`)

> Format : exigences **EARS** (« Le système DOIT… », « QUAND…, le système DOIT… », « SI…, ALORS le système DOIT… »).
> Les critères d'acceptation exécutables sont dans [`acceptance/`](acceptance/) et portent le tag de l'exigence (`@CAT-R4`…).
> Vocabulaire : voir [`../glossary.md`](../glossary.md).

## User stories

- **US-CAT-1** — En tant qu'**exploitant**, je veux que le catalogue local soit synchronisé automatiquement chaque jour avec le fournisseur, afin que les produits proposés soient à jour sans intervention.
- **US-CAT-2** — En tant qu'**exploitant**, je veux déclencher une synchronisation manuellement et consulter l'historique des exécutions, afin de vérifier qu'elle fonctionne.
- **US-CAT-3** — En tant que **client de l'API**, je veux lister les produits disponibles page par page, afin de les présenter et de les ajouter à un panier, même si le fournisseur est indisponible.

## Exigences

### CAT-R1 — Récupération complète
1. QUAND une synchronisation s'exécute, le système DOIT récupérer l'ensemble des produits du fournisseur en parcourant toutes ses pages jusqu'au total annoncé.
2. SI l'instantané récupéré est incomplet (nombre de produits distincts différent du total annoncé, page vide prématurée, identifiant en double), ALORS le système DOIT considérer la synchronisation comme échouée et ne rien modifier au catalogue.

### CAT-R2 — Synchronisation automatique quotidienne
1. Le système DOIT exécuter automatiquement une synchronisation par créneau quotidien. Le créneau est configurable par une expression cron (par défaut tous les jours à 02:00 UTC).
2. QUAND le créneau du jour est échu et qu'aucune synchronisation n'a réussi depuis ce créneau, le système DOIT lancer une synchronisation (`Scheduled`), y compris après un arrêt de toutes les instances ou un échec précédent (rattrapage).
3. QUAND une synchronisation a déjà réussi depuis le dernier créneau échu, le planificateur NE DOIT PAS en lancer une autre.
4. QUAND des synchronisations planifiées ont échoué consécutivement (échec ou blocage par le garde-fou), le planificateur DOIT espacer progressivement les nouvelles tentatives : 15 min après le 1er échec, puis 30 min, 1 h, et au plus 2 h entre deux tentatives (valeurs configurables). Une synchronisation réussie, manuelle ou planifiée, remet ce compteur à zéro (Décision D10).

### CAT-R3 — Déclenchement manuel et vérification
1. QUAND un exploitant authentifié par la clé d'administration (en-tête `X-Admin-Key`) appelle `POST /api/admin/catalog/sync`, le système DOIT exécuter immédiatement une synchronisation (`Manual`), même si une synchronisation a déjà réussi pour le créneau du jour.
2. La synchronisation manuelle DOIT suivre exactement le même traitement que la synchronisation planifiée (même verrou, même transaction, même gestion d'échec).
3. QUAND la synchronisation manuelle se termine, le système DOIT renvoyer le résumé du run : identifiant, statut, déclencheur, instance, dates de début et de fin, nombre de produits récupérés, insérés, mis à jour, retirés et réactivés, et message d'erreur éventuel.
4. SI la clé d'administration est absente ou invalide, ALORS le système DOIT refuser la demande (`403`) sans lancer de synchronisation.
5. Le système DOIT permettre de consulter l'historique des runs, manuels et planifiés, du plus récent au plus ancien (`GET /api/admin/catalog/sync-runs`).
6. Le déclenchement manuel DOIT accepter une option de forçage (`?force=true`) qui lève le garde-fou de retrait massif (CAT-R11). Le run DOIT enregistrer que le forçage a été utilisé.

### CAT-R4 — Ajout et mise à jour sans doublon
1. QUAND une synchronisation s'applique, le système DOIT créer les produits dont l'identifiant fournisseur est inconnu et mettre à jour ceux qui existent déjà.
2. Le système DOIT garantir qu'un identifiant fournisseur correspond à **au plus un** produit local, y compris en cas d'exécutions répétées.
3. QUAND une synchronisation reçoit des données identiques à celles déjà stockées, le système NE DOIT modifier aucun produit (exécution idempotente : 0 insertion, 0 mise à jour).

### CAT-R5 — Informations conservées
1. Le système DOIT conserver pour chaque produit : son identifiant fournisseur, son nom, sa catégorie, son prix, sa disponibilité indicative (stock et statut de disponibilité annoncés) et sa date de dernière mise à jour.
2. La date de dernière mise à jour DOIT correspondre au dernier changement effectif des informations du produit dans le catalogue local (Décision D4).

### CAT-R6 — Liste paginée des produits
1. Le système DOIT exposer `GET /api/products` qui liste les **produits actifs** du catalogue local, triés par identifiant produit, avec une **pagination par curseur** (`limit`, `cursor`).
2. `limit` DOIT être compris entre 1 et 100 (20 par défaut). SI `limit` est hors bornes ou si le curseur est invalide, ALORS le système DOIT répondre `400`.
3. La réponse DOIT contenir les produits de la page, le curseur de la page suivante et l'indicateur `hasMore`.
4. QUAND des produits sont ajoutés ou retirés entre deux pages, la suite du parcours NE DOIT ni répéter un produit déjà renvoyé ni sauter un produit resté actif.
5. Le système NE DOIT proposer aucune recherche ni aucun filtre sur les produits.

### CAT-R7 — Indépendance vis-à-vis du fournisseur
1. La consultation des produits DOIT s'appuyer uniquement sur le catalogue local, sans appel au fournisseur.
2. TANT QUE le fournisseur est indisponible, le système DOIT continuer à servir les produits déjà synchronisés et les paniers.

### CAT-R8 — Cohérence en cas d'échec et rattrapage
1. SI une synchronisation échoue ou s'interrompt à n'importe quelle étape (fournisseur indisponible, réponse invalide, erreur de base, arrêt de l'instance), ALORS le catalogue local DOIT rester exactement dans son état précédent (aucune application partielle).
2. QUAND une synchronisation échoue, le système DOIT enregistrer le run avec le statut `Failed` et la cause de l'échec, sans exposer de détail sensible.
3. QUAND une synchronisation ultérieure réussit, le catalogue local DOIT refléter l'instantané complet du fournisseur, quel que soit le nombre d'échecs précédents.

### CAT-R9 — Produits qui ne sont plus présents chez le fournisseur
1. QUAND une synchronisation réussie ne contient plus un produit actif, le système DOIT le marquer comme **retiré** sans le supprimer.
2. Un produit retiré NE DOIT plus apparaître dans la liste des produits ni pouvoir être ajouté à un panier comme nouvel article (voir CART-R11).
3. Les lignes de panier qui contiennent un produit retiré DOIVENT rester intactes et consultables.
4. QUAND un produit retiré réapparaît chez le fournisseur, le système DOIT le réactiver et mettre à jour ses informations.

### CAT-R10 — Exécution exclusive, y compris en multi-instance
1. Le système DOIT garantir qu'au plus une synchronisation s'exécute à un instant donné, toutes instances confondues.
2. QUAND une synchronisation est demandée alors qu'une autre est en cours : SI la demande est planifiée, ALORS le run DOIT être ignoré (`Skipped`) ; SI elle est manuelle, ALORS le système DOIT répondre `409` (`sync_already_running`).
3. QUAND plusieurs instances détectent en même temps que le créneau du jour est échu, une seule synchronisation planifiée DOIT être appliquée pour ce créneau.
4. SI l'instance qui exécute une synchronisation s'arrête brutalement, ALORS le verrou DOIT être libéré automatiquement afin qu'une exécution ultérieure puisse avoir lieu.

### CAT-R11 — Garde-fou contre le retrait massif
1. SI une synchronisation retirerait plus d'une proportion configurable des produits actifs (`CatalogSync:MaxDiscontinuedRatio`, 50 % par défaut), ALORS le système DOIT la considérer comme échouée, ne rien modifier au catalogue et enregistrer la cause (nombre de produits qui auraient été retirés).
2. Un fournisseur qui renvoie un catalogue vide alors que le catalogue local contient des produits actifs DOIT être traité comme un cas de ce garde-fou (100 % de retrait).
3. Le garde-fou NE DOIT PAS s'appliquer lorsque le catalogue local ne contient aucun produit actif (première synchronisation).
4. QUAND un exploitant déclenche manuellement une synchronisation avec forçage (CAT-R3.6), le système DOIT appliquer le retrait quelle que soit sa proportion.
5. Le planificateur NE DOIT jamais forcer une synchronisation.
6. QUAND le garde-fou bloque une synchronisation manuelle, le système DOIT répondre `422` (`sync_guard_triggered`) en indiquant le nombre de produits concernés et la possibilité de forcer.

### CAT-R12 — Signalement des anomalies de synchronisation à l'exploitant
1. QUAND une synchronisation est bloquée par le garde-fou, le système DOIT émettre un log de niveau `Error` avec l'identifiant d'événement stable `CatalogSyncGuardTriggered` et les valeurs utiles au diagnostic (produits actifs, produits qui seraient retirés, seuil, déclencheur, instance).
2. QUAND une synchronisation échoue pour une autre raison, le système DOIT émettre un log de niveau `Error` avec l'identifiant d'événement stable `CatalogSyncFailed` et la cause.
3. Le système DOIT exposer à l'exploitant (clé d'administration) l'état de la synchronisation (`GET /api/admin/catalog/sync-status`) : dernière synchronisation réussie et son âge, dernier run (statut, déclencheur, cause), nombre d'échecs consécutifs, prochaine tentative planifiée.
4. L'état DOIT indiquer `actionRequired: true`, avec la raison et l'action conseillée, QUAND le dernier run a été bloqué par le garde-fou, ou QUAND aucune synchronisation n'a réussi depuis plus d'une durée configurable (`CatalogSync:StaleAfter`, 26 h par défaut). Sinon, `actionRequired` DOIT valoir `false`.
5. L'action conseillée pour un blocage par le garde-fou DOIT inviter à vérifier le fournisseur avant de forcer la synchronisation (CAT-R3.6).

## Décisions (ambiguïtés du sujet tranchées, à valider)

| # | Question | Décision | Justification |
|---|---|---|---|
| D1 | Que faire des produits absents du fournisseur ? | Retrait logique (`Discontinued`), jamais de suppression physique ; réactivation s'ils reviennent. | Les paniers référencent le produit (clé étrangère) : il doit rester en base. Le retrait est réversible. |
| D2 | Les produits retirés apparaissent-ils dans la liste ? | Non, seuls les produits actifs sont listés. | La liste sert à choisir un produit à ajouter ; un produit retiré n'est plus ajoutable. |
| D3 | Que faire si une synchronisation retirerait une grande partie du catalogue, voire tout (catalogue vide) ? | Échec au-delà d'un seuil configurable (50 % des produits actifs par défaut), catalogue inchangé. Un exploitant peut forcer manuellement (`?force=true`) ; le planificateur ne force jamais (CAT-R11). | Une réponse vide ou très réduite est bien plus probablement une anomalie (changement de format, panne renvoyée en `200`, mauvaise configuration) qu'un vrai retrait. Coûts asymétriques : un faux retrait vide la boutique jusqu'à 24 h ; un refus à tort laisse seulement des produits affichés un peu plus longtemps (sans réservation de stock). Un retrait massif est une décision métier qui revient à un humain. *Généralisation du refus du seul catalogue vide, décidée après challenge du candidat.* |
| D4 | Que signifie « date de dernière mise à jour » ? | Date du dernier changement effectif des informations du produit dans notre catalogue (`LastUpdatedAt`). La date de dernière observation lors d'une synchro (`LastSyncedAt`) et la date annoncée par le fournisseur (`meta.updatedAt`) sont conservées en plus. | Distinguer « la donnée a changé » de « la donnée a été vérifiée » est utile pour l'exploitation. |
| D5 | Le déclenchement manuel est-il synchrone ? | Oui : la réponse contient le résumé du run. | Le catalogue fait ~200 produits (quelques secondes). Un mode asynchrone (`202` + suivi) est une évolution si le volume grandit. |
| D6 | Comment protéger le déclenchement manuel ? | Clé d'administration dans l'en-tête `X-Admin-Key`, définie par configuration. | Simple et suffisant pour l'exercice ; un rôle dans un vrai fournisseur d'identité est l'évolution naturelle. |
| D7 | Pagination ? | Par curseur (keyset) sur l'identifiant produit, sans nombre total de pages. | Stable pendant une synchronisation et à coût constant ; pas de frontend demandant d'accéder directement à la page N. |
| D9 | Comment l'exploitant apprend-il qu'une synchronisation est bloquée ou en retard ? | Logs `Error` avec des identifiants d'événement stables, sur lesquels brancher une alerte, et endpoint `sync-status` qui indique `actionRequired`, la raison et l'action conseillée (CAT-R12). | Le log déclenche l'alerte dans n'importe quel outil de logs ; l'endpoint donne le diagnostic et l'action à mener. La fraîcheur du catalogue (`StaleAfter`) couvre aussi les causes non prévues. *Écartés pour cette version :* health check `Degraded` (choix du candidat), métriques OpenTelemetry et notifications (e-mail, Slack), qui relèvent de l'outil de supervision et sont listés en évolutions. |
| D10 | À quel rythme relancer après un échec ? | Espacement progressif : 15 min, 30 min, 1 h, puis 2 h au maximum (configurable). | Conserve la guérison automatique si l'anomalie du fournisseur est passagère, sans produire ~96 runs en échec par jour ni solliciter le fournisseur inutilement. |
| D8 | Devise des prix ? | Non gérée : prix en décimal à 2 décimales, dans la devise implicite du fournisseur. | DummyJSON ne fournit pas de devise. |

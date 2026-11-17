# Vérification manuelle multi-instance (PLT-4, tâche T5.2)

> Seule exigence non couverte par un test automatisé : le démarrage reproductible avec `docker compose`.
> Vérification réalisée le 2026-11-16 sur Docker Desktop 29.9.3, avec le **vrai DummyJSON**.

## Procédure

```bash
cp .env.example .env
docker compose up --build -d          # sqlserver → migrator → 2 × api → nginx (:8080)
```

## Résultats observés

| # | Vérification | Commande (résumé) | Résultat |
|---|---|---|---|
| 1 | Ordre de démarrage | `docker compose ps -a` | `sqlserver` sain → `migrator` terminé avec le code 0 (3 migrations appliquées seul) → 2 conteneurs `api` → `nginx`. ✅ |
| 2 | Répartition entre instances | `curl -i :8080/health` (×6) | `X-Instance` alterne entre les 2 conteneurs. ✅ |
| 3 | Synchronisation de première mise en service, multi-instance | `GET /api/admin/catalog/sync-runs` | Instance A : run `Scheduled` `Succeeded`, **194 produits insérés** depuis DummyJSON. Instance B, au même moment : run `Scheduled` `Skipped` (verrou détenu par A). ✅ |
| 4 | Idempotence | `POST /api/admin/catalog/sync` | 194 récupérés, **0 inséré, 0 mis à jour**, 0,5 s. ✅ |
| 5 | Protection de l'administration | même appel sans `X-Admin-Key` | `403 admin_key_invalid` (ProblemDetails). ✅ |
| 6 | Pagination | `GET /api/products?limit=2` | 2 produits, `nextCursor`, `hasMore: true`. ✅ |
| 7 | **Exemple d'Alice**, deux onglets, lignes différentes | panier en v4, puis 2 `PUT` simultanés (A et B) depuis la même lecture | Les deux en **200**, servis par **deux instances différentes** ; panier en **v6**. ✅ |
| 8 | Même ligne, même version | 2 `PUT` simultanés sur A avec `expectedVersion` 3 | Un **200**, un **409** `cart_line_version_conflict`. ✅ |
| 9 | Panne du fournisseur | `CATALOG_BASE_URL=http://fournisseur-inexistant.invalid docker compose up -d api` puis synchronisation manuelle | `502 provider_unavailable` (« Le catalogue local est inchangé ») ; **liste des produits toujours servie (200)** ; **ajout au panier possible (200)** ; `sync-status` : dernier run `Failed / ProviderError`, aucune action requise (réussite récente). ✅ |
| 10 | Retour à la normale | `docker compose up -d api` puis synchronisation | `Succeeded`, 194 récupérés, 0 changement. ✅ |

## Problèmes trouvés et corrigés pendant cette vérification

| Problème | Cause | Correction |
|---|---|---|
| Toutes les requêtes servies par la même instance | La résolution système de l'image nginx Alpine (musl) ne renvoie qu'**une** adresse pour `api`, alors que le DNS de Docker en annonce deux. | Résolution dynamique : `resolver 127.0.0.11` + `server api:8080 resolve` (nginx ≥ 1.27.3), réévaluée toutes les 10 s. |
| `POST /api/admin/catalog/sync` sans corps → `400` vide, jamais vu par l'application | nginx relaie en HTTP/1.0 par défaut ; Kestrel refuse un `POST` HTTP/1.0 sans `Content-Length`. | `proxy_http_version 1.1` dans `deploy/nginx.conf`. |
| Logs très bruyants (chaque commande SQL et chaque appel HTTP) | Niveaux par défaut d'EF Core, de HttpClient et de Polly. | Niveaux `Warning` pour ces catégories dans `appsettings.json`. |

## Limite constatée
Après la recréation des conteneurs `api` (nouvelles adresses IP), nginx peut répondre `502` pendant au plus 10 s, le temps de rafraîchir sa résolution DNS (`valid=10s`). En production, un orchestrateur avec sondes de disponibilité et retrait progressif des instances (Kubernetes, par exemple) évite cette fenêtre.

## Défaut grave trouvé par cette vérification : synchronisation bloquée indéfiniment

**Symptôme.** Juste après un redémarrage des instances, une synchronisation manuelle est restée « en cours » plus de 3 minutes (nginx répond 504 au bout de 120 s ; les demandes suivantes reçoivent 409, verrou détenu). Côté SQL Server : aucune requête bloquée, aucune transaction ouverte. Logs : 1re tentative vers DummyJSON expirée à 10 s, nouvelle tentative, puis plus rien, malgré le délai global de 60 s du pipeline de résilience.

**Cause.** `GetFromJsonAsync` rend la main dès réception des **en-têtes** ; la lecture du **corps** se fait ensuite, hors du pipeline de résilience, dont les délais ne s'appliquent donc pas. Si le fournisseur envoie les en-têtes puis cesse d'émettre (connexion figée), la lecture attend indéfiniment, verrou détenu.

**Correction.** Délai par page couvrant aussi la lecture du corps (`CatalogSync:PageTimeout`, 30 s par défaut).

**Preuve.** Test d'intégration `Stalled_response_body_fails_within_page_timeout` : le faux fournisseur envoie les en-têtes de la page 2 puis un corps qui n'arrive jamais. Avec le correctif : échec propre en ~5 s (run `Failed / ProviderError`, catalogue intact). **Sans le correctif, le test reste bloqué** (arrêté par `--blame-hang-timeout`).

**Effet de bord vérifié.** Après le redémarrage des instances, la synchronisation suivante a marqué le run bloqué `Failed / Interrupted` (reprise des runs orphelins, CAT-R10.4).

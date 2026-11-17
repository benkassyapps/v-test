# Exigences transverses — Plateforme (`PLT`)

> Critères d'acceptation exécutables : [`acceptance/`](acceptance/) (tags `@PLT-*`). Certaines exigences sont vérifiées par des tests techniques xUnit (`Trait("Spec", "PLT-*")`).

### PLT-1 — Fonctionnement multi-instance
1. Le système DOIT pouvoir s'exécuter en plusieurs instances simultanées partageant la même base SQL Server, sans configuration propre à une instance.
2. Les garanties métier (CART-R1 à CART-R12, CAT-R4, CAT-R8, CAT-R10) DOIVENT tenir lorsque les requêtes et les traitements sont répartis entre plusieurs instances.
3. Chaque réponse HTTP DOIT indiquer l'instance qui l'a traitée (en-tête `X-Instance`), à des fins de diagnostic.

### PLT-2 — Identification de l'utilisateur courant
1. Le système DOIT identifier l'utilisateur courant par l'en-tête `X-User-Id`.
2. L'identifiant DOIT comporter de 1 à 64 caractères parmi `A-Z a-z 0-9 . _ - @`. SI l'en-tête est absent, vide ou invalide sur une opération de panier, ALORS le système DOIT répondre `401`.
3. L'identification DOIT être isolée dans un composant d'authentification, afin de pouvoir être remplacée (ex. jeton JWT) sans modifier les opérations métier.
4. Cette identification n'est **pas** une authentification : elle est assumée comme limite de l'exercice et documentée.

### PLT-3 — Contrat d'erreurs HTTP
1. Toute erreur DOIT être renvoyée au format ProblemDetails (RFC 9457, `application/problem+json`) avec `type`, `title`, `status`, `detail` (en français) et une extension `code` stable.
2. Le système DOIT appliquer la table suivante :

| Statut | `code` | Situation |
|---|---|---|
| 400 | `validation_failed` | Entrée invalide (quantité hors bornes, version absente, `limit` hors bornes, curseur invalide…). L'extension `errors` détaille les champs. |
| 401 | `user_not_identified` | `X-User-Id` absent ou invalide. |
| 403 | `admin_key_invalid` | Clé d'administration absente ou invalide. |
| 404 | `cart_not_found` / `cart_line_not_found` | Panier ou ligne inexistants pour l'utilisateur courant. |
| 409 | `cart_line_version_conflict` | Version de ligne périmée ; l'extension `currentVersion` indique la version actuelle. |
| 409 | `sync_already_running` | Synchronisation déjà en cours. |
| 422 | `product_unavailable` / `quantity_limit_exceeded` | Règle métier non respectée. |
| 422 | `sync_guard_triggered` | Synchronisation manuelle bloquée par le garde-fou de retrait massif (CAT-R11) ; peut être forcée. |
| 422 | `idempotency_key_reused` | Clé d'idempotence déjà utilisée pour une requête différente (CART-R18). |
| 500 | `internal_error` | Erreur inattendue, sans détail interne. |
| 502 | `provider_unavailable` | Synchronisation manuelle en échec à cause du fournisseur. |
| 503 | `service_unavailable` | Base de données temporairement indisponible. |

3. Les réponses d'erreur NE DOIVENT contenir ni pile d'appels, ni SQL, ni information de configuration.

### PLT-4 — Démarrage local reproductible
1. Le système DOIT pouvoir être démarré localement avec ses dépendances par une seule commande (`docker compose up --build`) : SQL Server, migrateur, 2 instances de l'API et un répartiteur de charge.
2. Une configuration d'exemple (`.env.example`) DOIT être fournie, sans aucun secret réel.

### PLT-5 — Schéma de base et migrations
1. Le schéma DOIT être versionné par des migrations, appliquées par un **migrateur dédié** qui s'exécute seul avant le démarrage des instances.
2. Les lectures NE DOIVENT PAS être bloquées par les écritures en cours (READ_COMMITTED_SNAPSHOT activé). La correction des règles métier NE DOIT PAS dépendre de ce réglage.

### PLT-6 — Observabilité minimale
1. Le système DOIT produire des logs structurés (en français) pour chaque synchronisation (début, fin, compteurs, échec) et pour les erreurs inattendues. Les événements qui appellent une action de l'exploitant portent un identifiant d'événement stable (voir CAT-R12).
2. Le système DOIT exposer un endpoint de santé (`GET /health`) qui vérifie l'accès à la base.

### PLT-7 — Documentation de l'API
1. Le système DOIT publier une description OpenAPI de ses endpoints et une interface pour les explorer en développement.

### PLT-8 — Secrets
1. Aucun secret NE DOIT être présent dans le dépôt. Les valeurs sensibles (mot de passe de base, clé d'administration) DOIVENT être fournies par configuration (variables d'environnement ou user-secrets).

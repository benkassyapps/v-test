# Test technique Senior .NET — Catalogue de produits et panier personnel

> Les outils d’intelligence artificielle sont **autorisés et encouragés**. Vous restez responsable de la qualité et du fonctionnement de la solution livrée.

## Contexte

Vous concevez un nouveau service pour un produit e-commerce destiné à **évoluer dans la durée**. L'application doit maintenir localement un catalogue de produits provenant d'un fournisseur externe et permettre à chaque utilisateur de gérer son panier personnel.

Il ne s'agit **pas d'un POC**. Votre réalisation doit pouvoir être maintenue, testée et enrichie au fil des évolutions fonctionnelles. **Aucun code de départ n'est fourni** : vous êtes responsable de la conception et de la réalisation de la solution.

L’application doit être **prête à fonctionner dans un environnement distribué**, avec une montée en charge horizontale sur **plusieurs instances applicatives**. Les règles métier et la cohérence des données doivent rester garanties lorsque les requêtes et les traitements s’exécutent simultanément sur différentes instances. Il n’est pas demandé de déployer un cluster.

### Usage de l'IA

Vous pouvez utiliser librement **Copilot, Codex, Claude Code, ChatGPT ou d'autres outils IA** pour réfléchir, générer du code, tester ou documenter. C'est même encouragé. L'usage de l'IA n'est pas pénalisé. En revanche, vous restez responsable de tout le code livré et devrez être capable d'en expliquer les choix, les limites et le comportement en cas d'incident ou de concurrence.

Nous évaluerons le résultat, les vérifications effectuées et votre capacité à justifier vos décisions, pas la proportion de code écrit manuellement. **Si vous utilisez des outils IA, fournissez les éléments de configuration et d’accompagnement que vous avez mis en place dans le dépôt** (par exemple `AGENTS.md`, instructions d’agents, skills, configuration de serveurs MCP, hooks ou autres fichiers pertinents), ainsi qu’une brève explication de leur rôle. Il n’est pas nécessaire d’en créer artificiellement si vous n’en avez pas eu l’usage. Ne fournissez jamais de secrets ou de jetons d’accès.

## 1. Gestion du catalogue

Le fournisseur de produits est **DummyJSON** : https://dummyjson.com/docs/products.

### Besoins métier

- Récupérer **l'ensemble des produits** disponibles auprès du fournisseur et les conserver localement.
- Effectuer une synchronisation **automatique quotidienne** ; permettre également son déclenchement manuel pour vérifier son fonctionnement.
- Ajouter les nouveaux produits et actualiser ceux qui existent déjà sans créer de doublons.
- Conserver les informations utiles à la consultation et au panier : identifiant du produit chez le fournisseur, nom, catégorie, prix, disponibilité indicative et date de dernière mise à jour.
- Mettre à disposition une API permettant **uniquement de lister les produits locaux avec pagination**. **Aucune recherche produit** n'est demandée.
- Un incident chez le fournisseur ne doit pas empêcher l'accès aux produits déjà synchronisés.
- Si une synchronisation échoue ou s'interrompt, les données doivent rester cohérentes et une exécution ultérieure doit pouvoir rétablir la situation.
- Définir un comportement cohérent pour les produits qui ne sont plus présents chez le fournisseur, sans rendre inutilisables les paniers qui les contiennent déjà.
- Éviter qu'une exécution simultanée ou répétée de la synchronisation compromette la cohérence du catalogue, y compris dans un environnement multi-instance.

La disponibilité indiquée par DummyJSON est une information de catalogue : **aucune réservation de stock** n'est attendue dans cet exercice.

## 2. Panier personnel

Chaque utilisateur possède **au maximum un panier personnel**. Les paniers ne sont pas partagés entre utilisateurs. La gestion complète de l'authentification ne fait pas partie du sujet : vous pouvez définir et documenter une manière simple de représenter l'utilisateur courant dans le cadre de l'exercice.

### Opérations attendues

| Opération | Besoin métier |
|---|---|
| Créer / récupérer son panier | S'il existe déjà, retourner le panier existant : **création idempotente**, y compris en cas d'appels simultanés |
| Consulter son panier | Retourner les articles, leurs quantités, prix, montant total et informations de version |
| Ajouter un article | Ajouter un produit disponible dans le catalogue local et la quantité demandée |
| Modifier une quantité | **Remplacer** la quantité d'une ligne par la nouvelle quantité demandée |
| Supprimer un article | Retirer la ligne du panier |

### Règles métier

1. Un utilisateur ne peut pas créer plusieurs paniers par répétition d'une même demande, même lorsque les appels arrivent en parallèle.
2. Un panier appartient à un seul utilisateur. Un utilisateur ne peut ni consulter ni modifier le panier d'un autre.
3. Un article ne peut être ajouté que s'il correspond à un produit existant et actif dans le catalogue local.
4. La quantité d'une ligne est un entier compris entre **1 et 999**. Un ajout dépassant cette limite est refusé sans altérer le panier.
5. Un même produit ne peut figurer qu'**une seule fois** dans un panier. Réajouter ce produit **augmente** la quantité existante ; plusieurs ajouts simultanés doivent conserver toutes les quantités acceptées.
6. Le nom et le prix du produit sont conservés lors de son premier ajout. Si son prix ou son nom change ensuite dans le catalogue, les lignes déjà présentes restent inchangées. Le montant du panier est calculé à partir de ces valeurs conservées.
7. Le panier possède une **version globale**, initialisée à `0`. Chaque modification effective et acceptée de son contenu augmente cette version d'**exactement 1**. Une demande rejetée ne doit rien modifier.
8. Chaque ligne de panier possède aussi une **version propre**. Pour modifier ou supprimer une ligne, le client doit fournir la version précédemment consultée. Si cette ligne a changé depuis, la demande doit être rejetée avec un conflit explicite : aucune modification concurrente ne doit être écrasée silencieusement.
9. Deux modifications simultanées portant sur **des lignes différentes** doivent pouvoir réussir toutes les deux, sans conflit injustifié, même si la version globale du panier évolue entre les deux demandes.
10. Une ligne supprimée ne doit pas pouvoir réapparaître à la suite d'une modification concurrente devenue obsolète.
11. Un produit retiré du catalogue reste consultable dans un panier existant ; il ne peut plus être ajouté comme nouvel article.
12. En cas d'échec au cours d'une opération, l'état du panier, de ses lignes et de ses versions doit rester cohérent.

### Exemple de concurrence

Alice possède un panier de version **4**, avec deux articles A et B. Elle ouvre ce panier dans deux onglets :

- Depuis l'onglet 1, elle change la quantité de l'article A.
- Depuis l'onglet 2, elle change la quantité de l'article B, à partir de la même lecture initiale.
- **Les deux modifications doivent pouvoir réussir**. La version du panier passe alors à **6**.
- En revanche, si deux requêtes tentent de modifier A à partir de la **même ancienne version de la ligne A**, au plus une modification peut être acceptée ; l'autre reçoit un conflit.

Ces garanties doivent tenir **même quand les requêtes sont traitées par des instances différentes**.

## 3. Périmètre et contraintes

- Solution backend en **C# / .NET 10**, avec API HTTP.
- Les données du catalogue et des paniers sont conservées dans **une même base de données SQL Server**.
- Le traitement de synchronisation quotidien fait partie du produit livré.
- Le système doit pouvoir fonctionner correctement avec **plusieurs instances en exécution**. Il n'est pas nécessaire de fournir une infrastructure de production.
- Pas de frontend, pas de partage de panier, pas de recherche produit, pas de paiement, pas de commande et pas de réservation de stock.
- Vous êtes **libre de choisir l'architecture, l'organisation du code, les composants et les bibliothèques**. Leurs avantages, coûts et limites doivent pouvoir être expliqués.
- Les choix relatifs aux erreurs HTTP, aux réponses de conflit et à l'identification de l'utilisateur doivent être cohérents et documentés.

## 4. Livrables

1. **Le code source complet** du service et du traitement quotidien.
2. **Un moyen reproductible de démarrer localement l’application et ses dépendances**, accompagné de la configuration d’exemple nécessaire.
3. **`README.md`** expliquant comment démarrer, lancer les tests, exécuter une synchronisation manuelle et appeler les principales opérations de l'API.
4. **Des tests automatisés** démontrant les principales règles métier, les conflits entre modifications simultanées et le comportement en cas d'échec de synchronisation.
5. **`DECISIONS.md`** (maximum 2 pages) présentant l'architecture retenue, les décisions importantes, les compromis, les limites et les évolutions possibles du produit.
6. **Si des outils IA ont été utilisés : un court bilan** précisant les outils employés, les vérifications effectuées et un exemple de proposition validée, adaptée ou rejetée. Inclure les fichiers réellement utilisés pour guider ou outiller les agents (`AGENTS.md`, skills, configuration MCP, etc.), sans secrets ni historique complet des conversations.

## 5. Restitution et discussion

Lors d'un échange technique, vous présenterez l'organisation de la solution et expliquerez comment elle répond aux besoins, notamment lorsque plusieurs instances ou requêtes agissent en parallèle. Nous discuterons aussi de sa maintenance, de ses limites et de son évolution si le catalogue, le nombre d'utilisateurs ou les équipes grandissent.

**Le critère principal n'est pas le nombre de technologies utilisées.** Nous attendons une solution fonctionnelle, robuste, raisonnablement simple et conçue comme la première version d'un véritable produit.

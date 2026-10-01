# Page de détail d’un actif — V2

## Parcours

Dans **Assets**, cliquer sur une ligne (hors étoile) ouvre le détail dans la même fenêtre. Le bouton de retour ramène à la liste. Aucun nouvel onglet n’est ajouté au menu.

## Fichiers à lire dans cet ordre

1. `FAAH_Frontend/Views/AssetListView.axaml` : la ligne possède un bouton lié à `OpenAssetCommand`.
2. `FAAH_Frontend/ViewModels/AssetListViewModel.cs` : transmet l’actif sélectionné au Shell.
3. `FAAH_Frontend/ViewModels/ShellViewModel.cs`, `ShowAssetDetail` : remplace `CurrentPage` et transmet le client HTTP déjà authentifié et l’identifiant du compte.
4. `FAAH_Frontend/Views/AssetDetailView.axaml` : décrit l’affichage. Les `{Binding ...}` lisent le ViewModel.
5. `FAAH_Frontend/ViewModels/AssetDetailViewModel.cs` : charge les informations, prépare et confirme les opérations simulées.
6. `FAAH_Frontend/Controls/CandleChart.cs` : dessine les bougies. La ligne va du plus bas au plus haut ; le rectangle va de l’ouverture à la clôture.
7. `FAAH_Frontend/Models/Candle.cs` et `TradePortfolioResponse.cs` : représentent les réponses JSON utiles.

Pas de nouveau package graphique : le graphique utilise les outils de dessin d’Avalonia.

## Appels au backend actuel

| Besoin | Route |
|---|---|
| Cours de l’actif | `GET /api/assets/{symbol}/market` |
| Bougies | `GET /api/assets/{symbol}/candles?period=1d&interval=5m` |
| Actualités classées pour l'actif | `GET /api/assets/{symbol}/news` |
| Résumés des portefeuilles | `GET /api/users/{user_id}/portfolios` |
| Types autorisés et positions d'un portefeuille | `GET /api/users/{user_id}/portfolios/{portfolio_id}` |
| Achat simulé | `POST /api/users/{user_id}/portfolios/{portfolio_id}/assets/buy` |
| Vente simulée | `POST /api/users/{user_id}/portfolios/{portfolio_id}/assets/sell` |

L’achat envoie `symbol`, `quantity`, `purchase_price`. La vente envoie `symbol`, `quantity`, `sale_price`. Le backend enregistre l’opération ; le frontend ne fait aucun INSERT et n’affiche un succès qu’après une réponse valide.

La page recharge ses données toutes les 60 secondes, ou avec **Actualiser**. En quittant la page, le timer et les lectures en cours sont arrêtés. Il s’agit de cours yfinance, pas d’une garantie de temps réel.

Les métadonnées (nom, type, place boursière, secteur…) proviennent de l’actif déjà chargé dans la liste.

## Achat et vente

Depuis le changement du backend du 30 septembre, la liste fournit `portfolio_id` et `status`, sans positions. `LoadPortfoliosAsync` lit les résumés, puis charge un par un les détails des portefeuilles actifs en USD. Les détails servent au filtre de type et au calcul de la quantité détenue. Si un détail ne peut pas être lu, les transactions restent désactivées jusqu'à une actualisation réussie.

1. Sélectionner un portefeuille puis cliquer sur **Acheter** ou **Vendre**.
2. Saisir une quantité positive dans le formulaire intégré à la page.
3. Vérifier le portefeuille et l'estimation affichée. Le serveur détermine le prix réellement utilisé.
4. Cliquer sur **Confirmer la simulation**.

La cotation utilisée doit dater de moins de deux minutes depuis sa réception. Si le formulaire expire, l’annuler, actualiser et le rouvrir. Aucun POST automatique après un timeout : l’opération pourrait déjà être enregistrée. Vérifier l’historique dans ce cas.

Le sélecteur ne propose que les portefeuilles actifs en USD qui acceptent le type de l'actif. Une liste `preferred_asset_types` vide signifie tous les types. Les niches et le niveau de risque ne sont pas des restrictions de type. Aucun portefeuille n'est sélectionné automatiquement.

`CanBuy` exige une sélection et un cours récent ; `CanSell` exige aussi une quantité détenue positive. La quantité vendue ne peut pas dépasser `HeldQuantity`. Changer de portefeuille ferme le formulaire ; pendant l'envoi, le choix est verrouillé. La réponse du serveur actualise les quantités détenues. Les portefeuilles sont relus lors de l'actualisation de la page.

Le backend contrôle aussi le type autorisé et l'état du portefeuille dans `portfolio/repository.py` (`check_trade_portfolio`). Déployer cette modification pour bénéficier du contrôle côté serveur. Pas de migration SQL ni de nouvelle dépendance.

## Limites connues à présenter honnêtement

- Actualités : le backend suit les liens `classification_assets` → `source_classifications` → `data_sources`. Une actualité liée apparaît même sans le nom de l'actif dans son texte ; une simple mention sans lien en base ne suffit pas. Les doublons de classification sont supprimés côté backend. Il n'y a plus de filtre textuel ni de limite de 20 articles côté frontend.
- Déploiement : la nouvelle route `/api/assets/{symbol}/news` doit être déployée sur le serveur utilisé par Avalonia. Une ancienne version du serveur renverra une erreur 404 ; le frontend ne revient pas à l'ancien filtre textuel.
- Graphique : période et durée de bougie sélectionnables, heures UTC, sans zoom interactif. Les choix viennent de `GET /api/history-options` ; le défaut reste une journée avec des bougies de cinq minutes. Changer de période adapte automatiquement les intervalles disponibles. Une réponse ancienne est ignorée si le choix a changé entre-temps.
- Le backend actuel enregistre les transactions en USD : la simulation est bloquée pour les autres devises, sans inventer une conversion.
- Le backend contrôle le solde disponible du compte et récupère le prix d'exécution. Le montant du formulaire reste une estimation. Ce parcours est destiné aux simulations, pas à des transactions réelles.
- **Sécurité backend à compléter avant mise en service** : les routes de portefeuille fournies ne vérifient pas que `user_id` correspond au propriétaire du token. Le frontend transmet le compte connecté mais cela ne remplace pas une autorisation côté serveur. Les accès concurrents aux positions doivent aussi être sécurisés côté backend.
- La quantité renvoyée par le backend apparaît dans le message de confirmation et dans la quantité détenue du portefeuille sélectionné.

## Vérification

Compilation : `dotnet build FAAH_Frontend/FAAH_Frontend.csproj`

Tests sans opérations sur un vrai compte : `dotnet run --project tests/AssetDetailChecks`

Les tests utilisent un serveur HTTP simulé et les vrais fichiers AXAML : navigation, bougies, news, achats/ventes, refus, double clic et erreurs. Ils ne remplacent pas un essai avec le backend déployé, la base de données et un compte de test.

Le serveur reste celui configuré dans `ShellViewModel` (`FAAH_API_URL`, sinon l’adresse existante). Pour tester sur la VM, définir cette variable avant de lancer l’application, avec l’adresse et le port réellement utilisés.

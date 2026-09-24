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
| Actualités disponibles | `GET /api/data-sources` |
| Achat simulé | `POST /api/users/{user_id}/portfolio/assets/buy` |
| Vente simulée | `POST /api/users/{user_id}/portfolio/assets/sell` |

L’achat envoie `symbol`, `quantity`, `purchase_price`. La vente envoie `symbol`, `quantity`, `sale_price`. Le backend enregistre l’opération ; le frontend ne fait aucun INSERT et n’affiche un succès qu’après une réponse valide.

La page recharge ses données toutes les 60 secondes, ou avec **Actualiser**. En quittant la page, le timer et les lectures en cours sont arrêtés. Il s’agit de cours yfinance, pas d’une garantie de temps réel.

Les métadonnées (nom, type, place boursière, secteur…) proviennent de l’actif déjà chargé dans la liste.

## Achat et vente

1. Cliquer sur **Acheter** ou **Vendre**.
2. Saisir une quantité positive dans le formulaire intégré à la page.
3. Vérifier le prix de simulation affiché, figé à l’ouverture du formulaire.
4. Cliquer sur **Confirmer la simulation**.

La cotation utilisée doit dater de moins de deux minutes depuis sa réception. Si le formulaire expire, l’annuler, actualiser et le rouvrir. Aucun POST automatique après un timeout : l’opération pourrait déjà être enregistrée. Vérifier l’historique dans ce cas.

## Limites connues à présenter honnêtement

- Actualités : filtrage textuel sur le nom ou le symbole (et BTC pour BTC-USD), limité à 20 articles. Un symbole ambigu peut donner un faux positif ; une mention indirecte peut être manquée. Ce n’est pas encore une association fondée sur les classifications IA.
- Graphique : période et durée de bougie sélectionnables, heures UTC, sans zoom interactif. Les choix viennent de `GET /api/history-options` ; le défaut reste une journée avec des bougies de cinq minutes. Changer de période adapte automatiquement les intervalles disponibles. Une réponse ancienne est ignorée si le choix a changé entre-temps.
- Le backend actuel enregistre les transactions en USD : la simulation est bloquée pour les autres devises, sans inventer une conversion.
- Le backend ne contrôle pas encore un solde disponible et accepte le prix transmis par le client. Ce parcours est destiné aux simulations de développement, pas à des transactions réelles.
- **Sécurité backend à compléter avant mise en service** : les routes de portefeuille fournies ne vérifient pas que `user_id` correspond au propriétaire du token. Le frontend transmet le compte connecté mais cela ne remplace pas une autorisation côté serveur. Les accès concurrents aux positions doivent aussi être sécurisés côté backend.
- La page Portfolio existante utilise encore ses données de démonstration ; ce changement ne la remplace pas. La quantité renvoyée par le backend apparaît dans le message de confirmation du détail.

## Vérification

Compilation : `dotnet build FAAH_Frontend/FAAH_Frontend.csproj`

Tests sans opérations sur un vrai compte : `dotnet run --project tests/AssetDetailChecks`

Les tests utilisent un serveur HTTP simulé et les vrais fichiers AXAML : navigation, bougies, news, achats/ventes, refus, double clic et erreurs. Ils ne remplacent pas un essai avec le backend déployé, la base de données et un compte de test.

Le serveur reste celui configuré dans `ShellViewModel` (`FAAH_API_URL`, sinon l’adresse existante). Pour tester sur la VM, définir cette variable avant de lancer l’application, avec l’adresse et le port réellement utilisés.

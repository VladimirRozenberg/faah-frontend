# Recherche des actifs

- Saisir tout ou partie d'un symbole (`BTC`, `AAPL`) ou d'un nom (`Bitcoin`, `Apple`).
- Recherche automatique après 350 ms sans frappe. Entrée ou **Search** lance immédiatement la recherche. Effacer le texte retire ce critère (le bouton Clear a été supprimé).
- La recherche ignore les majuscules/minuscules et les espaces au début/à la fin.
- Les résultats sont paginés. Une nouvelle recherche repart de la page 1 ; actualiser ou changer de page conserve le filtre validé.

## Code à lire

1. `Views/AssetListView.axaml` : champ de saisie et boutons.
2. `ViewModels/AssetListViewModel.cs` : `SearchText` relance un petit timer à chaque frappe ; après 350 ms, `SearchAsync` valide le filtre ; `LoadAssetsAsync` envoie `search` au backend. Si un appel est en cours, la dernière recherche est mémorisée et lancée à sa fin. Une ancienne réponse n'est pas affichée si le texte a changé.
3. `Views/AssetListView.axaml.cs` : la touche Entrée appelle la même commande que le bouton.
4. Backend `routers/assets.py`, `list_assets` : recherche SQL sur le symbole OU le nom, avant le comptage et la pagination. Les favoris gardent leur priorité parmi les résultats.

Exemple : `GET /api/assets?page=1&page_size=20&search=apple`.

## Panneau de filtres

À droite : type (All, Stocks, Crypto, Forex, Futures), niche pour Stocks uniquement, devise, pays, place boursière et favoris. Le panneau reste toujours ouvert ; en petite fenêtre il se place au-dessus du tableau avec défilement. La recherche principale est limitée à 850 pixels.

Pays, devise et place boursière acceptent une partie du nom, sans distinction de casse : « uni » retrouve United States. Un champ vide ou le type All retire ce critère.

Le menu « Sector / niche » utilise les niches de la BDD via GET /api/niches (ce ne sont pas les secteurs Yahoo). Sa saisie réduit les choix du menu ; sélectionner un choix envoie niche_id au backend. All niches retire ce filtre. Il est ignoré pour les autres types d'actifs. Les choix sont chargés une seule fois par ouverture de page ; Refresh réessaie en cas d'échec.

`BuildFilterQuery` construit l'URL ; `FiltersChanged` relance la recherche à la page 1. Le compteur `_filterVersion` empêche une ancienne réponse de remplacer les nouveaux résultats. Le backend combine les critères avec la recherche et les favoris avant la pagination.

Exemple : `GET /api/assets?page=1&page_size=20&asset_type=crypto&currency=USD&favorites_only=true`.

La correction backend déclare les listes avec `Annotated[..., Query()]` pour que FastAPI lise ces filtres dans l'URL et non dans un corps JSON. Documentation : https://fastapi.tiangolo.com/tutorial/query-params-str-validations/#query-parameter-list-multiple-values

**Déployer également le backend modifié sur le cloud.** Relancer Avalonia ne déploie pas le backend. Une ancienne API peut ignorer `search` ; si elle renvoie des actifs sans rapport avec le texte, le frontend affiche une erreur demandant le déploiement, plutôt que la liste non filtrée. Aucun changement de schéma de BDD, aucune nouvelle bibliothèque.

Tests frontend : `dotnet run --project tests/SearchChecks -p:UsedAvaloniaProducts=`.
Tests backend (dans son environnement Python) : `python -m unittest discover -s tests -p test_asset_search.py -v`.

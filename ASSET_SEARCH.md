# Recherche des actifs

- Saisir tout ou partie d'un symbole (`BTC`, `AAPL`) ou d'un nom (`Bitcoin`, `Apple`).
- Recherche automatique après 350 ms sans frappe. Entrée ou **Search** lance immédiatement la recherche. **Clear** réaffiche tous les actifs.
- La recherche ignore les majuscules/minuscules et les espaces au début/à la fin.
- Les résultats sont paginés. Une nouvelle recherche repart de la page 1 ; actualiser ou changer de page conserve le filtre validé.

## Code à lire

1. `Views/AssetListView.axaml` : champ de saisie et boutons.
2. `ViewModels/AssetListViewModel.cs` : `SearchText` relance un petit timer à chaque frappe ; après 350 ms, `SearchAsync` valide le filtre ; `LoadAssetsAsync` envoie `search` au backend. Si un appel est en cours, la dernière recherche est mémorisée et lancée à sa fin. Une ancienne réponse n'est pas affichée si le texte a changé.
3. `Views/AssetListView.axaml.cs` : la touche Entrée appelle la même commande que le bouton.
4. Backend `routers/assets.py`, `list_assets` : recherche SQL sur le symbole OU le nom, avant le comptage et la pagination. Les favoris gardent leur priorité parmi les résultats.

Exemple : `GET /api/assets?page=1&page_size=20&search=apple`.

**Déployer également le backend modifié sur le cloud.** Relancer Avalonia ne déploie pas le backend. Une ancienne API peut ignorer `search` ; si elle renvoie des actifs sans rapport avec le texte, le frontend affiche une erreur demandant le déploiement, plutôt que la liste non filtrée. Aucun changement de schéma de BDD, aucune nouvelle bibliothèque.

Tests frontend : `dotnet run --project tests/SearchChecks -p:UsedAvaloniaProducts=`.
Tests backend (dans son environnement Python) : `python -m unittest discover -s tests -p test_asset_search.py -v`.

# Logos des actifs

Le backend récupère les logos chez Twelve Data et les garde en base. Le frontend ne contacte jamais Twelve Data : aucune clé API à ajouter dans Avalonia.

## Fonctionnement

1. La liste appelle déjà `GET /api/assets`. Chaque actif reçoit maintenant son `logo_url` (ou `null` si aucun logo n'est disponible).
2. Pour les actifs de la page visible, le frontend appelle `GET /api/assets/{symbol}/logo` avec son client HTTP habituel.
3. L'image reçue est affichée à la place des initiales. Pendant le chargement, ou en cas d'erreur, les initiales restent visibles.
4. Le même logo est réutilisé sur la page de détail.

## Fichiers à comprendre

- `FAAH_Frontend/Models/Asset.cs` : `LogoUrl` est l'adresse fournie par le backend ; `Logo` est l'image Avalonia ; `HasLogo` indique si elle peut être affichée.
- `FAAH_Frontend/Services/AssetLogoService.cs` : télécharge l'image et la garde en mémoire. Maximum trois téléchargements simultanés, dix secondes par téléchargement et un mégaoctet par image.
- `FAAH_Frontend/ViewModels/AssetListViewModel.cs` : `LoadVisibleLogosAsync` demande les logos des lignes visibles, sans bloquer le chargement des cours.
- `FAAH_Frontend/ViewModels/ShellViewModel.cs` : partage le service entre les pages pour conserver le cache.
- `FAAH_Frontend/Views/AssetListView.axaml` et `AssetDetailView.axaml` : les éléments `Image` affichent le logo ; les cercles avec les initiales sont le remplacement en cas d'absence.

Un logo téléchargé est conservé pour la session de l'application. Un échec est mémorisé deux minutes : une nouvelle demande après ce délai permet de réessayer. Il n'y a pas de boucle de téléchargement automatique.

## Vérification

L'API configurée dans `ShellViewModel` (ou via `FAAH_API_URL`) doit pointer vers le backend mis à jour. Si `logo_url` vaut `null`, le backend n'a pas encore de logo pour cet actif : les initiales sont alors normales.

Les tests de `tests/LogoChecks` utilisent une API simulée : affichage, cache, pagination, images absentes ou incorrectes, et changement de page pendant le téléchargement. Ils ne consomment pas le quota Twelve Data.

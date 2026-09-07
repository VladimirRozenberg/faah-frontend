# FAAH — Frontend Avalonia

Projet complet et exécutable, reprenant le design des maquettes.
Ciblé **Avalonia 12.0.0** et **.NET 8**.

## Avant d'ouvrir : videz le cache

Le previewer de Visual Studio garde une copie de l'ancienne build. Si vous aviez déjà
essayé une version précédente de ce projet, fermez Visual Studio et supprimez :

    %LOCALAPPDATA%\AvaloniaUI\com.AvaloniaUI.Net.VisualStudio\Previewer\*

Supprimez aussi les dossiers `bin` et `obj` s'ils existent.

## Lancer

Ouvrez `FAAH_Frontend.sln`, laissez la restauration NuGet se terminer,
puis Générer → Regénérer la solution, puis **F5**.

En ligne de commande : `dotnet run --project FAAH_Frontend`.

Connexion : n'importe quel identifiant et n'importe quel mot de passe, les deux champs
doivent seulement être non vides. L'authentification réelle se branche dans
`ShellViewModel.Login()`.

## Ce qui a été corrigé pour Avalonia 12

- Toutes les versions de paquets passent par la propriété `$(AvaloniaVersion)` : un seul
  endroit à modifier si vous changez de version.
- `Avalonia.Diagnostics` a été **supprimé** : ce paquet n'existe plus en v12. Si vous voulez
  les Dev Tools, il faut `AvaloniaUI.DiagnosticsSupport` et `AttachDeveloperTools()`.
- `Avalonia.Headless` et `Avalonia.Markup.Xaml.Loader` sont référencés explicitement.
  L'aperçu XAML en a besoin ; l'extension essaie de les injecter seule mais son appel
  MSBuild échoue avec `MSB1001`, d'où l'erreur `Could not load file or assembly
  'Avalonia.Headless'`.
- `AvaloniaUseCompiledBindingsByDefault` est forcé à `false`. La v12 l'active par défaut,
  or la liste des portefeuilles utilise `{Binding $parent[ItemsControl].DataContext.OpenCommand}`,
  qui demande un binding par réflexion.

## Navigation

Une seule fenêtre, `MainWindow`, qui contient la barre de navigation et un `ContentControl`.
`ShellViewModel.CurrentPage` porte la vue affichée ; toutes les pages sont des `UserControl`.

- Onglets 01 à 04 : `ShowPortfoliosCommand`, `ShowAssetsCommand`, `ShowBotsCommand`, `ShowNewsCommand`
- Menu de l'avatar : **Admin Console** ouvre la liste des utilisateurs, **Log out** revient à la connexion
- Un clic sur une ligne de la liste des portefeuilles ouvre le détail
- **New User** ouvre le formulaire, **Cancel** et **Create User** reviennent à la liste

La barre de navigation est masquée tant que `IsLoggedIn` est faux.

## Structure

    FAAH_Frontend/
      App.axaml              inclut FluentTheme puis Styles/FaahTheme.axaml
      Program.cs
      Styles/FaahTheme.axaml couleurs, typographie et classes utilitaires
      Models/                Asset, Portfolio, NewsArticle, User
      ViewModels/            ShellViewModel (navigation) + un VM par page
      Views/                 MainWindow + une UserControl par écran

## Données

Tout est alimenté par des collections en dur dans les ViewModels. Pour brancher une API,
remplacez le contenu des `ObservableCollection` par un appel de service : les vues n'ont
pas à changer.

## Points connus

- La section **Bots** (03) affiche un écran vide : aucune maquette n'a été fournie.
- Le graphique et la jauge du détail de portefeuille sont dessinés en `Polyline` et `Path`
  avec des coordonnées fixes. Ils ne réagissent pas encore aux données.
- Les boutons Settings, Forgot password, Sign Up, la pagination et le tri des news sont
  présents visuellement mais sans commande associée.

# Actions Admin Console

Le frontend `faah-frontend` affiche le rôle et le statut de chaque compte. Les boutons Make admin / Make employee et Activate / Deactivate appellent les routes PUT existantes `/admin/utilisateurs/{user_id}/role` et `/admin/utilisateurs/{user_id}/statut` avec le token courant. L'affichage change après confirmation du serveur. Le nom ouvre toujours la fiche personnelle.

Le compte connecté ne peut pas modifier son propre rôle ni son propre statut depuis cet écran. Les protections backend existantes contre la désactivation de soi-même et le retrait de son propre rôle admin sont conservées. Les opérations nécessitent toujours le rôle admin côté serveur.

La liste et les réponses de modification utilisent maintenant `AdminUserResponse`, qui ajoute `is_active` et `email` à la réponse administrative. L'authentification conserve son schéma UserResponse. Si le backend n'est pas à jour, le frontend affiche UNKNOWN et désactive le changement de statut au lieu de deviner l'état du compte.

## Backend à déployer

Dans `faah-backend - Copie` : `schemas.py`, `admin/adminService.py`, `admin/gestion.py`. Aucune nouvelle table ni route ; aucune modification à main.py. Déployer ces fichiers sur Ubuntu et redémarrer le backend selon la procédure habituelle. Aucun appel de modification de compte réel ni déploiement distant n'a été effectué pendant le développement.

Validation : compilation frontend, tests HTTP simulés pour changements de rôle/statut, échec serveur, protections du compte courant et ancien format de réponse ; syntaxe Python vérifiée. Le parcours réel avec FastAPI et PostgreSQL reste à tester sur le serveur.

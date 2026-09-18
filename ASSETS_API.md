# Assets et favoris

La page charge le catalogue PostgreSQL via GET /api/assets, les favoris via GET /api/favorites et les cours via GET /api/market. Actualisation toutes les 60 secondes et via Refresh. Les valeurs absentes apparaissent comme Unavailable. La devise vient du serveur ; la variation est nommée DAILY CHANGE car le backend compare les deux dernières clôtures journalières disponibles, pas 24 heures glissantes. Le volume est le dernier volume journalier fourni par Yahoo Finance.

Un clic sur une étoile envoie PUT /api/favorites/{asset_id} pour ajouter ou DELETE pour retirer. L'étoile change uniquement après confirmation du serveur. Pendant la requête, les clics supplémentaires sont désactivés. Les favoris sont relus à l'ouverture et lors des actualisations. Quitter la page arrête son actualisation et annule les requêtes en cours.

Le nouveau routeur backend utilise CurrentUser : aucune identité utilisateur n'est acceptée depuis le frontend. Chaque lecture ou suppression est limitée à l'utilisateur authentifié. PUT est idempotent grâce à ON CONFLICT DO NOTHING sur la clé composée existante (fav_usr_id, fav_ast_id). Aucun changement de schéma n'est nécessaire si la table favorites du modèle existe déjà dans la base déployée.

## VM Ubuntu

Déployer aussi routers/favorites.py et main.py du backend modifié, puis reconstruire/redémarrer le service app selon votre procédure habituelle. Les étoiles nécessitent ces nouvelles routes ; mettre à jour seulement le frontend ne suffit pas. Aucun déploiement ni modification de la base distante n'a été effectué ici.

L'adresse du serveur est configurable par FAAH_API_URL sur le poste frontend (adresse publique HTTPS de la VM). La valeur par défaut reste https://footballhero.ch. PostgreSQL reste côté backend.

## Validation locale

Compilation frontend et tests HTTP simulés : correspondance catalogue/cours/favoris, devise, cours manquant, ajout/suppression, échec d'écriture, relecture, panne du marché, échec de lecture des favoris et arrêt après navigation. Syntaxe Python du routeur et de main.py vérifiée. Les dépendances FastAPI/SQLAlchemy ne sont pas installées dans le Python local : les routes et la persistance doivent encore être vérifiées avec PostgreSQL dans l'environnement backend, notamment avec deux comptes distincts. Le test simulé frontend ne valide pas la base distante.

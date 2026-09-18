# News — connexion au backend FAAH

La page appelle `GET /api/data-sources` avec le client HTTP authentifié existant. Les champs utilisés correspondent à `routers/data_sources.py` et au modèle `DataSource` de « faah-backend - Copie » : `src_id`, `src_title`, `src_content`, `src_original_url`, `src_type`, `src_published_at`, `src_created_at`.

Chargement à l'ouverture, actualisation toutes les 60 secondes et bouton Refresh. Latest trie par publication (ou collecte si la publication manque), A-Z par titre. Le texte est un extrait du contenu, sans résumé généré. Le sentiment et les actifs associés fictifs ont été retirés. Les dates sont affichées telles que renvoyées : le backend ne déclare pas de fuseau pour ces champs.

Une réponse vide affiche un message. En cas d'erreur, les derniers articles et l'heure de la dernière réussite restent affichés avec une erreur explicite. Quitter la page ou se déconnecter arrête le timer et annule la requête en cours.

## Adresse du backend sur la VM Ubuntu

Configurer `FAAH_API_URL` sur le poste qui lance le frontend, avec l'origine publique du backend (par exemple `https://api.votre-domaine.ch`). Sans cette variable, l'adresse existante `https://footballhero.ch` est conservée. Relancer le frontend après un changement.

Exemple PowerShell, à adapter :

```powershell
$env:FAAH_API_URL = 'https://api.votre-domaine.ch'
dotnet run --project FAAH_Frontend
```

Le docker-compose du backend expose FastAPI sur `127.0.0.1:8001` côté Ubuntu. Un reverse proxy HTTPS sur la VM doit transmettre les requêtes vers ce port, en préservant `/auth`, `/admin` et `/api`. L'adresse localhost de la VM ne doit pas être utilisée comme adresse serveur sur un autre poste.

Le backend doit avoir une base PostgreSQL accessible et des articles dans `data_sources`. Pour la collecte automatique, son fichier `.env` doit activer `RUN_WORKERS=true` ; les flux et intervalles se trouvent dans `config/rss_feeds.py`. Le frontend lit les articles collectés, il ne déclenche pas les routes d'ingestion ou d'analyse.

## Validation

Compilation du frontend et tests locaux avec réponses HTTP simulées : mapping JSON, tri, token, état vide, erreurs et reprise, conservation des derniers articles et arrêt après fermeture. La connexion réelle à la VM nécessite son URL, une session valide et le backend en fonctionnement ; elle n'a pas été vérifiée ici. Aucun changement au backend ni aux pages Portfolio, Assets ou tableau de bord n'est inclus.

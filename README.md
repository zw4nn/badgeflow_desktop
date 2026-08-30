# BadgeFlow Desktop 1.4.3

Version Windows de BadgeFlow.

## Format de base partagé Android / Windows

Le format utilisateur de sauvegarde, import et export est désormais **`.badgeflow`** sur les deux plateformes.
Un fichier `.badgeflow` contient `badgeflow-data.json` et `badgeflow-meta.json` dans une archive ZIP.

- Android -> Export `.badgeflow` -> import direct sur PC.
- PC -> Export `.badgeflow` -> import direct sur Android.
- L'autosauvegarde Windows produit `BadgeFlow-auto.badgeflow` ou `BadgeFlow-auto-Nom.badgeflow`.
- Le fichier SQLite `.db` reste uniquement le stockage local interne de la version Windows.
- L'import des anciens `.db` PC reste disponible pour compatibilité.

Le reste des fonctions 1.4 est conservé : onglets Recherche / Mes residences / Nouvelle residence / Parametres, lecteur FDI, annuaire CSV, logiciels memorises, Starprox, partage CSV et blocage des doublons.

## 1.5.0 - Base partagee Android / PC
Dans Parametres > Base partagee Android / PC, choisir le meme BadgeFlow-Sync.badgeflow dans le dossier Google Drive synchronise par Google Drive pour ordinateur.
BadgeFlow Desktop surveille le fichier toutes les 2 secondes, recharge automatiquement les modifications distantes et enregistre chaque modification locale dans ce fichier.
Une detection de conflit bloque l'ecrasement silencieux si Android et Windows ont ete modifies en parallele.

## 1.6.0 - Gestionnaires, badges directs et accordéon
- Gestionnaire / agence facultatif sur chaque résidence, avec suggestions mémorisées.
- Vue Mes résidences : par résidence ou par agence (arborescence repliable).
- Recherche globale par gestionnaire, résidence, adresse, nom/prénom du résident, badge et libellé.
- Badges directement rattachés à une résidence sans créer de faux résident.
- Libellé / référence libre et dates de création / modification des badges.
- Assistant de conversion d'une ancienne pseudo-résidence en gestionnaire.
- Format .badgeflow aligné sur Android 3.6.x, y compris gestionnaire, badges directs, libellés et dates.
- Synchronisation Google Drive conservée.

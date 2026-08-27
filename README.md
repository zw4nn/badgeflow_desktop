# BadgeFlow Desktop 1.4.1

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

# BadgeFlow Desktop 1.2

Version Windows WPF de BadgeFlow, pensée pour le bureau avec le lecteur FDI-MATELEC USB HID (VID `0x1072` / PID `0x0002`).

## Fonctions

- Résidences et résidents.
- Plusieurs badges par résident.
- Lecture du lecteur FDI avec modes Auto, Urmet/Hexact et Intratone.
- Saisie/modification manuelle et marquage Starprox.
- Recherche par résident, logement ou numéro de badge.
- Partage texte avec `ATTENTION BADGE STARPROX`.
- Export CSV configurable : choix des résidents et colonnes, un badge par ligne, données résident uniquement sur la première ligne et CSV normalisé sans accents.
- Import CSV compatible.
- Base SQLite locale dans le profil Windows de l'utilisateur.
- Sauvegarde/export de la base.

## Générer directement le .exe d'installation avec GitHub

Créer un dépôt GitHub séparé, par exemple `BadgeFlow-Desktop`, puis déposer **le contenu de ce ZIP à la racine du dépôt**.

Ouvrir ensuite :

`Actions` → `Build BadgeFlow Desktop` → `Run workflow`

Le workflow produit automatiquement deux artifacts :

1. `BadgeFlow-Portable-win-x64` : version portable autonome.
2. `BadgeFlow-Setup-win-x64` : contient **`BadgeFlow-Setup.exe`**, le vrai installateur Windows.

Le setup installe BadgeFlow dans le dossier Program Files de l'utilisateur, crée une entrée Menu Démarrer, propose un raccourci Bureau et ajoute une désinstallation Windows propre.

Le programme est publié en **self-contained .NET 8 Windows x64**, donc le PC cible n'a pas besoin d'installer séparément le runtime .NET.

## Compiler localement

Avec le SDK .NET 8 et Inno Setup 6 installés :

```bat
build-installer.bat
```

L'installateur sera créé dans :

`installer-output\BadgeFlow-Setup.exe`

## Easter eggs

L'installateur reste volontairement professionnel. Quelques clins d'œil sont néanmoins cachés dans l'assistant. Ils ne modifient aucun fichier système et n'ont aucun effet sur BadgeFlow ou sur les données.


## Icône Windows

Le projet utilise directement le logo BadgeFlow de l'application Android, converti en icône Windows multi-résolutions (`Assets/BadgeFlow.ico`). Il est intégré à `BadgeFlow.exe`, au setup, aux raccourcis Bureau/Menu Démarrer et aux fenêtres WPF.

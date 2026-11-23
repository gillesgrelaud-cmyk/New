# Transfert Photos — Smartphone vers PC (Windows 11)

Application Windows (WPF, .NET 8) pour récupérer automatiquement les **photos** et **vidéos**
d'un **smartphone branché en USB** (mode MTP/« Transfert de fichiers ») vers un dossier du PC.

## Fonctionnalités

- Détection automatique du smartphone connecté en USB (apparaît sous « Ce PC »)
- Analyse du téléphone : comptage des photos et vidéos trouvées
- Transfert vers le dossier PC de votre choix, avec deux modes d'organisation :
  - **Arborescence conservée** (ex. `Stockage interne\DCIM\Camera\IMG_001.jpg`)
  - **Par date de prise de vue** (`2024-06-14\IMG_001.jpg`, repli sur la date de modification)
- **Anti-doublons** : les fichiers déjà présents avec la même taille ne sont pas recopiés ;
  en cas de conflit de nom, le fichier est suffixé `nom (2).jpg`
- Journal détaillé, bouton Annuler, barre de progression

Types de fichiers reconnus :
- **Photos** : jpg, jpeg, png, heic, heif, webp, gif, bmp, dng, tif, tiff, avif
- **Vidéos** : mp4, mov, avi, mkv, 3gp, 3g2, m4v, webm, wmv, mpg, mpeg, mts, m2ts

Les dossiers cachés/metadata (`.thumbnails`, etc.) sont ignorés.

## Prérequis

- Windows 10 ou 11
- Un câble USB ; sur le téléphone, choisir dans la notification USB :
  **« Transfert de fichiers / Transfert de photos (MTP) »**
- Pour compiler : le [SDK .NET 8](https://dotnet.microsoft.com/download/dotnet/8.0)
  (un seul poste doit l'avoir). Le PC cible n'a besoin de rien d'autre
  si vous publiez en « self-contained ».

## Compiler

> **Astuce** : ce dépôt inclut un workflow GitHub Actions (`.github/workflows/build.yml`) qui
> compile automatiquement un `.exe` Windows autonome à chaque push. L'artefact
> `TransfertPhotos-exe` est téléchargeable depuis l'onglet **Actions** du dépôt — aucun SDK
> à installer localement.

```powershell
cd TransfertPhotos
dotnet build -c Release
```

L'exécutable se trouve dans `TransfertPhotos\bin\Release\net8.0-windows\TransfertPhotos.exe`.

## Publier un exécutable autonome (recommandé pour distribuer)

Option A — sans installation de .NET sur le PC cible (~70 Mo, tout embarqué) :

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o dist
```

Option B — léger, mais requiert le « .NET 8 Desktop Runtime » installé :

```powershell
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o dist
```

## Utilisation

1. Branchez le smartphone par USB et validez le mode « Transfert de fichiers (MTP) » sur le téléphone (éventuellement déverrouillez l'écran).
2. Lancez `TransfertPhotos.exe` : l'appareil apparaît dans la liste (sinon cliquez **Actualiser**).
3. Choisissez le **dossier de destination** (par défaut : `Images\Smartphone`).
4. (Facultatif) Cochez **Organiser par date** et/ou **Ignorer les doublons**.
5. Cliquez **Analyser** pour voir combien de photos/vidéos seront copiées, puis **Transférer**.
6. Bouton **Ouvrir le dossier** pour voir le résultat ; **Annuler** interrompt le transfert proprement.

## Comment ça marche

L'application n'installe aucun pilote : elle pilote la couche **MTP de Windows** via l'API
COM `Shell.Application` (ce que fait l'Explorateur de fichiers). Le transfert fichier par
fichier est attendu jusqu'à taille complète, ce qui fiabilise le suivi et l'anti-doublons.

## Limitations connues

- **Android** : fonctionne via le mode MTP. **iPhone** : pas de MTP ;
  utilisez l'import de l'app Photos de Windows (câble/Lightning) ou iCloud.
- Les fichiers `heic` sont copiés tels quels (non convertis en jpg).
- Le MTP est plus lent qu'une copie locale : comptez quelques minutes pour plusieurs Go.
- Si l'écran du téléphone se verrouille, le transfert peut se suspendre : laissez-le déverrouillé.

## Dépannage

| Problème | Solution |
|---|---|
| « Aucun appareil détecté » | Déverrouillez le téléphone, sélectionnez « Transfert de fichiers USB (MTP) » dans la notification USB, cliquez sur Actualiser. Essayez un autre câble/port (un câble « charge seule » ne suffit pas). |
| « L'appareil est inaccessible » | Débranchez/rebranchez le câble, puis Actualiser. |
| Copie plus lente au fil du temps | Le MTP ralentit sur de très nombreux petits fichiers : transférez par lots (ex. dossiers `DCIM\Camera`). |

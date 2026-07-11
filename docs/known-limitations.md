# Limites connues

Ce document décrit honnêtement ce que **Google Photos Local Uploader** ne peut pas faire, et pourquoi. La plupart de ces limites ne viennent pas de l'application elle-même, mais des règles imposées par Google sur son API Photos. Elles sont énoncées ici sans détour pour que vous sachiez exactement à quoi vous attendre avant de lancer un upload.

---

## 1. La détection de doublons ne couvre pas toute votre bibliothèque Google Photos

C'est la limite la plus importante. L'application affiche cet avertissement dans l'onglet Paramètres :

> « Google Photos ne permet pas à cette application de vérifier toute votre bibliothèque. La détection des doublons est garantie uniquement pour les fichiers déjà indexés localement ou uploadés par cette application. »

**Pourquoi ?** Depuis les changements de l'API Google Photos du **31 mars 2025**, Google a supprimé les autorisations (« scopes ») qui permettaient à une application tierce de lire l'ensemble de la bibliothèque d'un utilisateur. Une application ne peut désormais relire **que les médias qu'elle a elle-même créés**. Cette application utilise donc uniquement les scopes `photoslibrary.appendonly` (envoi) et `photoslibrary.readonly.appcreateddata` (relecture de ses propres envois) — il n'existe tout simplement plus de moyen technique de demander à Google : « cette photo existe-t-elle déjà quelque part dans le compte ? ».

**Concrètement :**

- Si une photo est déjà présente dans votre Google Photos parce que vous l'avez ajoutée **par un autre moyen** (application mobile, site web, autre logiciel), cette application ne peut pas le savoir. Elle risque donc de créer un doublon côté Google.
- En revanche, la détection est fiable pour tout ce qui passe par l'application : chaque fichier scanné est identifié par son empreinte SHA-256 dans la base locale. Deux fichiers au contenu strictement identique sont détectés, et un fichier déjà envoyé par l'application n'est jamais renvoyé. Ces cas apparaissent dans l'onglet « Détails des fichiers » avec les statuts « Ignoré (doublon local) » (`skipped_duplicate_local`) et « Ignoré (déjà uploadé) » (`skipped_duplicate_remote_app_created`).

## 2. Les envois consomment le stockage de votre compte Google

Tout fichier envoyé via l'API est stocké en **qualité d'origine** et est **décompté du quota de stockage** de votre compte Google (les 15 Go gratuits, ou votre abonnement Google One). L'API ne propose pas l'option « économiseur de stockage » disponible dans les applications officielles Google Photos ; cette application ne peut donc ni compresser côté Google, ni éviter le décompte. Avant d'envoyer une grande photothèque, vérifiez l'espace disponible sur votre compte.

## 3. Quota d'API : environ 9 500 photos par jour au maximum

Par défaut, Google accorde à un projet Google Cloud un quota de **10 000 requêtes par jour** sur l'API Photos Library. Chaque photo coûte une requête d'envoi d'octets (`POST /v1/uploads`), puis chaque lot déclenche un appel de création (`POST /v1/mediaItems:batchCreate`). Avec la taille de lot par défaut de 20 fichiers, un lot consomme donc 21 requêtes, soit un plafond théorique d'environ **9 500 photos par jour** — moins en pratique, car chaque nouvelle tentative après une erreur consomme aussi des requêtes.

Si le quota est atteint, Google répond par une erreur de limitation (HTTP 429 ou 403 « quota ») : l'application la journalise (« Limite de requêtes Google Photos atteinte (429). Nouvel essai automatique. »), attend en respectant l'en-tête `Retry-After` et un backoff exponentiel plafonné à 60 secondes, puis réessaie. Pour une très grande photothèque, l'envoi complet peut donc s'étaler sur plusieurs jours ; la reprise automatique est prévue pour cela.

## 4. Application OAuth en mode « Test » : reconnexion tous les 7 jours

Vous créez votre propre client OAuth dans Google Cloud Console (type « Application de bureau »). Tant que l'écran de consentement de votre projet reste en statut **« Test »** (le réglage par défaut), Google **fait expirer le refresh token au bout de 7 jours**. Passé ce délai, l'application affiche « La session Google a expiré ou a été révoquée. Reconnectez votre compte. » et il faut simplement se reconnecter — rien n'est perdu, l'inventaire local et la file d'attente sont conservés.

Pour supprimer cette expiration, il faut publier l'application OAuth en « Production » dans Google Cloud Console, ce qui peut impliquer un processus de validation par Google. Ce choix appartient à l'utilisateur ; l'application fonctionne dans les deux cas.

## 5. Pas de vidéos dans cette version (photos uniquement)

La version 1 traite **uniquement des images**. La liste d'extensions par défaut ne contient aucun format vidéo :

```
jpg,jpeg,png,webp,heic,heif,gif,tif,tiff,bmp,avif,ico,
dng,cr2,cr3,crw,nef,nrw,arw,orf,raf,rw2,srw,pef,srf,sr2
```

Les fichiers vidéo présents dans le dossier scanné sont simplement ignorés lors du scan. La limite de taille de 200 Mo (voir point 8) et le flux d'envoi sont eux aussi dimensionnés pour des photos.

## 6. Fichiers RAW : envoyés tels quels, compatibilité selon Google

Les formats RAW (DNG, CR2, CR3, CRW, NEF, NRW, ARW, ORF, RAF, RW2, SRW, PEF, SRF, SR2) sont acceptés par le scan et envoyés à Google avec le type MIME générique `application/octet-stream` (il n'existe pas de type MIME standardisé pour chaque format RAW propriétaire). C'est ensuite **Google Photos qui décide** d'accepter ou de refuser le fichier selon sa propre liste de formats pris en charge. Si Google refuse un fichier, l'application enregistre le message exact (« Google Photos a refusé ce fichier : ... ») et le fichier passe en statut « Erreur » (`failed`), sans bloquer le reste du lot.

## 7. Aucune suppression à distance possible — et aucune suppression, jamais

Le scope `photoslibrary.appendonly` utilisé par l'application permet **uniquement d'ajouter** des médias. L'API ne lui donne aucun moyen de supprimer ou de modifier quoi que ce soit dans votre Google Photos — et l'application ne supprime par ailleurs **jamais** un fichier de votre disque. Comme l'indique l'onglet Paramètres :

> « L'application ne supprime jamais vos photos locales ni vos médias Google Photos. La suppression ci-dessous ne concerne que l'inventaire, les journaux, les paramètres et les secrets stockés par l'application. »

Conséquence à connaître : si vous envoyez une photo par erreur, sa suppression dans Google Photos doit se faire **manuellement** (site ou application Google Photos). Le bouton « Supprimer les données locales de l'application » efface uniquement le dossier `%APPDATA%\GooglePhotosLocalUploader\` (base `app.db`, journaux `logs\`) et les secrets du Gestionnaire d'identifiants Windows — ni vos photos, ni vos médias en ligne.

## 8. Limite de 200 Mo par photo

Google Photos impose une taille maximale de **200 Mo par photo**. L'application applique cette limite avant tout envoi (paramètre « Taille max », par défaut 200 Mo, réglable de 1 à 200 — il n'est pas possible de la dépasser). Un fichier plus volumineux ne sera jamais envoyé.

## 9. Fichiers trop volumineux ou aux formats exclus : ignorés, avec la raison affichée

Tout fichier qui ne passe pas la vérification de compatibilité est marqué « Ignoré (incompatible) » (`skipped_incompatible`) — il n'est ni envoyé, ni supprimé, ni bloquant pour les autres. La raison exacte est enregistrée et visible dans l'onglet « Détails des fichiers » (filtre « Ignorés (incompatible) ») :

- « Extension .xxx non prise en charge » — l'extension ne figure pas dans la liste configurée ;
- « Fichier vide » — fichier de 0 octet ;
- « Fichier trop volumineux (NNN Mo, limite 200 Mo) » — au-delà de la taille maximale.

La liste d'extensions étant configurable dans les Paramètres, un fichier ignoré pour son extension peut être repris lors d'un scan ultérieur si vous ajoutez son extension à la liste.

## 10. La création du client OAuth ne peut pas être automatisée

Google n'expose **aucune API publique** permettant de créer un écran de consentement « Externes » ou un client OAuth de type « Application de bureau » : ni la CLI `gcloud`, ni Terraform (les ressources `google_iap_brand` / `google_iap_client` ne couvrent que les organisations Google Workspace et les clients IAP) ne peuvent produire les identifiants dont l'application a besoin. C'est pourquoi l'application fournit un **assistant intégré** (onglet « Paramètres » → « Assistant de configuration Google Cloud... ») qui guide la création dans la console, ouvre directement les bonnes pages et importe le fichier `client_secret_….json` téléchargé, ainsi qu'un script optionnel `scripts\setup-google-cloud.ps1` qui automatise la seule partie automatisable (création du projet + activation de l'API, via `gcloud`).

---

*Document mis à jour pour la version 1 de l'application. Les comportements décrits ici correspondent au code source vérifié (services `GooglePhotosApi`, `GoogleAuthService`, `UploadService`, `FileScanner`, `CompatibilityChecker`) et aux règles de l'API Google Photos en vigueur depuis le 31 mars 2025.*

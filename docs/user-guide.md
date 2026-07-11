# Guide utilisateur — Google Photos Local Uploader

Ce guide s'adresse à l'utilisateur final de **Google Photos Local Uploader**, l'application Windows (10/11) qui scanne un dossier local d'images, les indexe, puis les envoie par lots vers votre compte Google Photos, avec reprise automatique après interruption.

> **À savoir avant de commencer — limites assumées de l'application**
>
> - Depuis les changements de l'API Google Photos du 31 mars 2025, une application tierce ne peut lire **que les médias qu'elle a elle-même créés**. Comme l'indique l'onglet **Paramètres** : « Google Photos ne permet pas à cette application de vérifier toute votre bibliothèque. La détection des doublons est garantie uniquement pour les fichiers déjà indexés localement ou uploadés par cette application. » Si une photo existe déjà dans Google Photos parce que vous l'avez ajoutée par un autre moyen (téléphone, site web…), l'application ne peut pas le savoir et l'enverra à nouveau.
> - Les photos envoyées comptent dans le **stockage de votre compte Google** (qualité d'origine). L'API ne propose pas l'option « économiseur de stockage ».
> - La limite Google Photos par photo est de **200 Mo**.
> - L'application **ne supprime jamais** vos fichiers locaux ni vos médias Google Photos.
> - Vous devez créer **votre propre client OAuth** dans Google Cloud Console (type « Application de bureau »). La procédure détaillée est décrite dans le guide `docs/google-cloud-setup.md`. Aucun mot de passe Google ne vous est jamais demandé dans l'application : la connexion se fait dans votre navigateur.

---

## 1. Tour de l'interface

La fenêtre principale, intitulée **Google Photos Local Uploader**, se compose de quatre zones, de haut en bas.

### 1.1 Zone « Configuration »

| Élément | Rôle |
|---|---|
| **Dossier racine :** | Chemin du dossier contenant vos photos (lecture seule dans le champ). |
| **Parcourir...** | Ouvre un sélecteur de dossier (« Choisir le dossier racine contenant vos photos »). Le choix est enregistré immédiatement. |
| **Compte Google :** | Affiche « Aucun compte connecté » ou « Connecté : votre@email ». |
| **Connecter mon compte Google** | Lance l'autorisation OAuth dans votre navigateur par défaut. |
| **Déconnecter** | Révoque et supprime le refresh token (voir section 8). |

### 1.2 Barre d'actions

Cinq boutons : **Scanner le dossier**, **Démarrer l'upload**, **Pause**, **Reprendre**, **Arrêter**. Chaque bouton n'est actif que lorsque l'action est possible (par exemple, **Reprendre** n'est cliquable qu'en pause).

### 1.3 Zone « Progression »

- Une barre de progression globale avec le texte du type « 42,5 % (850 uploadés / 2000 fichiers) ».
- Les compteurs : **Détectés**, **En attente**, **Uploadés** (vert), **Ignorés** (orange), **Erreurs** (rouge).
- Une barre de progression du fichier en cours, avec son nom et les octets envoyés (par exemple « IMG_0042.jpg — 3,2 Mo / 8,1 Mo »).
- Le débit (« Débit : 2,4 Mo/s ») et l'estimation « Temps restant estimé : 1 h 05 min ».
- La dernière erreur rencontrée, en rouge.

### 1.4 La barre d'état (bas de fenêtre)

Elle affiche l'état courant (**Prêt**, **Scan en cours**, **Upload en cours**, **Upload en pause**, **Arrêt en cours...**) suivi du dernier message d'information (fin de scan, paramètres enregistrés, etc.).

### 1.5 Les quatre onglets

#### Onglet « Journal »
Le fil d'activité en temps réel (police à chasse fixe) : connexions, scans, uploads réussis, avertissements et erreurs. Les 500 dernières lignes sont conservées à l'écran ; l'historique complet reste disponible via l'export (section 7) et les fichiers journaux sur disque.

#### Onglet « Détails des fichiers »
La liste des fichiers indexés (jusqu'à 2 000 lignes affichées) avec les colonnes **Fichier**, **Statut**, **Taille**, **Uploadé le**, **Erreur / raison**, **Chemin**.

- **Filtre :** liste déroulante avec les valeurs **Tous**, **En attente**, **Uploadés**, **Erreurs**, **Ignorés (doublon local)**, **Ignorés (déjà uploadé)**, **Ignorés (incompatible)**.
- **Actualiser** : recharge la liste.
- **Relancer les fichiers en erreur** : voir section 6.

#### Onglet « Historique »
La liste des 100 derniers lots (batchs) d'upload : **Batch**, **Démarré**, **Terminé**, **Fichiers**, **Réussis**, **Échecs**, **Statut** (« Terminé », « Arrêté » ou « En cours »). Deux boutons : **Actualiser** et **Exporter le journal...** (section 7).

#### Onglet « Paramètres »

- **Authentification Google** : bouton **« Assistant de configuration Google Cloud... »** (assistant en 6 étapes qui ouvre les bonnes pages de la console et importe le fichier `client_secret_….json` téléchargé), et champs **Client ID OAuth :** / **Client Secret OAuth :** pour une saisie manuelle. Le refresh token et le client secret sont conservés dans le Gestionnaire d'identifiants Windows, jamais en clair.
- **Upload** :
  - **Taille du batch (1 à 50 fichiers) :** — 20 par défaut (50 est la limite dure de l'API Google).
  - **Tentatives max en cas d'erreur temporaire :** — 5 par défaut (borné de 0 à 20).
  - **Uploads simultanés (1 à 3) :** — 2 par défaut.
  - **Taille max par photo (Mo, limite Google : 200) :** — 200 par défaut.
- **Formats inclus** : extensions séparées par des virgules, sans point. Par défaut : `jpg,jpeg,png,webp,heic,heif,gif,tif,tiff,bmp,avif,ico,dng,cr2,cr3,crw,nef,nrw,arw,orf,raf,rw2,srw,pef,srf,sr2`. Retirez une extension pour exclure un format ; ajoutez-en une pour l'inclure.
- **Enregistrer les paramètres** : applique et enregistre les valeurs (les valeurs hors bornes sont automatiquement ramenées dans les limites).
- **Détection des doublons** : l'encadré orange rappelant la limite de l'API (texte cité en tête de ce guide).
- **Données locales** : le bouton **Supprimer les données locales de l'application** (section 9).

---

## 2. Première utilisation, pas à pas

1. **Créez votre client OAuth Google** : lancez l'application, ouvrez l'onglet **Paramètres** et cliquez sur **« Assistant de configuration Google Cloud... »**. L'assistant vous guide en 6 étapes (projet Google Cloud, activation de la Photos Library API, écran de consentement, client OAuth « Application de bureau ») en ouvrant les bonnes pages de la console, puis importe le fichier `client_secret_….json` téléchargé — les identifiants sont alors enregistrés automatiquement (le Client Secret part dans le Gestionnaire d'identifiants Windows).
2. **Alternative manuelle** : suivez `docs/google-cloud-setup.md`, renseignez **Client ID OAuth :** et **Client Secret OAuth :** dans l'onglet **Paramètres**, puis cliquez sur **Enregistrer les paramètres**.
3. Cliquez sur **Connecter mon compte Google**. Votre navigateur s'ouvre sur la page d'autorisation Google ; connectez-vous et acceptez les autorisations demandées (ajout de photos et lecture des médias créés par l'application uniquement). Une page « Connexion réussie » s'affiche : vous pouvez fermer la fenêtre du navigateur. Vous disposez de 5 minutes ; au-delà, le message « Délai d'autorisation dépassé (5 minutes). Réessayez. » apparaît.
4. De retour dans l'application, la zone Configuration affiche « Connecté : votre@email ».
5. Cliquez sur **Parcourir...** et choisissez le dossier racine contenant vos photos. Tous les sous-dossiers seront parcourus.
6. Cliquez sur **Scanner le dossier**. L'application énumère les images, calcule une empreinte (hash SHA-256) de chaque fichier et alimente son inventaire local. La barre d'état indique la progression puis un résumé : « Scan terminé : N images vues, N nouvelles, N doublons, N incompatibles. »
7. Cliquez sur **Démarrer l'upload**. Les fichiers en attente sont envoyés par lots (20 par défaut). Suivez l'avancement dans la zone Progression et l'onglet **Journal**.
8. À la fin, un résumé s'affiche : « Upload terminé : N uploadé(s), N en erreur, N ignoré(s). »

Vous pouvez relancer **Scanner le dossier** à tout moment (par exemple après avoir ajouté des photos) : un fichier déjà connu et inchangé n'est pas re-analysé, et un fichier déjà uploadé n'est jamais renvoyé.

---

## 3. Pause, reprise, arrêt

- **Pause** : suspend l'upload. Le transfert du fichier en cours se termine d'abord, comme l'indique le journal : « Upload mis en pause. Le fichier en cours se termine avant l'arrêt effectif. » L'état passe à **Upload en pause**.
- **Reprendre** : repart exactement là où la pause a eu lieu, sans rien renvoyer.
- **Arrêter** : interrompt l'upload (ou le scan) en cours. Les fichiers qui étaient en train d'être envoyés sont marqués **En pause** dans l'inventaire, et le message « Upload arrêté. Les fichiers en cours reprendront au prochain démarrage. » s'affiche. Cliquer à nouveau sur **Démarrer l'upload** reprend le travail restant.

---

## 4. Reprise après fermeture ou crash

L'inventaire est enregistré dans une base SQLite à chaque changement d'état : la reprise est donc fiable même après un crash ou une coupure de courant.

- **Fermeture normale de la fenêtre** : l'application arrête proprement le scan et l'upload avant de se fermer ; les fichiers en cours passent en **En pause**.
- **Au démarrage suivant** : tout fichier resté marqué « en cours d'upload » (cas d'un crash) est automatiquement remis en file d'attente. Le journal l'indique : « Reprise après interruption : N fichier(s) remis en file d'attente. »
- **Au clic sur Démarrer l'upload** : les fichiers **En pause** sont également remis en file, puis l'envoi reprend.
- **Optimisation** : si un fichier avait déjà été transféré chez Google mais pas encore finalisé (crash entre l'envoi des octets et la création du média), son jeton d'upload est conservé. S'il a moins de 20 heures, il est réutilisé **sans renvoyer les octets** (Google annonce une validité d'environ 24 h ; l'application garde une marge de sécurité). Le journal indique alors : « Upload token réutilisé pour … (octets déjà envoyés). »

---

## 5. Lire les statuts des fichiers

Statuts affichés dans la colonne **Statut** de l'onglet **Détails des fichiers** :

| Statut affiché | Signification |
|---|---|
| **Détecté** | Fichier repéré par le scan, pas encore mis en file d'attente (état transitoire). |
| **En attente** | Fichier prêt à être uploadé au prochain passage. |
| **Upload en cours** | Fichier en cours de transfert vers Google Photos. |
| **Uploadé** | Fichier créé avec succès dans Google Photos. La colonne **Uploadé le** indique la date. Il ne sera jamais renvoyé. |
| **Ignoré (doublon local)** | Un autre fichier local a exactement le même contenu (même hash SHA-256). Seul l'original sera envoyé ; la colonne **Erreur / raison** indique le chemin du fichier d'origine. |
| **Ignoré (déjà uploadé)** | Un fichier au contenu identique a déjà été envoyé **par cette application**. Rien n'est renvoyé. |
| **Ignoré (incompatible)** | Extension non incluse dans les paramètres, fichier vide, ou fichier dépassant la taille maximale (raison précisée dans **Erreur / raison**, par exemple « Fichier trop volumineux (250 Mo, limite 200 Mo) »). |
| **Erreur** | L'upload a échoué (raison dans **Erreur / raison**). Les erreurs temporaires sont retentées automatiquement tant que le nombre de tentatives reste sous le maximum configuré (5 par défaut). |
| **En pause** | Fichier interrompu par une pause, un arrêt ou une fermeture ; il reprendra au prochain **Démarrer l'upload**. |

Le compteur **En attente** de la zone Progression regroupe les fichiers *Détecté*, *En attente*, *Upload en cours* et *En pause* ; **Ignorés** regroupe les trois statuts « Ignoré (…) ».

---

## 6. Relancer les fichiers en erreur

Pendant un upload, chaque erreur temporaire (réseau, quota, serveur) est retentée automatiquement avec un délai croissant (backoff exponentiel plafonné à 60 secondes, consigne « Retry-After » de Google respectée). Un fichier qui a épuisé ses tentatives (5 par défaut, réglable dans **Paramètres**) reste en statut **Erreur**.

Pour le relancer :

1. Ouvrez l'onglet **Détails des fichiers** (filtre **Erreurs** pour ne voir qu'eux et lire la colonne **Erreur / raison**).
2. Corrigez la cause si nécessaire (réseau rétabli, fichier remis à sa place, extension réactivée…).
3. Cliquez sur **Relancer les fichiers en erreur**. Le compteur de tentatives est remis à zéro et tous les fichiers en erreur repassent **En attente**. La barre d'état confirme : « N fichier(s) en erreur remis en file d'attente. »
4. Cliquez sur **Démarrer l'upload**.

---

## 7. Exporter le journal

Dans l'onglet **Historique**, cliquez sur **Exporter le journal...**. Une boîte d'enregistrement propose un nom du type `google-photos-uploader-journal-20260711-1430.txt`. Le fichier contient les 10 000 entrées les plus récentes du journal (horodatage, niveau, source, message).

Indépendamment de cet export, l'application écrit aussi un fichier journal quotidien sur disque : `%APPDATA%\GooglePhotosLocalUploader\logs\app-AAAAMMJJ.log`. Les entrées en base de plus de 90 jours sont purgées automatiquement au démarrage.

---

## 8. Déconnecter le compte Google

Cliquez sur **Déconnecter** dans la zone Configuration. Une confirmation s'affiche : « Déconnecter le compte Google ? Le refresh token sera révoqué et supprimé du Gestionnaire d'identifiants Windows. »

En confirmant :

- le refresh token est révoqué auprès de Google (si vous êtes hors ligne, la révocation distante est simplement ignorée, mais le refresh token local est tout de même effacé) ;
- le **Client Secret est conservé** dans le Gestionnaire d'identifiants Windows : vous pouvez reconnecter un compte (le même ou un autre) sans le retaper. Il n'est effacé que par « Supprimer les données locales de l'application » ;
- le refresh token et le client secret sont supprimés du Gestionnaire d'identifiants Windows ;
- l'application affiche à nouveau « Aucun compte connecté ».

Votre inventaire local (fichiers déjà uploadés, historique) est conservé. Vous pouvez reconnecter le même compte ou un autre à tout moment.

> Vous pouvez aussi révoquer l'accès de l'application depuis votre compte Google (myaccount.google.com, section Sécurité). Dans ce cas, l'application affichera au prochain upload : « Session Google expirée : reconnectez votre compte puis relancez l'upload. »

---

## 9. Supprimer les données locales de l'application

Dans l'onglet **Paramètres**, section **Données locales**, cliquez sur **Supprimer les données locales de l'application**. Après confirmation, l'application :

1. arrête tout upload en cours ;
2. déconnecte le compte Google et efface les secrets du Gestionnaire d'identifiants Windows ;
3. supprime le dossier `%APPDATA%\GooglePhotosLocalUploader\` (base de données `app.db` : inventaire, historique, paramètres, journaux) ;
4. se ferme.

Comme le rappelle la boîte de confirmation : **vos photos locales et vos médias Google Photos ne sont PAS touchés**. En revanche, l'application perd la mémoire de ce qui a déjà été uploadé : après un nouveau scan, les fichiers seront considérés comme jamais envoyés et seraient renvoyés (créant des doublons côté Google, que l'API ne permet pas de détecter).

---

## 10. FAQ

**Que se passe-t-il si je déplace ou supprime un fichier après le scan ?**
Si le fichier n'existe plus au moment de son upload, il passe en statut **Erreur** avec la raison « Fichier introuvable (déplacé ou supprimé depuis le scan). » et n'est plus retenté automatiquement. Lors d'un nouveau scan, les fichiers non retrouvés (et non encore uploadés) sont marqués « disparus » dans l'inventaire. Si le fichier a été déplacé ailleurs **dans le dossier racine**, le scan le retrouve sous son nouveau chemin ; grâce au hash SHA-256, s'il avait déjà été uploadé par l'application, il est marqué **Ignoré (déjà uploadé)** et n'est pas renvoyé.

**Que se passe-t-il si je perds la connexion réseau pendant un upload ?**
Chaque fichier est retenté automatiquement avec un délai croissant (1 s, 2 s, 4 s… plafonné à 60 s, plus une composante aléatoire). Après 5 échecs réseau consécutifs, un disjoncteur arrête la session avec le message « Trop d'erreurs réseau consécutives. Vérifiez la connexion Internet puis relancez l'upload. » Rien n'est perdu : les fichiers restent en attente ou en erreur relançable, et un simple clic sur **Démarrer l'upload** reprend le travail une fois le réseau rétabli.

**Que se passe-t-il si le token expire ?**
Il y a trois « tokens » différents, tous gérés automatiquement :
- le **token d'accès** (courte durée) est rafraîchi automatiquement, y compris en plein upload ;
- le **refresh token** (longue durée) peut être révoqué ou expirer côté Google ; dans ce cas l'upload s'interrompt avec le message « Session Google expirée : reconnectez votre compte puis relancez l'upload. » — cliquez sur **Connecter mon compte Google**, puis relancez ;
- le **jeton d'upload** d'un fichier (octets déjà transférés mais média pas encore créé) est réutilisé s'il a moins de 20 heures ; au-delà, les octets du fichier sont simplement renvoyés.

**L'application peut-elle détecter qu'une photo est déjà dans Google Photos ?**
Uniquement si c'est **elle** qui l'y a mise. L'API ne lui donne aucun accès au reste de votre bibliothèque (voir l'encadré de l'onglet **Paramètres**).

**Que se passe-t-il si je modifie un fichier déjà uploadé ?**
Au scan suivant, le changement de contenu est détecté (hash différent) : le fichier repasse **En attente** et la nouvelle version est uploadée comme un nouveau média. L'ancienne version reste dans Google Photos : l'application ne supprime jamais rien.

**Les vidéos sont-elles prises en charge ?**
La liste de formats par défaut ne contient que des formats d'image (y compris RAW). La limite de taille configurée dans l'application vise les photos (limite Google : 200 Mo).

**Puis-je lancer l'application deux fois en même temps ?**
Non : une seule instance peut tourner à la fois (les deux se disputeraient le même inventaire). Si vous relancez l'application alors qu'elle est déjà ouverte, un message « Google Photos Local Uploader est déjà en cours d'exécution. » s'affiche et la seconde instance se ferme.

**Où sont stockées mes données et mes secrets ?**
L'inventaire, l'historique et les paramètres : `%APPDATA%\GooglePhotosLocalUploader\app.db`. Les journaux : `%APPDATA%\GooglePhotosLocalUploader\logs\`. Le refresh token et le client secret OAuth : dans le Gestionnaire d'identifiants Windows (entrées `GooglePhotosLocalUploader/RefreshToken` et `GooglePhotosLocalUploader/OAuthClientSecret`), jamais en clair sur le disque. Le Client ID, qui n'est pas un secret, est stocké dans la base.

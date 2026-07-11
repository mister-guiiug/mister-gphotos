# Configuration Google Cloud (OAuth) — guide pas à pas

Ce guide s'adresse à tout utilisateur de **Google Photos Local Uploader**, sans aucune
connaissance technique préalable. Il explique comment créer, dans Google Cloud, les
identifiants OAuth dont l'application a besoin pour envoyer vos photos vers votre compte
Google Photos.

Comptez environ **15 minutes**. Vous n'aurez besoin que d'un navigateur et de votre compte
Google.

> **Trois façons de faire — choisissez la vôtre :**
>
> 1. **L'assistant intégré (recommandé)** : dans l'application, onglet **« Paramètres »** →
>    bouton **« Assistant de configuration Google Cloud... »**. Il reprend exactement les
>    étapes de ce guide, ouvre les bonnes pages de la console à chaque étape, puis
>    **importe le fichier `client_secret_….json`** téléchargé — plus rien à recopier.
> 2. **Ce guide**, pour tout faire à la main dans le navigateur.
> 3. **Le script `scripts\setup-google-cloud.ps1`** (pour les utilisateurs de la CLI
>    `gcloud`) : il automatise les étapes 1 et 2 (projet + activation de l'API), puis
>    ouvre les pages des étapes 3 et 4.
>
> **Pourquoi pas une automatisation complète ?** Google n'expose **aucune API publique**
> pour créer un écran de consentement « Externes » ni un client OAuth de type
> « Application de bureau » : ni `gcloud`, ni Terraform (`google_iap_brand` /
> `google_iap_client` ne couvrent que les organisations Google Workspace et les clients
> IAP) ne peuvent le faire. Ces deux étapes se font donc obligatoirement dans la console,
> quel que soit l'outil.

> **Pourquoi cette étape est-elle nécessaire ?**
> L'application ne possède pas de « clé » Google partagée entre tous les utilisateurs :
> chacun crée **son propre client OAuth** dans **sa propre** console Google Cloud. C'est
> gratuit, cela ne nécessite aucune carte bancaire, et cela garantit que personne d'autre
> que vous ne contrôle l'accès à votre compte. L'application ne vous demandera **jamais**
> votre mot de passe Google : la connexion se fait dans votre navigateur, directement sur
> les pages de Google.

---

## Ce qu'il faut savoir avant de commencer (limites, énoncées franchement)

- **Depuis le 31 mars 2025**, la Google Photos Library API ne permet plus à une
  application de lire l'ensemble de votre bibliothèque Google Photos. Une application ne
  peut relire **que les médias qu'elle a elle-même créés**. Conséquence, affichée telle
  quelle dans l'application : « Google Photos ne permet pas à cette application de
  vérifier toute votre bibliothèque. La détection des doublons est garantie uniquement
  pour les fichiers déjà indexés localement ou uploadés par cette application. »
- Les photos envoyées **comptent dans le stockage de votre compte Google** (qualité
  d'origine). L'API ne propose pas l'option « économiseur de stockage ».
- Google Photos limite chaque photo à **200 Mo**.
- L'API applique un **quota de requêtes par jour** (10 000 par défaut, voir la section
  [Quotas](#quotas-de-lapi-photos-library) plus bas).
- L'application ne supprime **jamais** vos fichiers locaux ni vos médias Google Photos.

---

## Étape 1 — Créer un projet Google Cloud

1. Ouvrez la console Google Cloud : <https://console.cloud.google.com/> et connectez-vous
   avec le compte Google qui recevra les photos.
2. Si c'est votre première visite, acceptez les conditions d'utilisation lorsque Google
   vous les présente.
3. En haut de la page, cliquez sur le **sélecteur de projet** (à côté du logo
   « Google Cloud »), puis sur **« Nouveau projet »**.
4. Donnez un nom au projet, par exemple `Photos Local Uploader`, laissez les autres champs
   par défaut, puis cliquez sur **« Créer »**.
5. Attendez quelques secondes, puis **sélectionnez ce nouveau projet** dans le sélecteur de
   projet (vérifiez que son nom apparaît bien en haut de la page pour toutes les étapes
   suivantes).

## Étape 2 — Activer l'API « Photos Library API »

1. Dans le menu de gauche, ouvrez **« API et services »** puis **« Bibliothèque »**
   (ou allez directement sur <https://console.cloud.google.com/apis/library>).
2. Dans le champ de recherche, tapez **`Photos Library API`**.
3. Cliquez sur le résultat **« Photos Library API »**, puis sur le bouton **« Activer »**.

Sans cette activation, la connexion échouera avec une erreur d'accès refusé.

## Étape 3 — Configurer l'écran de consentement OAuth

L'écran de consentement est la page que Google vous montrera dans le navigateur au moment
de la connexion, pour vous demander d'autoriser l'application.

> Selon la version de la console, cette configuration se trouve sous
> **« API et services » → « Écran de consentement OAuth »**, ou sous la rubrique
> **« Google Auth Platform »**. Les libellés peuvent varier légèrement, mais les choix à
> faire restent les mêmes.

1. Ouvrez **« API et services » → « Écran de consentement OAuth »**.
2. Type d'utilisateur (**Audience**) : choisissez **« Externes »** (le choix « Interne »
   n'existe que pour les organisations Google Workspace). Cliquez sur **« Créer »**.
3. Renseignez les champs obligatoires :
   - **Nom de l'application** : par exemple `Google Photos Local Uploader` ;
   - **Adresse e-mail d'assistance utilisateur** : votre propre adresse ;
   - **Coordonnées du développeur** : votre propre adresse également.
4. Laissez le reste par défaut et enregistrez. L'ajout des « champs d'application »
   (scopes) sur cet écran est facultatif : l'application les demandera d'elle-même au
   moment de la connexion (la liste exacte est donnée [plus bas](#autorisations-scopes-demandées-par-lapplication)).
5. **Utilisateurs test** : dans la section « Utilisateurs test » (ou « Audience » →
   « Test users »), cliquez sur **« Ajouter des utilisateurs »** et ajoutez **votre propre
   adresse Gmail** (celle du compte Google Photos de destination). Sans cela, Google
   bloquera la connexion avec un message du type « Accès bloqué : cette application n'a pas
   terminé la procédure de validation de Google ».

Votre application est alors en **mode « Test »** (statut de publication « Testing »).

### Important : en mode Test, la connexion expire tous les 7 jours

C'est une règle de Google, pas de l'application : pour une application externe dont le
statut de publication est **« Test »**, le **refresh token expire au bout de 7 jours**.
Concrètement, environ une fois par semaine, l'application affichera « La session Google a
expiré ou a été révoquée. Reconnectez votre compte. » et il faudra cliquer à nouveau sur
**« Connecter mon compte Google »** (30 secondes, aucune donnée n'est perdue : l'inventaire
et la progression sont conservés localement).

Vous avez deux options :

| Option | Comment | Conséquence |
|---|---|---|
| **Rester en mode Test** | Ne rien faire de plus | Reconnexion à refaire tous les 7 jours ; suffisant pour un usage ponctuel |
| **Passer en production** | Sur l'écran de consentement, cliquer sur **« Publier l'application »** (statut « En production ») | Le refresh token n'expire plus au bout de 7 jours ; recommandé pour un usage régulier |

Précision honnête sur le passage en production : les scopes Google Photos sont classés
« sensibles » par Google, et une application « En production » non validée par Google
affichera, lors de la connexion, un avertissement **« Google n'a pas validé cette
application »**. Pour un usage strictement personnel (vous êtes à la fois le développeur
du projet Cloud et le seul utilisateur), c'est attendu et sans danger : cliquez sur
**« Paramètres avancés »** puis **« Accéder à [nom de l'application] »** pour continuer.
La procédure complète de validation par Google n'est utile que si vous comptez distribuer
votre client OAuth à d'autres personnes, ce qui n'est pas le cas ici.

## Étape 4 — Créer l'identifiant OAuth « Application de bureau »

1. Ouvrez **« API et services » → « Identifiants »**
   (<https://console.cloud.google.com/apis/credentials>).
2. Cliquez sur **« Créer des identifiants » → « ID client OAuth »**.
3. **Type d'application** : choisissez **« Application de bureau »** (Desktop app).
   C'est indispensable : ce type de client autorise la redirection locale
   `http://127.0.0.1:{port}/` que l'application utilise pour recevoir l'autorisation
   (le port est choisi automatiquement à chaque connexion, il n'y a **rien à déclarer**
   dans la console à ce sujet).
4. Donnez-lui un nom, par exemple `Client bureau Uploader`, puis cliquez sur **« Créer »**.

## Étape 5 — Récupérer le Client ID et le Client Secret

À la création, Google affiche une fenêtre contenant :

- l'**ID client** (Client ID) — une longue chaîne se terminant par
  `.apps.googleusercontent.com` ;
- le **Code secret du client** (Client Secret) — une chaîne commençant en général par
  `GOCSPX-`.

Copiez ces deux valeurs (bouton de copie, ou téléchargez le fichier JSON proposé et
gardez-le en lieu sûr). Vous pourrez toujours les retrouver plus tard dans
**« API et services » → « Identifiants »**, en cliquant sur le nom du client créé.

> À savoir : pour une application de bureau, Google lui-même considère que ce « secret »
> ne peut pas rester réellement confidentiel. L'application le traite néanmoins comme un
> secret : il est stocké chiffré dans le Gestionnaire d'identifiants Windows, jamais en
> clair sur le disque (voir [Sécurité](#où-sont-stockés-vos-identifiants-et-secrets)).

## Étape 6 — Saisir les identifiants dans l'application

1. Lancez **Google Photos Local Uploader**.
2. Ouvrez l'onglet **« Paramètres »**, section **« Authentification Google »**.
3. Collez l'ID client dans le champ **« Client ID OAuth : »**.
4. Collez le code secret dans le champ **« Client Secret OAuth : »** (le champ masque la
   saisie ; le secret n'est pas enregistré avec les autres paramètres, il n'est conservé —
   chiffré — qu'après une connexion réussie).
5. Cliquez sur **« Enregistrer les paramètres »**.
6. En haut de la fenêtre, dans la zone **« Configuration »**, cliquez sur
   **« Connecter mon compte Google »**.
7. Votre navigateur s'ouvre : choisissez votre compte, acceptez les autorisations
   demandées (en mode Test non validé, passez par « Paramètres avancés » → « Accéder à… »
   si l'avertissement s'affiche). Vous disposez de **5 minutes** avant expiration du délai
   (« Délai d'autorisation dépassé (5 minutes). Réessayez. »).
8. La page affiche **« Connexion réussie — Vous pouvez fermer cette fenêtre et revenir dans
   Google Photos Local Uploader. »** ; dans l'application, la ligne « Compte Google : »
   passe à **« Connecté : votre-adresse@gmail.com »**.

Pour vous déconnecter plus tard : bouton **« Déconnecter »** (le refresh token est révoqué
auprès de Google et supprimé du Gestionnaire d'identifiants Windows).

---

## Autorisations (scopes) demandées par l'application

Lors de la connexion, l'application demande exactement quatre autorisations, ni plus ni
moins :

| Scope | À quoi il sert |
|---|---|
| `https://www.googleapis.com/auth/photoslibrary.appendonly` | **Ajouter** des photos à votre bibliothèque (upload). C'est un droit d'ajout seul : il ne permet ni de lire, ni de modifier, ni de supprimer vos médias existants. |
| `https://www.googleapis.com/auth/photoslibrary.readonly.appcreateddata` | **Relire uniquement les médias créés par cette application** — c'est tout ce que l'API autorise depuis le 31 mars 2025. Utilisé pour vérifier ce que l'application a déjà envoyé. |
| `openid` | Identification standard, nécessaire pour obtenir le jeton d'identité. |
| `email` | Afficher l'adresse du compte connecté dans l'interface (« Connecté : … »). |

L'application ne demande **aucun** scope donnant accès en lecture ou en modification à
l'ensemble de votre bibliothèque : de tels accès n'existent d'ailleurs plus dans l'API
pour les applications tierces.

## Quotas de l'API Photos Library

- Le quota par défaut de la Photos Library API est de **10 000 requêtes par jour et par
  projet**. Pour un usage personnel, c'est largement suffisant dans la plupart des cas :
  chaque photo consomme une requête d'upload, plus une requête de création
  (`mediaItems:batchCreate`) partagée par lot de 20 fichiers (réglage par défaut, maximum
  50) — soit, en ordre de grandeur, **environ 9 500 photos par jour**.
- Où consulter vos quotas : console Google Cloud → **« API et services »** →
  **« API et services activés »** → **« Photos Library API »** → onglet
  **« Quotas et limites du système »**. C'est aussi là que se demande une éventuelle
  augmentation auprès de Google.
- Si le quota ou la limite de débit est atteint, l'application ne perd rien : elle
  journalise par exemple « Limite de requêtes Google Photos atteinte (429). Nouvel essai
  automatique. », respecte le délai demandé par Google (en-tête `Retry-After`), réessaie
  avec un délai progressif (plafonné à 60 s) et, au besoin, les fichiers concernés seront
  repris à la prochaine session.

## Où sont stockés vos identifiants et secrets

| Donnée | Emplacement | Sensible ? |
|---|---|---|
| Client ID OAuth | Base locale SQLite (`%APPDATA%\GooglePhotosLocalUploader\app.db`) | Non (le Client ID n'est pas un secret) |
| Client Secret OAuth | Gestionnaire d'identifiants Windows, entrée `GooglePhotosLocalUploader/OAuthClientSecret` | Oui — jamais écrit en clair |
| Refresh token (jeton de connexion) | Gestionnaire d'identifiants Windows, entrée `GooglePhotosLocalUploader/RefreshToken` | Oui — jamais écrit en clair |

Le bouton **« Supprimer les données locales de l'application »** (onglet « Paramètres »,
section « Données locales ») efface l'inventaire, les journaux, les paramètres et ces deux
secrets. Vos photos locales et vos médias Google Photos ne sont pas touchés.

## Dépannage rapide

| Symptôme | Cause probable | Solution |
|---|---|---|
| « Accès bloqué : cette application n'a pas terminé la procédure de validation » | Votre adresse n'est pas dans les **utilisateurs test** (mode Test) | Étape 3, point 5 : ajoutez votre adresse, puis réessayez |
| Avertissement « Google n'a pas validé cette application » | Application « En production » non validée par Google | Normal pour un usage personnel : « Paramètres avancés » → « Accéder à… » |
| « La session Google a expiré ou a été révoquée. Reconnectez votre compte. » au bout d'une semaine | Mode Test : refresh token expiré après 7 jours | Reconnectez-vous, ou publiez l'application « En production » (étape 3) |
| « Le Client ID est manquant ou invalide (…), ou le Client Secret est manquant. Voulez-vous ouvrir l'assistant de configuration pas à pas ? » | Champs vides, Client ID mal recopié, ou paramètres non enregistrés | Répondez « Oui » pour ouvrir l'assistant intégré, ou vérifiez les deux champs (le Client ID se termine par `.apps.googleusercontent.com`) puis « Enregistrer les paramètres » |
| « Échange du code OAuth refusé par Google (401). » | Client Secret erroné ou client supprimé dans la console | Recopiez le secret depuis « API et services » → « Identifiants » |
| « Google n'a pas fourni de refresh token. Réessayez la connexion. » | Réponse Google incomplète (rare) | Relancez « Connecter mon compte Google » ; l'application force une nouvelle demande de consentement à chaque connexion, un nouveau refresh token sera émis |
| Erreur 403 « Accès refusé par Google Photos : … » lors de l'upload | « Photos Library API » non activée sur le projet | Étape 2 : activez l'API, attendez quelques minutes, réessayez |
| « Délai d'autorisation dépassé (5 minutes). Réessayez. » | La page du navigateur est restée sans réponse plus de 5 minutes | Cliquez à nouveau sur « Connecter mon compte Google » |
| « L'accès à Google Photos n'a pas été accordé. Sur l'écran de consentement Google, cochez toutes les autorisations demandées puis réessayez la connexion. » | Consentement granulaire : les cases « Google Photos » n'ont pas été cochées sur l'écran d'autorisation | Relancez la connexion et cochez **toutes** les autorisations proposées par Google |

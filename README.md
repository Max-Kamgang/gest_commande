# Gestion des commandes, du stock et des livraisons

Application de gestion des commandes decoupee en **5 microservices** qui tournent
dans des **conteneurs Windows** sur **Windows Server 2025**, orchestres par Docker
Compose. Le poste client (Windows 10/11) accede a tout depuis un navigateur.

Technologies : .NET 10 (ASP.NET Core Web API), EF Core + SQLite, YARP, JWT,
Docker Compose, PowerShell.

---

## 1. Architecture

```
                        Poste client (Windows 10/11)
                                navigateur
                                    |
                            http://SRV-MICRO-01
                                    |
            +=======================================================+
            |  CONTENEUR 1 : gateway          (seul port publie)    |
            |  - sert l'interface web (HTML/CSS/JS)                 |
            |  - route /api/... vers les 4 services                 |
            |  - valide le jeton JWT, limite le debit               |
            +=======================================================+
                 |              |               |              |
     reseau Docker « microservices » (driver nat, DNS interne)
                 |              |               |              |
    +------------v---+  +-------v--------+  +---v----------+  +v-------------------+
    | CONTENEUR 2    |  | CONTENEUR 3    |  | CONTENEUR 4  |  | CONTENEUR 5        |
    | identity       |  | catalog        |  | order        |  | delivery           |
    |                |  |                |  |              |  |                    |
    | comptes        |  | produits       |  | clients      |  | livreurs           |
    | roles          |  | categories     |  | commandes    |  | bordereaux         |
    | jetons JWT     |  | prix           |  | cycle de vie |  | suivi              |
    |                |  | STOCK          |  |              |  | NOTIFICATIONS      |
    +----------------+  +----------------+  +--------------+  +--------------------+
       identity.db         catalog.db          order.db           delivery.db
```

**Une base par service.** Il n'y a aucune cle etrangere entre services : chacun
possede son fichier SQLite et ne connait les autres que par leur API HTTP.

**Qui appelle qui.** Un seul service pilote, `order-service` :

```
order-service  --->  catalog-service    reserver / liberer / sortir le stock
order-service  --->  delivery-service   creer le bordereau de livraison
delivery-service --> order-service      signaler que la commande est livree
```

`identity-service` n'appelle personne : il signe les jetons, que les autres
valident localement avec la meme cle.

---

## 2. Le cycle de vie d'une commande

C'est le coeur du projet. Une commande traverse cinq etats, et chaque transition
est verifiee par une machine a etats (`OrderStateMachine`) : une transition
interdite renvoie un **409** avec la liste des transitions possibles, jamais une
erreur 500.

```
  Brouillon  --confirmer-->  Confirmee  --preparer-->  EnPreparation
                                                            |
                                                        expedier
                                                            v
     Livree  <--livraison remise--  Expediee
                                                 (Annulee possible tant que
                                                  le stock n'est pas sorti)
```

| Transition | Ce qui se passe vraiment |
|---|---|
| **Confirmer** | `order-service` appelle `catalog-service` : le stock est **reserve** et les **prix du moment sont renvoyes** puis figes dans les lignes. Un seul appel distant pour les deux. |
| **Preparer** | Changement d'etat local, aucun appel distant. |
| **Expedier** | La reservation devient une **sortie definitive** de stock, puis le **bordereau de livraison** est cree chez `delivery-service`. |
| **Annuler** | Si du stock etait immobilise, la reservation est **liberee** avant l'enregistrement. |
| **Livrer** | `delivery-service` enregistre la remise au client, puis previent `order-service`. |

### Ce qui se passe quand une etape echoue

Deux regles, visibles dans [`OrderWorkflow.cs`](src/OrderService/Services/OrderWorkflow.cs) :

1. **A la confirmation, on revient en arriere.** Si le stock est reserve mais que
   l'enregistrement local echoue, la reservation est liberee et la commande
   **reste en Brouillon**, avec le motif dans `lastError`. Il n'existe jamais de
   commande a moitie confirmee.
2. **A l'expedition, on rejoue en avant.** Une sortie de stock definitive ne se
   reprend pas automatiquement : si la creation du bordereau echoue apres la
   sortie, la commande reste en preparation et il suffit de **relancer
   l'expedition**. Les deux appels distants sont rejouables (meme numero de
   commande = meme effet), donc la relance ne double rien.

---

## 3. Deploiement sur le serveur

### Pre-requis

- Windows Server 2025 (ou 2022, voir la note sur les tags d'images)
- Une console PowerShell **en administrateur**
- Le code du projet copie sur le serveur

### Trois commandes

```powershell
# 1. Preparer le serveur : Docker, dossiers, reseau, pare-feu (une seule fois)
.\deploy\1-setup-serveur.ps1
```

```powershell
# 2. Renseigner les deux secrets
Copy-Item .env.example .env
notepad .env
```

```powershell
# 3. Construire les images et demarrer les cinq conteneurs
.\deploy\2-deployer.ps1
```

Le script de deploiement attend que les cinq conteneurs soient `healthy`,
interroge l'etat de la plateforme et affiche les adresses d'acces. La premiere
construction est longue : elle telecharge les images de base nanoserver.

> **Windows Server 2022** : remplacer `ltsc2025` par `ltsc2022` dans les deux
> lignes `SDK_IMAGE` / `RUNTIME_IMAGE` du `.env`. Le tag doit correspondre a la
> version de l'hote, sinon les conteneurs refusent de demarrer.

> **HTTPS** : la plateforme est publiee en HTTP, ce qui suffit sur un reseau
> local maitrise. Pour ajouter TLS, generer un certificat auto-signe
> (`New-SelfSignedCertificate` puis `Export-PfxCertificate`), le monter dans le
> conteneur `gateway` et ajouter trois variables a son bloc `environment` :
> `ASPNETCORE_URLS=http://+:8080;https://+:8443`,
> `Kestrel__Certificates__Default__Path` et
> `Kestrel__Certificates__Default__Password`, puis publier le port 443.

### Acces depuis le poste client

| Adresse | Contenu |
|---|---|
| `http://SRV-MICRO-01` | interface web |
| `http://SRV-MICRO-01/swagger` | documentation des 4 API, dans une seule page |
| `http://SRV-MICRO-01/health/services` | etat des 4 services, en une requete |

### Comptes de demonstration

Crees automatiquement au premier demarrage. Mot de passe commun : `Demo@2026`.

| Compte | Role | Ce qu'il peut faire |
|---|---|---|
| `admin@micro.local` | Admin | tout, plus la gestion des comptes |
| `gestionnaire@micro.local` | Gestionnaire | catalogue, stock, commandes, affectation des livraisons |
| `livreur@micro.local` | Livreur | **ses** livraisons uniquement |
| `client@micro.local` | Client | **ses** commandes uniquement |

Le catalogue (8 produits avec leur stock), 3 clients et 2 livreurs sont egalement
crees au premier demarrage : le scenario complet est jouable immediatement.

### Scenario de demonstration

1. Se connecter en `gestionnaire@micro.local`.
2. **Commandes** -> *Nouvelle commande* : choisir un client, ajouter deux lignes.
3. Ouvrir la commande -> **Confirmer**. Verifier dans **Stock** -> *Mouvements*
   que deux reservations sont apparues, et que la quantite disponible a baisse.
4. **Mettre en preparation**, puis **Expedier** : le stock sort definitivement et
   un bordereau apparait dans **Livraisons**.
5. Dans **Livraisons**, affecter un livreur, puis *Enlevement effectue*.
6. Se connecter en `livreur@micro.local` : il ne voit que cette livraison.
   *Marquer livree*. La commande repasse automatiquement a **Livree**.
7. **Notifications** : chaque etape a laisse un message de suivi.

Pour montrer la gestion d'erreur : commander une quantite superieure au stock
disponible et confirmer. La reponse est un **409** explicite, la commande reste en
brouillon et le motif est affiche sur sa fiche.

---

## 4. Developpement en local

Sans Docker, les cinq projets se lancent sur des ports distincts. Il faut le
**SDK .NET 10**.

```powershell
dotnet restore GestionCommandes.slnx
```

Puis, dans cinq terminaux :

```powershell
dotnet run --project src/IdentityService    # http://localhost:5001
```

```powershell
dotnet run --project src/CatalogService     # http://localhost:5002
```

```powershell
dotnet run --project src/OrderService       # http://localhost:5003
```

```powershell
dotnet run --project src/DeliveryService    # http://localhost:5004
```

```powershell
dotnet run --project src/Gateway            # http://localhost:5000
```

En environnement `Development`, la cle JWT et la cle interne sont lues dans les
fichiers `appsettings.Development.json` : aucun secret a configurer. Les bases
SQLite sont creees dans un dossier `data` a cote de chaque projet.

---

## 5. Les API

Tout passe par la gateway. Jeton obtenu par `POST /api/auth/login`, puis envoye
dans l'en-tete `Authorization: Bearer <jeton>`.

| Service | Endpoints |
|---|---|
| identity | `/api/auth/login`, `/api/auth/me`, `/api/users` |
| catalog | `/api/categories`, `/api/products`, `/api/stock`, `/api/stock/movements`, `/api/stock/entries`, `/api/stock/adjustments`, `/api/stock/reservations` |
| order | `/api/customers`, `/api/orders`, `/api/orders/{id}/confirm` · `/prepare` · `/ship` · `/cancel` |
| delivery | `/api/couriers`, `/api/deliveries`, `/api/deliveries/{id}/assign` · `/pickup` · `/complete` · `/fail`, `/api/notifications` |

Deux endpoints decrivent les machines a etats, sans authentification :
`/api/orders/workflow` et `/api/deliveries/workflow`.

### Securite

- **Jeton JWT** signe en HMAC-SHA256, valide des la gateway puis a nouveau par
  chaque service (defense en profondeur).
- **Mots de passe** : PBKDF2-HMAC-SHA256, 210 000 iterations, sel aleatoire.
  Aucun mot de passe n'est stocke ni journalise en clair.
- **Appels entre conteneurs** : en-tete `X-Internal-Key`. La gateway **supprime**
  cet en-tete de toute requete entrante, donc un client exterieur ne peut jamais
  s'en servir pour se faire passer pour un service.
- **Un seul port publie.** Les quatre services ne sont joignables depuis aucun
  poste du reseau, seulement depuis le reseau Docker interne.

---

## 6. Choix de conception

Ce que le projet fait **volontairement simple**, et pourquoi :

| Choix | Raison |
|---|---|
| **Le catalogue porte aussi le stock** | Un produit et son stock sont une seule entite dans un seul service : pas de jointure entre bases, pas d'appel reseau supplementaire sur le chemin critique de la commande. |
| **Un stock global par produit** (pas d'entrepots) | Le theme porte sur les commandes, pas sur la logistique d'entrepot. C'est le point d'extension naturel : ajouter une table `Stock(produit, entrepot)` ne touche qu'a `catalog-service`. |
| **Les notifications sont dans delivery-service** | Elles decrivent les evenements de livraison. L'endpoint `POST /api/notifications` reste ouvert aux appels internes pour qu'un autre service puisse publier les siennes sans rien changer ici. |
| **La gateway sert l'interface web** | Tous les appels du navigateur sont de **meme origine** : aucun CORS a configurer, et un conteneur en moins. |
| **`EnsureCreated` plutot que les migrations EF** | Le schema est cree d'un bloc au premier demarrage. Suffisant pour une base dediee par service, et l'outillage de migration n'est pas embarque dans les images. |
| **SQLite plutot qu'un SGBD reseau** | Une base par service, un fichier par conteneur, aucun serveur de base a installer. Les montages Docker assurent la persistance. |
| **Pas de bus de messages** | Les trois appels entre services sont synchrones et rejouables. Un bus ajouterait un conteneur et un mode de panne pour trois appels. |
| **Journalisation sur la sortie standard** | `docker compose logs -f order-service` suffit ; pas de fichier a aller chercher dans un volume. |

### Les details qui comptent

- **SQLite et les decimales** : les montants sont stockes en centimes (entier),
  car SQLite n'a pas de type `decimal` et comparerait les prix comme du texte
  (« 9500 » > « 450000 »). Les dates sont stockees en millisecondes Unix, pour la
  meme raison : un `ORDER BY` sur une date texte est faux.
- **Conteneurs Windows et droits d'ecriture** : les images nanoserver tournent
  sous `ContainerUser`, qui ne peut pas ecrire partout. Chaque Dockerfile accorde
  explicitement le droit d'ecrire dans `C:\app\data` (`icacls`). Sans cette ligne,
  le premier demarrage echoue sur un refus d'acces.
- **Concurrence sur le stock** : SQLite n'a pas de `rowversion`. Chaque produit
  porte un GUID regenere a chaque enregistrement, utilise comme jeton de
  concurrence : deux reservations simultanees sur le meme produit font echouer la
  seconde avec un 409 au lieu de survendre.

---

## 7. Exploitation

```powershell
docker compose ps
```

```powershell
docker compose logs -f --tail 50 order-service
```

```powershell
docker compose restart order-service
```

```powershell
docker compose down
```

**Sauvegarde.** Tout l'etat de la plateforme tient dans les quatre fichiers
`.db` : il suffit de copier le dossier de donnees, conteneurs arretes.

```powershell
docker compose stop
Copy-Item C:\microservices\data -Destination "C:\microservices\sauvegardes\$(Get-Date -Format yyyy-MM-dd)" -Recurse
docker compose start
```

**Repartir de zero** (supprime les donnees, recree le jeu de demonstration) :

```powershell
docker compose down
Remove-Item C:\microservices\data\*\*.db -Force
docker compose up -d
```

### Dependannage

| Symptome | Cause probable et solution |
|---|---|
| Un conteneur reste `unhealthy` | `docker compose logs <service>`. Souvent un refus d'ecriture sur le dossier de donnees monte. |
| `no matching manifest for windows` | Le tag d'image ne correspond pas a l'hote : `ltsc2025` pour Server 2025, `ltsc2022` pour Server 2022. |
| L'interface repond `401` partout | Les conteneurs n'ont pas la meme `JWT_KEY`. Verifier le `.env` puis `docker compose up -d --force-recreate`. |
| `503 Service indisponible` sur une confirmation | `catalog-service` ne repond pas. Le message nomme le service en cause ; verifier ses logs. |
| Le port 80 est deja pris | Mettre `HTTP_PORT=8080` dans le `.env`, relancer `1-setup-serveur.ps1 -Port 8080`, puis `docker compose up -d`. |

---

## 8. Arborescence

```
gest_commande/
├─ docker-compose.yml          les 5 conteneurs
├─ .env.example                secrets et chemins a renseigner
├─ deploy/
│  ├─ 1-setup-serveur.ps1      Docker, dossiers, reseau, pare-feu
│  └─ 2-deployer.ps1           build, demarrage, verification
└─ src/
   ├─ Shared/                  socle commun (securite, erreurs, sante, pagination)
   ├─ Gateway/                 routage + interface web (wwwroot)
   ├─ IdentityService/         comptes, roles, jetons
   ├─ CatalogService/          produits, prix, stock, reservations
   ├─ OrderService/            clients, commandes, cycle de vie
   └─ DeliveryService/         livreurs, bordereaux, notifications
```

`Shared` n'est pas un microservice : c'est une bibliotheque compilee dans chaque
image, qui ne contient aucune regle metier, uniquement de la plomberie identique
partout (validation des jetons, format des erreurs, sondes de sante, pagination).

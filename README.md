# BAIS — Guide de jeu et de serveur

Ce dépôt contient un projet Unity (FishNet) avec un client jouable et un serveur dédié. Ce guide explique comment jouer, héberger une partie (serveur ou hôte), et utiliser les options en ligne de commande.

## Prérequis
- Unity 6000.2.8f1 (version utilisée pour le projet)
- .NET/Mono installés via Unity (automatique)
- Réseau: ouvrir le port UDP 7770 sur l’hôte/serveur par défaut (configurable)

## Comment jouer (client)
- Lancer le client depuis l’éditeur Unity ou le binaire de client fourni.
- Contrôles (par défaut):
  - Déplacement: axes `Horizontal` (clavier: A/D ou flèches gauche/droite)
  - Saut: bouton `Jump` (espace par défaut)
  - Viser: souris (le bras suit le curseur)
  - Tirer: clic gauche souris (ou gâchette droite manette)

Notes:
- Le pseudo affiché au‑dessus du joueur est synchronisé en réseau. Par défaut, s’il n’est pas défini, il sera `Player{OwnerId}`. Le script lit la clé `player_nickname` dans les PlayerPrefs côté client si vous ajoutez un UI/flux pour la changer.

## Lancer depuis l’éditeur Unity
La scène contient un `NetworkManager` et un composant `SimpleFishNetBootstrap` qui pilote le démarrage.

Dans l’inspecteur de `SimpleFishNetBootstrap`:
- `Mode`:
  - Server: démarre un serveur dédié (pas de rendu client)
  - Client: démarre un client et se connecte à l’adresse/port configurés
  - Host: lance serveur + client local
- `Auto Start`: permet de démarrer automatiquement à l’Awake (utile pour tests locaux)
- `Client Settings`:
  - `Address`: IP/nom d’hôte à joindre (ex: 127.0.0.1)
  - `Port`: 7770 par défaut (transport FishNet Tugboat)

Bouton contexte "Start Now" sur le composant: démarre immédiatement selon le `Mode` sélectionné.

## Lancer un serveur dédié (build headless)

Vous pouvez utiliser les binaires fournis à la racine du projet ou builder le vôtre depuis Unity (Server build).

### Options en ligne de commande supportées
Le `SimpleFishNetBootstrap` lit les options suivantes:
- `-server` | `-client` | `-host` — fixe le mode
- `-autostart` | `-noautostart` — démarre automatiquement à l’Awake
- `-autostartclient` — auto‑start du client si mode Client/Host
- `-address <ip>` — adresse du serveur à joindre (client)
- `-port <num>` — port réseau (par défaut 7770)
- `-tickrate <num>` — tick rate réseau (par ex. 30)
- `-nologs` — désactive le log réseau périodique
- `-loginterval <sec>` — intervalle logs réseau (min 0.1)

Unity headless:
- `-batchmode -nographics` — exécution sans rendu (recommandé pour serveur)

### Exemples

#### Linux (x86_64)
```bash
chmod +x ./server.x86_64
./server.x86_64 -batchmode -nographics -server -autostart -port 7770
```

#### Windows (Unity Player)
Depuis un build Windows de votre serveur (ex: `BAIS_Server.exe`):
```powershell
./BAIS_Server.exe -batchmode -nographics -server -autostart -port 7770
```

## Rejoindre un serveur existant (client autonome)

Lancer le client en précisant l’adresse/port:

### Linux/Windows (client build)
```bash
./ClientExecutable -client -autostart -address 127.0.0.1 -port 7770
```

Sinon, depuis l’éditeur Unity, régler `Mode = Client`, `Auto Start Client On Awake` et mettre l’`Address` souhaitée, puis Play.

## Héberger en local (Host)
Le mode Host lance un serveur et connecte un client local dans la même instance (utile pour dev/test).

Exemple CLI:
```bash
./ClientExecutable -host -autostart -port 7770
```

## Réseau et pare‑feu
- Transport: FishNet Tugboat (UDP)
- Port par défaut: 7770/UDP
- Ouvrez/Redirigez le port 7770/UDP sur le routeur/pare‑feu de la machine serveur
- Pour changer de port, utilisez `-port <num>` côté serveur et côté clients

## Build rapides
1. Ouvrez le projet dans Unity 6000.2.8f1
2. File > Build Profiles
   - Client: build standard
   - Serveur: build headless (ajoutez `-batchmode -nographics` à la ligne de commande au lancement)

## Dépannage
- "No NetworkManager found": assurez‑vous que la scène active contient un `NetworkManager`.
- Connexion impossible: vérifiez l’IP, le port, et que le serveur écoute bien (logs côté serveur).
- Latence/TPU: ajustez `-tickrate` selon la charge (ex: 20–60).
- Pseudos non visibles: la clé PlayerPrefs `player_nickname` doit être définie côté client (sinon valeur par défaut `Player{OwnerId}`).
- Port utilisé: si 7770 est occupé, choisissez un autre port et ouvrez‑le côté pare‑feu/routeur.

---

Crédits: Unity, FishNet. Voir le code `Assets/Scripts/SimpleFishNetBootstrap.cs` pour les détails d’amorçage réseau, et `Assets/Scripts/PlayerController.cs` pour les contrôles gameplay.

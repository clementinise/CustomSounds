# CustomSounds
## What It Does:
This mod lets you replace the game's default sounds with your own .wav files. It's a simple way to edit any of the game's audio.

## How to Install:

- Use any compatible mod manager for the easiest installation.

OR

- Make sure you have [BepInEx](https://thunderstore.io/c/lethal-company/p/BepInEx/BepInExPack/) & [LCSoundTool](https://thunderstore.io/c/lethal-company/p/no00ob/LCSoundTool/) installed.
- Download this mod and put it in your BepInEx plugins folder. (`\GAME_LOCATION\Lethal Company\BepInEx\plugins`)
- Put your matching .wav files in the "CustomSounds" directory.

## How to Use:
The mod automatically replaces game sounds with the .wav files from the "CustomSounds" folder. Make sure your file names match the in-game sounds you want to replace.

## Terminal Commands:
- `CUSTOMSOUNDS LIST`: Displays all currently loaded sounds
- `CUSTOMSOUNDS RELOAD`: Reloads and applies sounds from the 'CustomSounds' folder and its subfolders
- `CUSTOMSOUNDS REVERT`: Unloads all custom sounds and restores original game sounds
- `CUSTOMSOUNDS HELP`: Provides a list of all CustomSounds commands

## Syncing Custom Sounds with Clients (Experimental Feature):
CustomSounds now introduces an experimental feature allowing the host to sync their custom sounds with all clients in the lobby. This feature ensures that everyone in the game experiences the same custom audio environment. However, it requires all clients to have the CustomSounds mod installed.

### How to Sync:
As a host, you can initiate the sync process through terminal commands. This will send your custom sounds to all connected clients who have CustomSounds installed.

### Terminal Commands for Sync:
- `CUSTOMSOUNDS SYNC`: Starts the synchronization process of custom sounds with all clients.
- `CUSTOMSOUNDS FORCE-UNSYNC`: Forces all clients to unsync and revert to their original sounds.
- `CUSTOMSOUNDS UNSYNC`: Allows clients to manually unsync from the host's custom sounds and revert to their original audio setup.

When the host initiates a sync, clients will receive a notification to accept the sync request. On acceptance, custom sounds from the host will be downloaded and applied to the client's game.

***Note:*** *The sync feature is experimental and might not work as expected in all scenarios. Feedback and bug reports are appreciated to improve this functionality!*

## For Sound Packs Creator
To make installation easier, you will need to organize your release as follows:

```
- manifest.json
- README.md
- CHANGELOG.md (Optional)
- BepInEx
    - plugins
        - CustomSounds
            - <YourSoundPackName>
                - [Insert All Audio Files Here]
```

*For a better understanding, you can refer to the folder structure of [MinecraftCompany Doors](https://thunderstore.io/c/lethal-company/p/Clementinise/MinecraftCompany_Doors/)*

## Tips
You can utilize [LCSoundTool](https://thunderstore.io/c/lethal-company/p/no00ob/LCSoundTool/) and its F5 logging feature to identify the name of the audio you want to replace.

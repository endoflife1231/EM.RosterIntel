# Installation

## Requirements

- Esports Manager 2026 on Windows x64.
- BepInEx 6 for Unity IL2CPP x64.
- The game must be launched once after installing BepInEx so that its folders and IL2CPP interop files are generated.

## 1. Find the game directory

In Steam:

1. Open **Library**.
2. Right-click **Esports Manager 2026**.
3. Select **Manage → Browse local files**.

The opened folder is the game directory. It contains the game executable.

## 2. Install BepInEx

Follow the official BepInEx IL2CPP guide:

https://docs.bepinex.dev/master/articles/user_guide/installation/unity_il2cpp.html

For the standard Windows release of the game, use the current `Unity.IL2CPP-win-x64` build.

Extract the BepInEx archive directly into the game directory. Start the game once and close it. The first launch can take longer because BepInEx generates IL2CPP support files.

After this step, the following folder should exist:

```text
<GAME DIRECTORY>\BepInEx\plugins
```

## 3. Install EM Roster Intel

1. Close the game completely.
2. Remove an older copy, if present:

   ```text
   <GAME DIRECTORY>\BepInEx\plugins\EM.RosterIntel
   ```

3. Extract the `EM.RosterIntel` folder from the release ZIP into:

   ```text
   <GAME DIRECTORY>\BepInEx\plugins\
   ```

4. Verify the final path:

   ```text
   <GAME DIRECTORY>\BepInEx\plugins\EM.RosterIntel\EM.RosterIntel.dll
   ```

Do not place the complete release ZIP in the `plugins` folder. Extract it first.

## 4. Verify the installation

1. Start the game.
2. Open the squad screen.
3. Press `F9`.
4. Check the BepInEx log for a line containing:

   ```text
   EM Roster Intel 1.0.0 loading.
   ```

Depending on the BepInEx build, the log is normally located at one of these paths:

```text
<GAME DIRECTORY>\BepInEx\LogOutput.log
<GAME DIRECTORY>\BepInEx\LogOutput.txt
```

## Using the full report

1. Open your own team roster.
2. If statistics are incomplete, open the statistics pages for all five starters.
3. To populate Transfer Radar, open several other teams' rosters.
4. Return to your own roster and open the **Transfers** tab.

## Updating

1. Close the game.
2. Delete the old `BepInEx\plugins\EM.RosterIntel` folder.
3. Extract the new `EM.RosterIntel` folder into `BepInEx\plugins`.
4. Keep the configuration file unless the release notes explicitly require a reset.

## Resetting settings

Close the game and delete:

```text
<GAME DIRECTORY>\BepInEx\config\com.dignityty.esm26.rosterintel.cfg
```

The file will be recreated with default settings on the next launch.

## Uninstalling

Close the game and delete:

```text
<GAME DIRECTORY>\BepInEx\plugins\EM.RosterIntel
```

Optionally delete the configuration file shown above.

## Troubleshooting

### The overlay does not appear

- Press `F9`.
- Open the squad screen and wait for player cards to load.
- Confirm the DLL path exactly matches the path shown above.
- Confirm you installed the **Unity IL2CPP x64** BepInEx build, not a Mono build.
- Confirm BepInEx itself loads by checking its log.
- Check whether a game update was released after the mod version.

### Transfer Radar is empty

Open several other teams' squad pages first, then return to your own team.

### The report has low confidence

Open the statistics pages for the five starters. The mod can fall back to attributes when match statistics have not been observed, but the result is less objective.

### The game updated

Reflection-based mods can stop working after game updates. Keep the old DLL backed up and check the project's Releases and Issues pages before replacing files.

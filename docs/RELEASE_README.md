# EM Roster Intel v1.0.0

EM Roster Intel is an unofficial, read-only roster analytics mod for **Esports Manager 2026**. It evaluates the active roster, explains player roles, highlights weak points, suggests conservative bench swaps, and builds a sport-only Transfer Radar from teams viewed during the current session.

## Compatibility target

- Esports Manager 2026 patch `1.0.21`.
- Unity `6000.3.12f1` from the original release metadata.
- BepInEx 6 for Unity IL2CPP x64 on Windows.

Game updates can change internal IL2CPP types and may break the mod.

## Install

1. Close the game.
2. Install BepInEx 6 `Unity.IL2CPP-win-x64` by following the official guide:
   https://docs.bepinex.dev/master/articles/user_guide/installation/unity_il2cpp.html
3. Start the game once after installing BepInEx, then close it.
4. Extract the `EM.RosterIntel` folder into:

   ```text
   <GAME DIRECTORY>\BepInEx\plugins\
   ```

5. Confirm the final DLL path:

   ```text
   <GAME DIRECTORY>\BepInEx\plugins\EM.RosterIntel\EM.RosterIntel.dll
   ```

6. Start the game, open the squad screen, and press `F9` if the overlay is hidden.

Detailed instructions are in `INSTALL.md`. Russian instructions are in `INSTALL-RU.md` and `INSTALL-RU.txt`.

## Controls

| Action | Control |
|---|---|
| Show or hide | `F9` |
| Move the window | drag the top bar |
| Resize | `-` / `+` |
| Minimize or restore | `_` / `□` |
| Hide | `×` |
| Reset position and size | `Backspace` |

## Transfer Radar

Transfer Radar evaluates sporting fit only. It does not account for transfer fees, salary, contracts, buyout clauses, club willingness, or player interest.

To populate it, open several other teams' squad pages, return to your own team, and open the **Transfers** tab.

## Data quality

For the most complete report, open the statistics pages for all five starters. If match statistics have not been observed, the mod can use player attributes, but the result is less objective.

## Safety model

The mod is designed to be read-only. It does not intentionally write to saves, automatically change the roster, patch match simulation, or modify the game's standard tables and windows.

## Troubleshooting

If the overlay does not appear:

- press `F9`;
- open the squad screen and wait for player cards to load;
- verify the DLL path shown above;
- verify that the BepInEx build is Unity IL2CPP x64, not Mono;
- check `BepInEx\LogOutput.log` or `BepInEx\LogOutput.txt` for:

  ```text
  EM Roster Intel 1.0.0 loading.
  ```

## Disclaimer

EM Roster Intel is an unofficial community-made modification. It is not affiliated with, endorsed by, sponsored by, or approved by Neurona Games, indie.io, Valve, Steam, or the BepInEx project. Esports Manager 2026 and all related names, trademarks, and assets belong to their respective owners.

Use mods at your own risk and back up important saves.

## License

The mod source is released under the MIT License. See `LICENSE.txt`. The license does not grant rights to Esports Manager 2026, BepInEx, Unity, or third-party assets and binaries.

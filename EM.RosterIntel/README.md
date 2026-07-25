# EM Roster Intel

EM Roster Intel is an unofficial, read-only roster analytics mod for **Esports Manager 2026**. It evaluates the active roster, explains player roles, highlights weak points, suggests conservative bench swaps, and builds a sport-only Transfer Radar from teams you have viewed.

> **Important:** Transfer Radar evaluates sporting fit only. It does not account for transfer fees, salary, contract length, buyout clauses, club willingness, or player interest.

## Release status

| Component | Target |
|---|---|
| Mod version | `1.0.0` |
| Game | Esports Manager 2026 |
| Game patch | `1.0.21` compatibility target |
| Unity | `6000.3.12f1` from the original release metadata |
| Mod loader | BepInEx 6, Unity IL2CPP, Windows x64 |
| Plugin ID | `com.dignityty.esm26.rosterintel` |

Game updates can change IL2CPP types and break reflection-based mods. Compatibility with future patches is not guaranteed.

## Features

### Roster analysis

- detects the currently managed team when the active save changes;
- evaluates team fit, firepower, IGL contribution, AWP strength, utility, form, morale, and performance;
- explains player roles and archetypes;
- identifies the weakest link and produces a roster verdict;
- recommends bench swaps only when the projected role fit and team fit improve enough;
- protects a strong primary AWPer or captain from low-value automatic replacement suggestions.

### Transfer Radar

Transfer Radar evaluates:

- role compatibility;
- player strength and available statistics;
- projected fit with the active roster;
- whether the move preserves roles, requires role changes, or causes a wider rebuild.

To populate the radar, open several other teams' squad pages, then return to your own team and open the **Transfers** tab.

### Tabs

- **Roster** — team profile, verdict, starters, and bench.
- **Transfers** — viewed candidates ranked by sporting fit.
- **Details** — data sources, statistical coverage, and confidence information.
- **Help** — role explanations, terminology, and controls.

## Installation

1. Close the game.
2. Install **BepInEx 6 for Unity IL2CPP x64** by following the [official IL2CPP installation guide](https://docs.bepinex.dev/master/articles/user_guide/installation/unity_il2cpp.html).
3. Start the game once after installing BepInEx, then close it. The first IL2CPP run generates required files and may take longer than normal.
4. Download `EM.RosterIntel-v1.0.0.zip` from the GitHub Releases page.
5. Extract the `EM.RosterIntel` folder into:

   ```text
   <GAME DIRECTORY>\BepInEx\plugins\
   ```

6. Confirm the final path is:

   ```text
   <GAME DIRECTORY>\BepInEx\plugins\EM.RosterIntel\EM.RosterIntel.dll
   ```

7. Start the game, open the squad screen, and press `F9` if the overlay is hidden.

See [INSTALL.md](INSTALL.md) for detailed installation, updating, removal, and troubleshooting instructions. A Russian guide is available at [docs/INSTALL-RU.md](docs/INSTALL-RU.md).

## Controls

| Action | Control |
|---|---|
| Show or hide the overlay | `F9` |
| Move the window | drag the top bar |
| Decrease or increase size | `-` / `+` |
| Minimize or restore | `_` / `□` |
| Hide the window | `×` |
| Reset position and size | `Backspace` |

## Reading the report

- `91 maps · r1.19` — the report uses 91 maps with an average rating of 1.19.
- `player card` — data was read directly from the player's profile/statistics view.
- `attributes only` — player attributes are available, but match history has not been captured.
- `same role` — the candidate can replace the current player without changing team roles.
- `role change` — the candidate is strong, but responsibilities must be redistributed.
- `rebuild` — the transfer changes the IGL, core structure, or team style.
- `AWP covered` — a strong sniper was found, but the team already has a reliable primary AWPer.

## Data and safety model

The mod is designed to be read-only:

- it does not intentionally write to save files;
- it does not automatically change the roster;
- it does not patch match simulation;
- it does not modify the game's standard tables or windows;
- it reads roster and statistics data through passive reflection and read-only Harmony postfix hooks;
- it does not contain an online service, telemetry client, updater, or account system.

The analysis quality depends on the data the game has exposed during the current session. If statistics are missing, open the statistics pages for the five starters before relying on the report.

## Configuration

After the first successful launch, BepInEx creates:

```text
<GAME DIRECTORY>\BepInEx\config\com.dignityty.esm26.rosterintel.cfg
```

Delete this file while the game is closed to restore default settings.

## Building from source

### Prerequisites

- Windows x64;
- a .NET SDK capable of targeting `net6.0`;
- Esports Manager 2026 installed locally;
- BepInEx 6 Unity IL2CPP x64 installed and launched at least once so that `BepInEx\interop` exists.

The repository does **not** redistribute game assemblies, BepInEx binaries, or generated IL2CPP interop assemblies.

### Build

1. Copy `local.props.example` to `local.props`.
2. Set `GameDir` to your local game directory.
3. Run:

   ```powershell
   dotnet build EM.RosterIntel.sln -c Release
   ```

To build and copy the plugin directly to the local BepInEx plugins folder:

```powershell
./scripts/build.ps1 -GameDir "C:\Path\To\Esports Manager 2026" -Deploy
```

To create a release ZIP:

```powershell
./scripts/package-release.ps1 -GameDir "C:\Path\To\Esports Manager 2026" -Version "1.0.0"
```

Build references are resolved from the user's local BepInEx installation. See [BUILDING.md](BUILDING.md) for details.

## Screenshots

Screenshots are not bundled in this initial repository. To add them later:

1. save PNG files under `docs/screenshots/`;
2. recommended names: `roster-overview.png`, `transfer-radar.png`, and `details-audit.png`;
3. add Markdown images here, for example:

```markdown
![Roster overview](docs/screenshots/roster-overview.png)
![Transfer Radar](docs/screenshots/transfer-radar.png)
```

Use screenshots that do not expose personal paths, Steam account details, private save names, or unrelated overlays.

## Source provenance

The original archive supplied for this repository contained the compiled `EM.RosterIntel.dll` but not the original source project. The files under `src/` were reconstructed from the version `1.0.0` assembly and then cleaned for readability. See [SOURCE_NOTICE.md](SOURCE_NOTICE.md) before modifying or publishing the source.

## Contributing

Bug reports and pull requests are welcome. Do not commit game files, BepInEx binaries, generated interop assemblies, logs containing personal paths, or copyrighted game assets. See [CONTRIBUTING.md](CONTRIBUTING.md).

## Disclaimer

EM Roster Intel is an unofficial community-made modification. It is not affiliated with, endorsed by, sponsored by, or approved by Neurona Games, indie.io, Valve, Steam, or the BepInEx project. Esports Manager 2026 and all related names, trademarks, and assets belong to their respective owners.

Use mods at your own risk. Back up important saves before installing any third-party modification.

## License

The mod source code in this repository is available under the [MIT License](LICENSE). This license does not grant rights to Esports Manager 2026, BepInEx, Unity, or any third-party assets or binaries.

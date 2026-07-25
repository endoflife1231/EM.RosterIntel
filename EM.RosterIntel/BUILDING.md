# Building from source

## Why local references are required

EM Roster Intel is a BepInEx 6 IL2CPP plugin. Its build depends on BepInEx and Unity interop assemblies generated on the user's machine after the first BepInEx launch. These binaries are not stored in this repository.

## Prerequisites

- Windows x64;
- a .NET SDK capable of targeting `net6.0`;
- Esports Manager 2026;
- BepInEx 6 `Unity.IL2CPP-win-x64` installed in the game directory;
- one successful game launch after BepInEx installation.

Expected local files include:

```text
<GAME DIRECTORY>\BepInEx\core\BepInEx.Core.dll
<GAME DIRECTORY>\BepInEx\core\BepInEx.Unity.IL2CPP.dll
<GAME DIRECTORY>\BepInEx\core\0Harmony.dll
<GAME DIRECTORY>\BepInEx\core\Il2CppInterop.Runtime.dll
<GAME DIRECTORY>\BepInEx\interop\UnityEngine.CoreModule.dll
<GAME DIRECTORY>\BepInEx\interop\UnityEngine.IMGUIModule.dll
<GAME DIRECTORY>\BepInEx\interop\UnityEngine.InputLegacyModule.dll
```

## Configure the local path

Copy the example file:

```powershell
Copy-Item local.props.example local.props
```

Edit `local.props`:

```xml
<Project>
  <PropertyGroup>
    <GameDir>C:\Path\To\Esports Manager 2026</GameDir>
  </PropertyGroup>
</Project>
```

`local.props` is ignored by Git and must not be committed.

## Build

```powershell
dotnet build EM.RosterIntel.sln -c Release
```

Output:

```text
src\EM.RosterIntel\bin\Release\net6.0\EM.RosterIntel.dll
```

## Build and deploy locally

```powershell
./scripts/build.ps1 -GameDir "C:\Path\To\Esports Manager 2026" -Deploy
```

When `-Deploy` is used, the build copies only the plugin DLL to:

```text
<GAME DIRECTORY>\BepInEx\plugins\EM.RosterIntel\EM.RosterIntel.dll
```

## Package a release

```powershell
./scripts/package-release.ps1 -GameDir "C:\Path\To\Esports Manager 2026" -Version "1.0.0"
```

The script creates:

```text
artifacts\EM.RosterIntel-v1.0.0.zip
```

## Important restrictions

Do not commit or redistribute:

- files from the game installation;
- generated `BepInEx\interop` assemblies;
- BepInEx binaries;
- save files;
- logs containing personal paths or account information;
- game screenshots or assets unless you are permitted to distribute them.

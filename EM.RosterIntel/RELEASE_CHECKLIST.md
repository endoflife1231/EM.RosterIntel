# Release checklist

Before publishing a GitHub release:

- [ ] Build from a clean checkout using `local.props`.
- [ ] Confirm the plugin loads on the game patch listed in `README.md`.
- [ ] Confirm `EM Roster Intel 1.0.0 loading.` appears in the BepInEx log.
- [ ] Open the squad page and verify `F9`, dragging, resizing, minimizing, and reset controls.
- [ ] Verify roster analysis with statistics for all five starters.
- [ ] Open several opponent rosters and verify Transfer Radar.
- [ ] Confirm the mod does not modify the save or roster.
- [ ] Run `scripts/package-release.ps1`.
- [ ] Inspect the ZIP: its top-level folder must be `EM.RosterIntel`.
- [ ] Confirm the ZIP contains only the plugin DLL, README, installation guides, license, and changelog.
- [ ] Create tag `v1.0.0` and upload `EM.RosterIntel-v1.0.0.zip` as a GitHub Release asset.
- [ ] Do not commit the release DLL, ZIP, game files, BepInEx files, or interop assemblies to the source repository.
- [ ] Optionally add screenshots under `docs/screenshots/` and update the README.

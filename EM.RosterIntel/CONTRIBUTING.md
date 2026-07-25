# Contributing

## Bug reports

Include:

- EM Roster Intel version;
- exact Esports Manager 2026 patch;
- complete BepInEx 6 build identifier;
- Windows version;
- steps to reproduce;
- expected and actual behavior;
- the relevant BepInEx log section.

Remove personal paths, Steam account information, save names, tokens, and unrelated logs before posting.

## Pull requests

- Keep the plugin read-only unless a change is explicitly discussed first.
- Do not add automatic save or roster modifications.
- Do not commit game assemblies, BepInEx binaries, generated interop assemblies, save files, or copyrighted game assets.
- Keep public APIs and configuration keys backward-compatible where practical.
- Update `CHANGELOG.md` for user-visible changes.
- Build locally and perform an in-game smoke test before requesting review.

## Code style

The project uses `.editorconfig`. Prefer small focused changes, clear names, defensive reflection, and actionable log messages.

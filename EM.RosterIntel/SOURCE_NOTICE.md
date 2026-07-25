# Source provenance notice

The original release archive used to prepare this repository contained:

- `EM.RosterIntel.dll` version `1.0.0`;
- a Russian README;
- a Russian installation text file.

It did **not** contain the original C# solution or project files.

The source files under `src/` were reconstructed from the supplied assembly with ILSpy and then mechanically cleaned to remove decompiler diagnostics and restore ordinary interpolated logging calls. The following information cannot be recovered reliably from a compiled assembly:

- original comments and documentation comments;
- original local variable names in all cases;
- original formatting and file layout;
- some compiler-level source constructs;
- the exact original project configuration;
- a guarantee that rebuilding produces a byte-for-byte identical DLL.

The supplied version `1.0.0` DLL remains the canonical binary for the packaged release. Before publishing this repository, the maintainer should replace the reconstructed source with the original source if it still exists and run a clean build and in-game smoke test.

This notice is included to avoid presenting reconstructed code as an exact copy of an unavailable original source tree.

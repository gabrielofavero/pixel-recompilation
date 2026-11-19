# Windows installer target

Use the same single-executable Inno Setup shape as `z0mb1es-rec0mp1led`: a per-user wizard plus a bundled self-contained .NET preparation helper. The player selects their own `LIVE` package. The setup must not include the package, original assembly, content, or prepared derivatives, and must run offline without Python, the .NET SDK, or a compiler on the player's machine.

The installer is an implementation gate in the [initial plan](../docs/plans/in-progress/2026-11-10-initial-implementation.md). No setup artifact exists yet because the game runtime has not passed its launch gate.


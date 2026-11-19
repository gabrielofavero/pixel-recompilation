Record each guarded local IL change here with its original assembly hash, method signature, method IL hash, reason, and validation result.

## Desktop input enhancement

`desktop-input-v1` is installed by `src/Port.Prepare/Patches/DesktopInputPatch.cs` and implemented in the separate `src/Port.Hooks/` assembly. It adds prefix/postfix calls and redirects the single keyboard sample in `LowLevel.CLASS_GAMEPAD.Update`, retaining the original input-reader body. It injects no rendering changes.

The supported original executable SHA-256 is `4f8fe508c6ae7205726acc359abd321f53cc8ebff945c79d50b6190a95244e85`. `config/patch-anchors.json` records the Update method token, full signature, and original IL SHA-256; preparation verifies these before installing hooks. Binding/menu tests check retention of original opcodes, absence of visual hooks, and rejection of changed anchors. See [desktop input](../docs/foundation/desktop-input.md) for bindings, runtime bypass, and game-loop validation.


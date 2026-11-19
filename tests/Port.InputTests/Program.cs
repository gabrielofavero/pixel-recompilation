using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Port.Hooks;

int checks = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new InvalidDataException(message);
    checks++;
}
KeyboardState Translate(bool game, bool pause, bool start, params Keys[] keys) =>
    KeyboardScheme.Translate(new KeyboardState(keys), game, pause, start);

foreach (var (input, expected) in new[] { (Keys.W, Keys.Up), (Keys.A, Keys.Left), (Keys.S, Keys.Down), (Keys.D, Keys.Right),
    (Keys.Up, Keys.Up), (Keys.Left, Keys.Left), (Keys.Down, Keys.Down), (Keys.Right, Keys.Right), (Keys.Space, Keys.A),
    (Keys.Enter, Keys.A), (Keys.E, Keys.B), (Keys.Back, Keys.B), (Keys.LeftShift, Keys.X), (Keys.RightShift, Keys.X),
    (Keys.Q, Keys.Y), (Keys.Tab, Keys.Subtract), (Keys.PageUp, Keys.PageUp), (Keys.PageDown, Keys.PageDown) })
    Check(Translate(true, false, false, input).GetPressedKeys().SequenceEqual([expected]), $"Binding failed: {input}");
Check(!Translate(true, false, false, Keys.Space).IsKeyDown(Keys.Space), "Space must jump without pausing.");
Check(!Translate(true, false, false, Keys.A).IsKeyDown(Keys.A), "A movement must not also jump.");
Check(Translate(true, false, false, Keys.Escape).IsKeyDown(Keys.Space), "Escape must pause gameplay.");
Check(Translate(true, true, false, Keys.Escape).IsKeyDown(Keys.Space), "Escape must resume paused gameplay.");
Check(Translate(true, true, false, Keys.Back).IsKeyDown(Keys.Escape), "Backspace must return to title while paused.");
Check(!Translate(true, false, false, Keys.Back).IsKeyDown(Keys.Escape), "Backspace must not quit active gameplay.");
Check(Translate(false, false, false, Keys.Escape).IsKeyDown(Keys.B), "Escape must cancel dialogs and menus.");
Check(Translate(false, false, true, Keys.Enter).IsKeyDown(Keys.Space), "Enter must select the keyboard player.");
Check(!Translate(false, false, false, Keys.Enter).IsKeyDown(Keys.Space), "Enter must not pause outside the start prompt.");
Check(Translate(true, false, false, Keys.F1, Keys.Y, Keys.X, Keys.B).GetPressedKeys().Length == 0,
    "Legacy letter bindings must not leak into the desktop scheme.");
Check(Translate(true, false, false, Keys.D, Keys.Space, Keys.LeftShift).GetPressedKeys().Length == 3,
    "Move, run, and jump must coexist.");

var menu = new FakeMenu();
var hidden = MenuTargets.Build("TITLE", "SELECTION", menu, false);
Check(hidden.Select(target => target.Id).SequenceEqual(["title-0", "title-2", "quit"]),
    "Unavailable Continue and Purchase options must not be clickable.");
menu.m_continue = true;
var available = MenuTargets.Build("TITLE", "SELECTION", menu, false);
Check(available.Any(target => target.Id == "title-1"), "Continue must be clickable after unlocking progress.");
menu.m_menuChoixMax = 1;
var levels = MenuTargets.Build("CONTINUE", "SELECTION", menu, false);
Check(levels.Select(target => target.Id).SequenceEqual(["level-0", "level-1"]),
    "Locked levels must not be clickable.");
Check(MenuTargets.Build("TITLE", "FADE", menu, false).Count == 0, "Fading menus must not accept clicks.");
Check(MenuTargets.Build("GAME", "", menu, false).Count == 0, "Gameplay clicks must not activate menu actions.");
var pauseTargets = MenuTargets.Build("GAME", "", menu, true);
Check(pauseTargets.Select(target => target.Id).SequenceEqual(["resume", "return-title"]), "Pause mouse actions missing.");
Check(!available[0].Bounds.Intersects(available[1].Bounds), "Adjacent menu targets must not overlap.");
Check(available[0].Bounds.Contains(available[0].Bounds.Center), "Sprite origin/scale conversion failed.");
Check(GameAccess.SpriteBounds(new FakeSprite { m_sprX = 100, m_sprY = 100, m_sprScaleX = 2, m_sprScaleY = 2 }) ==
    new Rectangle(-156, 36, 512, 128), "Scaled sprite bounds must match rendered coordinates.");
Check(MenuTargets.Build("OUTRO", "PAGE1", menu, false).Single().Id == "continue", "Existing outro prompt must be clickable.");
Check(MenuTargets.Build("END_FINISHED", "END2", menu, false).Single().Id == "continue", "Existing ending prompt must be clickable.");
menu.m_timer = 120;
Check(MenuTargets.Build("OUTRO", "PAGE1", menu, false).Count == 0, "Outro prompt must respect its original timer.");
Check(MenuTargets.Build("END_FINISHED", "FADE", menu, false).Count == 0, "Ending fade must ignore mouse input.");

if (args.Length == 1)
{
    var stage = Path.GetFullPath(args[0]);
    var originalPath = Path.Combine(stage, "original/584E07D1/Arkedo Series - 03 PIXEL!.exe");
    var preparedPath = Path.Combine(stage, "run/584E07D1/Arkedo Series - 03 PIXEL!.exe");
    using var original = ModuleDefinition.ReadModule(originalPath);
    using var prepared = ModuleDefinition.ReadModule(preparedPath);
    var oldUpdate = original.GetType("LowLevel.CLASS_GAMEPAD").Methods.Single(method => method.Name == "Update");
    var newUpdate = prepared.GetType("LowLevel.CLASS_GAMEPAD").Methods.Single(method => method.Name == "Update");
    var before = newUpdate.Body.Instructions.Take(2).ToArray();
    var after = newUpdate.Body.Instructions.TakeLast(3).ToArray();
    Check(before[0].OpCode == OpCodes.Ldarg_0 && before[1].Operand is MethodReference { Name: "BeforeUpdate" }, "Prefix missing.");
    Check(after[0].OpCode == OpCodes.Ldarg_0 && after[1].Operand is MethodReference { Name: "AfterUpdate" } &&
        after[2].OpCode == OpCodes.Ret, "Postfix missing.");
    var oldInstructions = oldUpdate.Body.Instructions.SkipLast(1).ToArray();
    var retained = newUpdate.Body.Instructions.Skip(2).SkipLast(3).ToArray();
    Check(oldInstructions.Length == retained.Length, "Input hook must retain the original reader body.");
    for (int i = 0; i < retained.Length; i++)
        Check(oldInstructions[i].OpCode == retained[i].OpCode, $"Input reader opcode changed at {i}.");
    Check(retained.Count(instruction => instruction.Operand is MethodReference method &&
        method.DeclaringType.FullName == "Port.Hooks.DesktopInput" && method.Name == "GetKeyboardState") == 1,
        "Exactly one keyboard sampling site must be redirected.");
    Check(!prepared.Types.SelectMany(type => type.Methods).Where(method => method.HasBody && method.Name is "Draw" or "Display")
        .SelectMany(method => method.Body.Instructions).Any(instruction => instruction.Operand is MethodReference method &&
            method.DeclaringType.Namespace == "Port.Hooks"), "Desktop input must not inject visual hooks.");
    using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(stage, "manifest.json")));
    Check(manifest.RootElement.GetProperty("enhancements").EnumerateArray().Any(value => value.GetString() == DesktopInput.PatchId),
        "Enhancement identity missing from manifest.");
    Check(manifest.RootElement.GetProperty("hooksSha256").GetString() == Convert.ToHexStringLower(SHA256.HashData(
        File.ReadAllBytes(Path.Combine(stage, "run/584E07D1/Port.Hooks.dll")))), "Prepared hook DLL differs from its manifest.");
}
Console.WriteLine($"DESKTOP_INPUT_TESTS_OK checks={checks}");

internal sealed class FakeSprite
{
    public float m_sprX { get; set; } = 640;
    public float m_sprY { get; set; } = 483;
    public float m_sprScaleX { get; set; } = 1;
    public float m_sprScaleY { get; set; } = 1;
    public float m_sprOriginX => 128;
    public float m_sprOriginY => 32;
    public int m_sprFrmWidth => 256;
    public int m_sprFrmHeight => 64;
}
internal sealed class FakeMenu
{
    public bool m_continue { get; set; }
    public bool m_isTrialMode => false;
    public int m_menuChoixMax { get; set; }
    public int m_timer { get; set; }
    public object m_sprButton { get; } = new FakeSprite();
    public object[] m_sprMenuChoix { get; } = Enumerable.Range(0, 4).Select(index =>
        (object)new FakeSprite { m_sprY = 483 + index * 50 }).ToArray();
}

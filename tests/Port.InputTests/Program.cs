using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Microsoft.Xna.Framework.Graphics;
using Port.Bridge;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Port.Hooks;
using DisplayMode = Port.Hooks.DisplayMode;

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
Check(Translate(true, true, false, Keys.Escape).IsKeyDown(Keys.Space) &&
    !Translate(true, true, false, Keys.Escape).IsKeyDown(Keys.Escape), "Escape must resume paused gameplay without returning to title.");
Check(Translate(true, true, false, Keys.P).IsKeyDown(Keys.Space), "P must resume paused gameplay.");
Check(Translate(true, true, false, Keys.Back).IsKeyDown(Keys.Escape), "Backspace must return to title while paused.");
Check(!Translate(true, false, false, Keys.Back).IsKeyDown(Keys.Escape), "Backspace must not quit active gameplay.");
foreach (bool start in new[] { false, true })
    Check(Translate(false, false, start, Keys.Escape).GetPressedKeys().Order().SequenceEqual(new[] { Keys.B, Keys.Escape }.Order()),
        "Escape must cancel dialogs/menus and feed title Exit, just like Backspace.");
foreach (bool paused in new[] { false, true })
    Check(Translate(true, paused, false, Keys.Escape).GetPressedKeys().SequenceEqual([Keys.Space]),
        "Escape must feed only the original Start/pause slot, never B or Back/Select.");
Check(Translate(false, false, false, Keys.Back).GetPressedKeys().Order().SequenceEqual(new[] { Keys.B, Keys.Escape }.Order()),
    "Backspace must feed both the menu Back and title Exit commands.");
Check(Translate(false, false, true, Keys.Enter).IsKeyDown(Keys.Space), "Enter must select the keyboard player.");
Check(!Translate(false, false, false, Keys.Enter).IsKeyDown(Keys.Space), "Enter must not pause outside the start prompt.");
Check(Translate(true, false, false, Keys.F1, Keys.Y, Keys.X, Keys.B).GetPressedKeys().Length == 0,
    "Legacy letter bindings must not leak into the desktop scheme.");
Check(Translate(true, false, false, Keys.D, Keys.Space, Keys.LeftShift).GetPressedKeys().Length == 3,
    "Move, run, and jump must coexist.");

var menu = new FakeMenu();
var promptVectors = PromptVectors.Embedded();
Check(promptVectors.Layouts.Count == 23, "Prompt inventory must retain the native title and submenu Select icons.");
foreach (var (asset, layout) in promptVectors.Layouts)
    for (int index = 0; index < layout.Patches.Length; index++)
        if (layout.Patches[index].Label == "ESC")
            Check(asset == "GAME/Interface/GAME_It_Pause" && index == 0, "ESC artwork must only identify pause/resume.");
Check(!promptVectors.Layouts.ContainsKey("TITLE/TITLE_Bg") && !promptVectors.Layouts.ContainsKey("TITLE/SubmenuBack"),
    "Title and Help/Options must keep the original Back/Select symbol for desktop input.");
Check(promptVectors.Layouts["GAME/Interface/GAME_It_Pause"].Patches.All(patch => patch.Bounds[0] > 640),
    "Pause artwork must keep the original Main Menu Back/Select symbol.");
foreach (var asset in new[] { "TITLE/TITLE_B_button", "CONT/CONT_Bg", "CREDITS/CREDITS_Bg" })
    Check(promptVectors.Layouts[asset].Patches[0].Label == "BSP", "Back/Exit prompts must identify Backspace: " + asset);
foreach (var label in new[] { "E", "Q", "SPC", "ESC", "TAB", "SFT", "BSP", "ENT", "PGU", "PGD" })
{
    var pixels = promptVectors.Render(new PromptPatch { Label = label, Bounds = [0, 0, 128, 128] });
    Check(pixels[0].A == 0 && pixels[28 * 128 + 48].A == 255, "Badge must retain its transparent canvas and original stepped ring.");
    Check(pixels.Where((_, index) => index % 128 is >= 38 and < 90 && index / 128 is >= 43 and < 85).Count(p => p.A == 255) > 100,
        $"Keyboard badge must contain legible lettering: {label}");
}
var vectorsAssembly = typeof(KeyboardScheme).Assembly;
var gaugeLayout = promptVectors.Layouts["GAME/Interface/GAME_It_Jauge"];
var gaugePixels = new Color[1080 * 88];
promptVectors.Paint(gaugePixels, 1080, 88, gaugeLayout);
int GaugeLetterPixels(int frame) => Enumerable.Range(28, 40).Sum(y =>
    Enumerable.Range(292, 32).Count(x => gaugePixels[y * 1080 + frame * 360 + x].A == 255));
Check(GaugeLetterPixels(2) > GaugeLetterPixels(1), "The ready gauge's Q must expand between animation frames.");
Check(Enumerable.Range(0, 88).All(y => gaugePixels.Skip(y * 1080).Take(360).All(pixel => pixel == Color.Transparent)),
    "The Q pulse must leave the charging gauge frame unchanged.");
var gaugeNormal = gaugeLayout.Patches[0].Bounds;
var gaugeExpanded = gaugeLayout.Patches[1].Bounds;
Check(gaugeNormal[0] + gaugeNormal[2] / 2f == gaugeExpanded[0] + gaugeExpanded[2] / 2f - 360 &&
    gaugeNormal[1] + gaugeNormal[3] / 2f == gaugeExpanded[1] + gaugeExpanded[3] / 2f,
    "The gauge's normal and expanded Q badges must share a center.");
string PromptResource(string name)
{
    using var reader = new StreamReader(vectorsAssembly.GetManifestResourceStream("KeyboardPrompts." + name)!);
    return reader.ReadToEnd();
}
try
{
    PromptVectors.Parse(PromptResource("keyboard.svg"), "{\"invalid\":{\"size\":[128,128],\"patches\":[{\"label\":\"SPACE\",\"bounds\":[0,0,128,128]}]}}");
    Check(false, "Four/five-letter badges must be rejected before replacing current art.");
}
catch (InvalidDataException) { checks++; }
var hidden = MenuTargets.Build("TITLE", "SELECTION", menu, false);
Check(hidden.Select(target => target.Id).SequenceEqual(["title-0", "title-2", "quit"]),
    "Unavailable Continue and Purchase options must not be clickable.");
menu.m_continue = true;
var available = MenuTargets.Build("TITLE", "SELECTION", menu, false);
Check(available.Any(target => target.Id == "title-1"), "Continue must be clickable after unlocking progress.");
menu.m_menuChoixMax = 1;
var levels = MenuTargets.Build("CONTINUE", "SELECTION", menu, false);
Check(levels.Select(target => target.Id).SequenceEqual(["level-0", "level-1", "levels-back"]),
    "Locked levels must not be clickable.");
Check(levels.Single(target => target.Id == "levels-back").Action == Keys.B, "Continue's clickable Back prompt must feed the original B slot.");
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

Check(DisplaySettings.Available(1920, 1080).SequenceEqual([new(1280, 720), new(1600, 900), new(1920, 1080)]),
    "1080p displays must hide larger resolutions.");
Check(OptionsSession.Pressed(3) && OptionsSession.Pressed(1) && !OptionsSession.Pressed(2) && !OptionsSession.Pressed(0),
    "Menu navigation must use pressed edges rather than held input.");
Check(DisplaySettings.Available(3840, 2160)[^1] == new Resolution(3840, 2160), "4K must be available on 4K screens.");
Check(DisplaySettings.Available(3440, 1440)[^1] == new Resolution(3440, 1440), "Ultrawide screens must offer their native resolution.");
Check(DisplaySettings.Available(3440, 1440).All(size => size.Width <= 3440 && size.Height <= 1440), "Ultrawide choices must fit both dimensions.");
Check(DisplaySettings.Available(5120, 1440)[^1] == new Resolution(5120, 1440), "32:9 screens must offer their native resolution.");
Check(DisplaySettings.Available(1280, 800).SequenceEqual([new Resolution(1280, 720)]), "Uncommon ratios must keep 16:9 choices.");
Check(DisplaySettings.Available(1024, 600).Single() == new Resolution(1024, 576), "Small displays need a fitting fallback.");
var ultra = DisplaySettings.Fit(3440, 1440);
Check(ultra == new Rectangle(440, 0, 2560, 1440), "Ultrawide output needs centered pillarboxing.");
Check(DisplaySettings.Fit(1280, 1024) == new Rectangle(0, 152, 1280, 720), "4:3 output needs centered letterboxing.");
Check(DisplaySettings.ToLogical(ultra.Center, ultra) == new Point(640, 360), "Scaled mouse center must map to the logical center.");
Check(DisplaySettings.ToLogical(new Point(439, 50), ultra).X < 0, "Black bar clicks must stay outside the logical picture.");
foreach (int width in new[] { 1280, 1720, 2560 })
{
    // A wider selected mode may be presented in a narrower resized window.
    var bounds = DisplaySettings.Fit(1920, 1080, width);
    Check(DisplaySettings.ToLogical(bounds.Center, bounds, width) == new Point(640, 360),
        "Pause mouse coordinates must stay centered over the actual gameplay canvas.");
    int resumeX = bounds.X + (int)Math.Round((850 + (width - 1280) / 2d) * bounds.Width / width);
    var logical = DisplaySettings.ToLogical(new Point(resumeX, bounds.Center.Y), bounds, width);
    Check(Math.Abs(logical.X - 850) <= 1, "Pause Resume hit target must follow the displayed artwork in a resized window.");
}

Check(LegacyGraphics.MapLegacyAddress((TextureAddressMode)1) == TextureAddressMode.Wrap &&
    LegacyGraphics.MapLegacyAddress((TextureAddressMode)2) == TextureAddressMode.Mirror &&
    LegacyGraphics.MapLegacyAddress((TextureAddressMode)3) == TextureAddressMode.Clamp,
    "XNA3 texture address values must be remapped to FNA, including the mirrored gradient.");
string hint = "Use [A], [B], [X], [Y], [LT] or the \"Left Trigger\".";
Check(TutorialHints.Translate(hint, true) == "Use [Space], [E], [Shift], [Q], [Tab] or the [Tab].",
    "Tutorial hints must explain the keyboard equivalents.");
Check(TutorialHints.Translate(hint, false) == hint, "Controller hints must retain the original wording.");
var sign = new FakeHintFont { m_fntText = hint };
TutorialHints.RefreshText(sign, true);
Check(sign.m_fntText.Contains("[Space]") && sign.m_fntText.Contains("[Tab]"), "An open sign must switch immediately to keyboard hints.");
TutorialHints.RefreshText(sign, false);
Check(sign.m_fntText == hint, "Switching back to controller must restore the exact original sign.");
TutorialHints.RefreshText(sign, true);
TutorialHints.RefreshText(sign, true);
Check(sign.m_fntText == TutorialHints.Translate(hint, true), "Repeated keyboard frames must not replace the retained controller text.");
sign.m_fntText = "Next sign: [Y]!";
TutorialHints.RefreshText(sign, true);
Check(sign.m_fntText == "Next sign: [Q]!", "Opening a different sign must replace the retained source text.");
TutorialHints.RefreshText(sign, false);
Check(sign.m_fntText == "Next sign: [Y]!", "A new sign must also restore its own controller wording.");
Check(MenuTargets.Build("CREDITS", "CREDITS", menu, false).Single().Action == Keys.B, "Credits must support mouse Back.");

Check(Ultrawide.WidthFor(3440, 1440) == 1720 && Ultrawide.WidthFor(5120, 1440) == 2560, "Ultrawide view must preserve vertical scale.");
Check(Ultrawide.WidthFor(1920, 1080) == 1280 && Ultrawide.WidthFor(1280, 1024) == 1280, "Narrow screens must keep the original view.");
Check(Ultrawide.Expand("PLATFORM", 0) && Ultrawide.Expand("SIGN", 0), "Platforming and sign dialogue must support wider worlds, including while paused.");
Check(new[] { "SCAN", "ZOOM" }.All(state => Ultrawide.Expand(state, 0)), "Scanning and zoom maps must fill the wider canvas, including while paused.");
Check(!Ultrawide.Expand("ENIGMA", 0), "Authored button puzzles must retain 16:9.");
Check(!Ultrawide.Expand("PLATFORM", 220), "Intro artwork must remain 16:9.");
var camera = Matrix.CreateTranslation(-1360, -400, 0);
var wider = Ultrawide.WidenCamera(camera, 1720, 8000);
Check(wider.M41 == -1140 && wider.M42 == camera.M42 && wider.M11 == 1, "Widening must preserve world center and vertical scale.");
Check(Ultrawide.WidenCamera(Matrix.Identity, 1720, 8000).M41 == 0, "Left level edge must not expose the void.");
Check(Ultrawide.WidenCamera(Matrix.CreateTranslation(-6720, 0, 0), 1720, 8000).M41 == -6280, "Right level edge must not expose the void.");
var mapCamera = Matrix.CreateScale(2.3f) * Matrix.CreateTranslation(500, -120, 0);
var wideMapCamera = Ultrawide.InspectionCamera(mapCamera, 2560);
Check(wideMapCamera.M41 == 1140 && wideMapCamera.M42 == -120 && wideMapCamera.M11 == 2.3f,
    "Zoom maps must keep their unclamped camera, center and zoom scale.");
Check(DisplaySettings.Fit(3440, 1440, 1720) == new Rectangle(0, 0, 3440, 1440), "Wider gameplay must fill a 21:9 screen.");
var shadows = Ultrawide.LightBounds(1720, new Rectangle(300, 200, 400, 300));
Check(shadows.Sum(rect => rect.Width * rect.Height) == 1720 * 720 - 400 * 300, "Darkness must cover all expanded edges outside the light.");
Check(shadows.All(rect => rect.Left >= 0 && rect.Right <= 1720 && rect.Top >= 0 && rect.Bottom <= 720), "Light masks must stay within the canvas.");

string? savedRoot = Environment.GetEnvironmentVariable("PIXEL_PORT_SAVE_DIR");
string settingsRoot = Path.Combine(Path.GetTempPath(), "pixel-options-" + Guid.NewGuid().ToString("N"));
try
{
    Environment.SetEnvironmentVariable("PIXEL_PORT_SAVE_DIR", settingsRoot);
    var sizes = DisplaySettings.Available(1920, 1080);
    Check(DisplaySettings.Load(sizes) == new DisplaySettings(DisplayMode.Borderless, sizes[^1]), "Borderless must be the default.");
    var custom = new DisplaySettings(DisplayMode.Windowed, sizes[0]);
    custom.Save();
    Check(DisplaySettings.Load(sizes) == custom, "Display settings must survive a restart.");
    new DisplaySettings(DisplayMode.Fullscreen, new(3840, 2160)).Save();
    Check(DisplaySettings.Load(sizes).Resolution == sizes[^1], "Settings from a larger monitor must be clamped.");
    File.WriteAllText(DisplaySettings.SettingsPath, "{invalid");
    Check(DisplaySettings.Load(sizes).Mode == DisplayMode.Borderless, "Corrupt settings must fall back safely.");
}
finally
{
    Environment.SetEnvironmentVariable("PIXEL_PORT_SAVE_DIR", savedRoot);
    if (Directory.Exists(settingsRoot))
    {
        File.Delete(Path.Combine(settingsRoot, "display-settings.json"));
        Directory.Delete(settingsRoot);
    }
}

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
            method.DeclaringType.FullName == "Port.Hooks.DesktopInput"), "Desktop input must not inject visual hooks.");
    Check(prepared.GetType("Scale.GameScale").Methods.Single(method => method.Name == "Draw").Body.Instructions
        .Count(instruction => instruction.Operand is MethodReference method && method.DeclaringType.FullName == "Port.Hooks.HelpOptions") == 2,
        "Presentation must bracket the original Draw method.");
    using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(stage, "manifest.json")));
    Check(manifest.RootElement.GetProperty("enhancements").EnumerateArray().Any(value => value.GetString() == DesktopInput.PatchId),
        "Enhancement identity missing from manifest.");
    Check(manifest.RootElement.GetProperty("enhancements").EnumerateArray().Any(value => value.GetString() == HelpOptions.PatchId),
        "Help/options enhancement identity missing from manifest.");
    Check(manifest.RootElement.GetProperty("enhancements").EnumerateArray().Any(value => value.GetString() == Ultrawide.PatchId),
        "Ultrawide enhancement identity missing from manifest.");
    foreach (var (type, name, hook) in new[] { ("Scale.GAME.Level", "DrawTiles", "GetWidth"),
        ("Scale.GAME.Level", "ProcessScanMode", "BeforeScan"),
        ("Scale.GAME.Level", "ProcessScanMode", "GetScanWidth"),
        ("LowLevel.CLASS_SPR", "Ark_SprDisplay", "GetWidthFloat"), ("LowLevel.CLASS_SPR", "Ark_SprDisplay", "DrawSprite"),
        ("LowLevel.CLASS_FNT", "Ark_FntDisplay", "DrawText") })
        Check(prepared.GetType(type).Methods.Single(method => method.Name == name).Body.Instructions
            .Count(instruction => instruction.Operand is MethodReference method && method.DeclaringType.FullName == "Port.Hooks.Ultrawide" && method.Name == hook) == 1,
            $"Missing ultrawide hook: {type}.{name}/{hook}");
    var scanReturns = prepared.GetType("Scale.GAME.Level").Methods.Single(method => method.Name == "ProcessScanMode")
        .Body.Instructions.Where(instruction => instruction.OpCode == OpCodes.Ret).ToArray();
    Check(scanReturns.Length == original.GetType("Scale.GAME.Level").Methods.Single(method => method.Name == "ProcessScanMode")
        .Body.Instructions.Count(instruction => instruction.OpCode == OpCodes.Ret) &&
        scanReturns.All(instruction => instruction.Previous.Operand is MethodReference { Name: "AfterScan" }),
        "Every original scan return must restore the camera and cursor context.");
    Check(prepared.GetType("Scale.GAME.Level").Methods.Single(method => method.Name == "ProcessScanMode").Body.Instructions
        .Count(instruction => instruction.Operand is MethodReference method && method.DeclaringType.FullName == "Port.Hooks.Ultrawide" && method.Name == "GetScanWidthFloat") == 2,
        "Both original right cursor clamp sites must use the scan canvas width.");
    var addressCalls = prepared.GetType("LowLevel.CLASS_SPR").Methods.Single(method => method.Name == "Ark_SprDisplay")
        .Body.Instructions.Where(instruction => instruction.Operand is MethodReference method &&
            method.DeclaringType.FullName == "Port.Bridge.LegacyGraphics" && method.Name is "SetAddressU" or "SetAddressV");
    Check(addressCalls.Count() == 6, "Sprite wrap/mirror/clamp setters must use the compatibility bridge.");
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
internal sealed class FakeHintFont
{
    public string m_fntText = "";
    public void Ark_FntSetText(string value) => m_fntText = value;
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

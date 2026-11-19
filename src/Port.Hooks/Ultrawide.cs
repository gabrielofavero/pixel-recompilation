using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Port.Hooks;

// Keep simulation coordinates at 1280 x 720; scan hit testing temporarily uses the rendered view.
public static class Ultrawide
{
    public const string PatchId = "ultrawide-platforming-v1";
    [ThreadStatic] private static WorldFrame? current;
    [ThreadStatic] private static ScanFrame? scan;
    public static int GetWidth() => current?.Width ?? 1280;
    public static float GetWidthFloat() => GetWidth();

    internal static int WidthFor(int width, int height) => height <= 0 ? 1280 :
        Math.Clamp((int)Math.Round(720d * width / height / 2) * 2, 1280, 2560);

    // Pause freezes the current world; changing its width would move the camera,
    // HUD and dialogue underneath the original pause artwork and its fade.
    internal static bool Expand(string state, int introTimer) =>
        state is "PLATFORM" or "SIGN" or "SCAN" or "ZOOM" && introTimer == 0;

    public static int GetScanWidth() => scan?.Width ?? 1280;
    public static float GetScanWidthFloat() => GetScanWidth();

    public static void BeforeScan(object level)
    {
        if (scan is not null) throw new InvalidOperationException("Nested scan update.");
        var pad = level.GetType().Assembly.GetType("LowLevel.CLASS_GAMEPAD")!.GetField("m_gpad")!.GetValue(null)!;
        var game = ((GameComponent)pad).Game;
        var resolution = HelpOptions.Session(game).Settings.Resolution;
        int width = WidthFor(resolution.Width, resolution.Height);
        if (width > 1280) scan = new ScanFrame(level, width);
    }

    public static void AfterScan(object level)
    {
        scan?.Restore();
        scan = null;
    }

    internal static Matrix InspectionCamera(Matrix original, int width)
    {
        original.M41 += (width - 1280) / 2f;
        return original;
    }

    internal static Matrix WidenCamera(Matrix original, int width, int levelWidth)
    {
        original.M41 = Math.Clamp(original.M41 + (width - 1280) / 2f,
            Math.Min(0, width - levelWidth * original.M11), 0);
        return original;
    }

    internal static void Begin(Game game, object? level, int width)
    {
        if (current is not null) throw new InvalidOperationException("Nested ultrawide rendering frame.");
        if (level is not null && width > 1280) current = new WorldFrame(game, level, width);
    }

    internal static void End()
    {
        current?.Restore();
        current = null;
    }

    public static void DrawSprite(SpriteBatch batch, Texture2D texture, Vector2 position, Rectangle? source,
        Color color, float rotation, Vector2 origin, Vector2 scale, SpriteEffects effects, float depth, object sprite)
    {
        if (current is WorldFrame frame && !GameAccess.Get<bool>(sprite, "m_sprCameraOn"))
        {
            if (ReferenceEquals(sprite, frame.Pause))
            {
                // Extend only the dimmed border. Keep the authored menu at its
                // original scale, centered over the unchanged wider world.
                var region = source ?? texture.Bounds;
                int margin = (frame.Width - 1280) / 2;
                batch.Draw(texture, new Rectangle(0, 0, margin, 720),
                    new Rectangle(region.Left, region.Top, 1, region.Height), color, 0, Vector2.Zero, SpriteEffects.None, depth);
                batch.Draw(texture, new Rectangle(frame.Width - margin, 0, margin, 720),
                    new Rectangle(region.Right - 1, region.Top, 1, region.Height), color, 0, Vector2.Zero, SpriteEffects.None, depth);
            }
            if (ReferenceEquals(sprite, frame.Overlay) || ReferenceEquals(sprite, frame.Fade))
            {
                batch.Draw(texture, new Rectangle(0, 0, frame.Width, 720), null, color, 0, Vector2.Zero, SpriteEffects.None, depth);
                return;
            }
            if (frame.StretchSprites.Contains(sprite))
            {
                batch.Draw(texture, new Rectangle(0, 0, frame.Width, 720), source, color, 0, Vector2.Zero, effects, depth);
                return;
            }
            int contour = Array.IndexOf(frame.Contours, sprite);
            if (contour >= 0)
            {
                var rect = frame.LightContours[contour];
                if (rect.Width > 0 && rect.Height > 0)
                    batch.Draw(texture, rect, null, color, 0, Vector2.Zero, SpriteEffects.None, depth);
                return;
            }
            position.X += ReferenceEquals(sprite, frame.Light) ? frame.CameraDelta : frame.LeftHud.Contains(sprite) ? 0
                : frame.RightHud.Contains(sprite) ? frame.Width - 1280 : (frame.Width - 1280) / 2f;
        }
        batch.Draw(texture, position, source, color, rotation, origin, scale, effects, depth);
    }

    public static void DrawText(SpriteBatch batch, SpriteFont font, string text, Vector2 position,
        Color color, float rotation, Vector2 origin, Vector2 scale, SpriteEffects effects, float depth)
    {
        if (current?.LeftFonts.Contains(font) != true) position.X += (GetWidth() - 1280) / 2f;
        batch.DrawString(font, text, position, color, rotation, origin, scale, effects, depth);
    }

    internal static Rectangle[] LightBounds(int width, Rectangle hole)
    {
        int left = Math.Clamp(hole.Left, 0, width), right = Math.Clamp(hole.Right, 0, width);
        int top = Math.Clamp(hole.Top, 0, 720), bottom = Math.Clamp(hole.Bottom, 0, 720);
        return [new(0, 0, width, top), new(0, bottom, width, 720 - bottom),
            new(0, top, left, bottom - top), new(right, top, width - right, bottom - top)];
    }

    private sealed class ScanFrame
    {
        internal readonly int Width;
        private readonly object cursor;
        private readonly FieldInfo camera, spriteCamera;
        private readonly Matrix original, originalSprite;
        internal ScanFrame(object level, int width)
        {
            Width = width;
            var assembly = level.GetType().Assembly;
            camera = assembly.GetType("Scale.GAME.GAME_Camera")!.GetField("m_camera")!;
            spriteCamera = assembly.GetType("LowLevel.CLASS_SPR")!.GetField("m_sprCamera")!;
            original = (Matrix)camera.GetValue(null)!;
            originalSprite = (Matrix)spriteCamera.GetValue(null)!;
            var expanded = WidenCamera(original, width, GameAccess.Get<int>(level, "Width") * 88);
            camera.SetValue(null, expanded);
            spriteCamera.SetValue(null, expanded);
            cursor = GameAccess.Get(level, "m_sprCursor")!;
            if (!GameAccess.Get<bool>(cursor, "m_sprCameraOn"))
                GameAccess.Set(cursor, "m_sprX", GameAccess.Get<float>(cursor, "m_sprX") + (width - 1280) / 2f);
        }
        internal void Restore()
        {
            if (!GameAccess.Get<bool>(cursor, "m_sprCameraOn"))
                GameAccess.Set(cursor, "m_sprX", GameAccess.Get<float>(cursor, "m_sprX") - (Width - 1280) / 2f);
            camera.SetValue(null, original);
            spriteCamera.SetValue(null, originalSprite);
        }
    }

    private sealed class WorldFrame
    {
        internal readonly int Width;
        internal readonly object Overlay, Fade, Light, Pause;
        internal readonly object[] Contours;
        internal readonly Rectangle[] LightContours;
        internal readonly float CameraDelta;
        internal readonly HashSet<object> LeftHud = new(ReferenceEqualityComparer.Instance);
        internal readonly HashSet<object> RightHud = new(ReferenceEqualityComparer.Instance);
        internal readonly HashSet<object> StretchSprites = new(ReferenceEqualityComparer.Instance);
        internal readonly HashSet<SpriteFont> LeftFonts = new(ReferenceEqualityComparer.Instance);
        private readonly FieldInfo cameraField, spriteCameraField;
        private readonly Matrix originalCamera, originalSpriteCamera;

        internal WorldFrame(Game game, object level, int width)
        {
            Width = width;
            Pause = GameAccess.Get(GameAccess.Get(game, "m_stateComponent")!, "m_sprPause")!;
            foreach (var name in new[] { "m_sprItEnergy", "m_sprItJaugeRemplissage" })
                LeftHud.UnionWith(GameAccess.Get<object[]>(level, name));
            foreach (var name in new[] { "m_sprItLoupe", "m_sprItJauge", "m_sprItJaugeGlow" })
                LeftHud.Add(GameAccess.Get(level, name)!);
            foreach (var name in new[] { "m_sprScanButtonA", "m_sprScanAction" }) LeftHud.Add(GameAccess.Get(level, name)!);
            StretchSprites.Add(GameAccess.Get(level, "m_sprZoomEffect")!);
            RightHud.UnionWith(GameAccess.Get<object[]>(level, "m_sprItTresor"));
            bool zoom = GameAccess.Get(level, "m_state")!.ToString() == "ZOOM";
            if (zoom && GameAccess.Get(level, "map") is object map)
            {
                StretchSprites.Add(GameAccess.Get(map, "m_sprHalo")!);
                LeftHud.Add(GameAccess.Get(map, "m_sprTimer")!);
                foreach (var name in new[] { "m_sprEscape", "m_sprMini", "m_sprMiniBg", "m_sprMiniMarqueur", "m_sprMiniBordure" })
                    if (GameAccess.Get(map, name) is object sprite) RightHud.Add(sprite);
                foreach (var name in new[] { "m_fntTimer", "m_fntTimerOmbre" })
                    LeftFonts.Add(GameAccess.Get<SpriteFont>(GameAccess.Get(map, name)!, "m_fntFont"));
            }
            var assembly = game.GetType().Assembly;
            cameraField = assembly.GetType("Scale.GAME.GAME_Camera")!.GetField("m_camera")!;
            spriteCameraField = assembly.GetType("LowLevel.CLASS_SPR")!.GetField("m_sprCamera")!;
            originalCamera = (Matrix)cameraField.GetValue(null)!;
            originalSpriteCamera = (Matrix)spriteCameraField.GetValue(null)!;
            var expanded = zoom ? InspectionCamera(originalCamera, width)
                : WidenCamera(originalCamera, width, GameAccess.Get<int>(level, "Width") * 88);
            CameraDelta = expanded.M41 - originalCamera.M41;
            Overlay = GameAccess.Get(level, "m_sprQuadNoir")!;
            var fade = assembly.GetType("LowLevel.CLASS_MOD_FADE")!.GetField("m_fadeCurrent")!.GetValue(null)!;
            Fade = GameAccess.Get(fade, "m_sprFade")!;
            Light = GameAccess.Get(level, "m_sprMaskLumiere")!;
            Contours = GameAccess.Get<object[]>(level, "m_sprMaskLumiereContour");
            LightContours = LightBounds(width, new Rectangle(
                (int)(GameAccess.Get<float>(Light, "m_sprX") + CameraDelta - GameAccess.Get<float>(Light, "m_sprOriginX")),
                (int)(GameAccess.Get<float>(Light, "m_sprY") - GameAccess.Get<float>(Light, "m_sprOriginY")),
                GameAccess.Get<int>(Light, "m_sprFrmWidth"), GameAccess.Get<int>(Light, "m_sprFrmHeight")));
            cameraField.SetValue(null, expanded);
            spriteCameraField.SetValue(null, expanded);
        }

        internal void Restore()
        {
            cameraField.SetValue(null, originalCamera);
            spriteCameraField.SetValue(null, originalSpriteCamera);
        }
    }
}

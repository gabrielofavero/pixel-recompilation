using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using Port.Hooks;

internal static class PromptArtworkProbe
{
    internal static void Run(Game game, string root, string directory)
    {
        directory = Path.GetFullPath(directory);
        Directory.CreateDirectory(directory);
        using var content = new ContentManager(game.Services, root);
        using var batch = new SpriteBatch(game.GraphicsDevice);
        var prompts = new PromptArtwork();
        var layouts = PromptVectors.Embedded().Layouts;
        int count = 0;
        foreach (var (asset, layout) in layouts)
        {
            var source = content.Load<Texture2D>(asset);
            using var texture = new Texture2D(game.GraphicsDevice, source.Width, source.Height, false, source.Format);
            var original = new Color[source.Width * source.Height];
            source.GetData(original);
            texture.SetData(original);
            prompts.Apply(texture, asset, true);
            var keyboard = new Color[original.Length];
            texture.GetData(keyboard);
            if (keyboard.SequenceEqual(original)) throw new InvalidDataException("Prompt was not replaced: " + asset);
            if (asset == "GAME/Interface/GAME_It_Jauge") VerifyGaugePulse(original, keyboard, texture.Width);
            for (int y = 0; y < texture.Height; y++)
            for (int x = 0; x < texture.Width; x++)
            {
                bool inside = layout.Patches.Any(patch => Contains(patch.Clear ?? patch.Bounds, x, y) || Contains(patch.Bounds, x, y));
                if (!inside && original[y * texture.Width + x] != keyboard[y * texture.Width + x])
                    throw new InvalidDataException("Prompt changed unrelated pixels: " + asset);
            }
            Capture(texture, asset, "keyboard");
            prompts.Apply(texture, asset, false);
            var restored = new Color[original.Length];
            texture.GetData(restored);
            if (!restored.SequenceEqual(original)) throw new InvalidDataException("Controller artwork was not restored exactly: " + asset);
            Capture(texture, asset, "controller");
            prompts.Apply(texture, asset, true);
            texture.GetData(restored);
            if (!restored.SequenceEqual(keyboard)) throw new InvalidDataException("Repeated keyboard swap changed artwork: " + asset);
            count++;
        }
        VerifyReload();
        Console.WriteLine($"PROMPT_ART_RESULT layouts={count} replacements=ok unrelated-pixels=ok controller-restore=exact repeat=ok hot-reload=ok invalid-edit=retained");

        void VerifyReload()
        {
            string? previous = Environment.GetEnvironmentVariable("PIXEL_PORT_PROMPT_ART_DIR");
            string edits = Path.Combine(directory, "live-edits");
            Directory.CreateDirectory(edits);
            string Read(string name)
            {
                using var reader = new StreamReader(typeof(KeyboardScheme).Assembly.GetManifestResourceStream("KeyboardPrompts." + name)!);
                return reader.ReadToEnd();
            }
            string json = Read("layouts.json"), path = Path.Combine(edits, "layouts.json");
            var texture = content.Load<Texture2D>("GAME/Tiles/GAME_Bk_sign_deco_B");
            var original = new Color[texture.Width * texture.Height];
            texture.GetData(original);
            var modified = new Color[original.Length];
            var repeated = new Color[original.Length];
            try
            {
                File.WriteAllText(Path.Combine(edits, "keyboard.svg"), Read("keyboard.svg"));
                // Change E to Q in the existing file, as an artist would.
                File.WriteAllText(path, json.Replace("\"label\": \"E\"", "\"label\": \"Q\""));
                Environment.SetEnvironmentVariable("PIXEL_PORT_PROMPT_ART_DIR", edits);
                prompts.Reload(true);
                prompts.Update(content, true);
                texture.GetData(modified);
                if (modified.SequenceEqual(original)) throw new InvalidDataException("Live edit was not applied.");
                File.WriteAllText(path, "{broken");
                File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(2));
                prompts.Reload(true);
                prompts.Update(content, true);
                texture.GetData(repeated);
                if (!repeated.SequenceEqual(modified)) throw new InvalidDataException("Invalid edit replaced the last valid art.");
                File.WriteAllText(path, json);
                File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(4));
                prompts.Reload(true);
                prompts.Update(content, true);
                texture.GetData(repeated);
                if (repeated.SequenceEqual(modified)) throw new InvalidDataException("Correcting a live edit did not reload.");
                prompts.Update(content, false);
                texture.GetData(repeated);
                if (!repeated.SequenceEqual(original)) throw new InvalidDataException("Live reload lost the original controller art.");
            }
            finally
            {
                Environment.SetEnvironmentVariable("PIXEL_PORT_PROMPT_ART_DIR", previous);
                prompts.Reload(true);
                File.WriteAllText(path, json);
            }
        }

        static void VerifyGaugePulse(Color[] original, Color[] keyboard, int width)
        {
            // The game loops atlas frames 1 and 2 only while the gauge is full.
            // Identical replacement badges erase that animation even though
            // ordinary texture-swap and controller-restoration checks pass.
            int normal = 0, expanded = 0;
            for (int y = 0; y < 88; y++)
            for (int x = 0; x < 360; x++)
            {
                if (keyboard[y * width + x] != original[y * width + x])
                    throw new InvalidDataException("Keyboard pulse changed the charging gauge frame.");
                // Inspect the letter itself, excluding any native ring pixels
                // left at the replacement's edges.
                if (x < 292 || x >= 324 || y < 28 || y >= 68) continue;
                if (keyboard[y * width + 360 + x].A == 255) normal++;
                if (keyboard[y * width + 720 + x].A == 255) expanded++;
            }
            if (expanded <= normal)
                throw new InvalidDataException("The full keyboard gauge must alternate normal and expanded badges.");
            Console.WriteLine("PROMPT_GAUGE_PULSE charging=unchanged expanded-frame=ok");
        }

        static bool Contains(int[] rect, int x, int y) => x >= rect[0] && y >= rect[1] && x < rect[0] + rect[2] && y < rect[1] + rect[3];
        void Capture(Texture2D texture, string asset, string mode)
        {
            using var target = new RenderTarget2D(game.GraphicsDevice, texture.Width, texture.Height, false, SurfaceFormat.Color, DepthFormat.None);
            game.GraphicsDevice.SetRenderTarget(target);
            game.GraphicsDevice.Clear(Color.Transparent);
            batch.Begin(SpriteSortMode.Deferred, BlendState.Opaque, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullNone);
            batch.Draw(texture, Vector2.Zero, Color.White);
            batch.End();
            game.GraphicsDevice.SetRenderTarget(null);
            using var file = File.Create(Path.Combine(directory, asset.Replace('/', '-') + "-" + mode + ".png"));
            target.SaveAsPng(file, target.Width, target.Height);
        }
    }
}

using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;

namespace Port.Hooks;

// Swap pixels, rather than sprite geometry: this also covers copied sprites,
// static puzzle sprites, atlas frames and prompts baked into backgrounds.
internal sealed class PromptArtwork
{
    private sealed class TextureState(Texture2D texture)
    {
        internal readonly Color[] Original = Read(texture);
        internal Color[]? Keyboard;
        internal int Revision = -1;
        internal bool Applied;
        private static Color[] Read(Texture2D texture)
        {
            var pixels = new Color[texture.Width * texture.Height];
            texture.GetData(pixels);
            return pixels;
        }
    }

    private readonly ConditionalWeakTable<Texture2D, TextureState> textures = new();
    private PromptVectors vectors = PromptVectors.Embedded();
    private long nextCheck;
    private string stamp = "", rejected = "";
    private int revision;

    internal Color[] Original(Texture2D texture) => textures.GetValue(texture, key => new(key)).Original;

    internal void Update(ContentManager content, bool keyboard)
    {
        Reload();
        // The pinned FNA ContentManager has no public loaded-asset enumeration.
        // Read its cache on the game thread; never change cache ownership/lifetime.
        var assets = GameAccess.Get<Dictionary<string, object>>(content, "loadedAssets");
        foreach (var (name, value) in assets)
            if (value is Texture2D texture && (vectors.Layouts.ContainsKey(name.Replace('\\', '/')) || textures.TryGetValue(texture, out _)))
                Apply(texture, name.Replace('\\', '/'), keyboard);
    }

    internal void Apply(Texture2D texture, string asset, bool keyboard)
    {
        if (texture.IsDisposed) return;
        var state = textures.GetValue(texture, key => new(key));
        if (!keyboard || !vectors.Layouts.TryGetValue(asset, out var layout))
        {
            if (state.Applied) texture.SetData(state.Original);
            state.Applied = false;
            return;
        }
        if (state.Revision != revision)
        {
            if (texture.Width != layout.Size[0] || texture.Height != layout.Size[1] || texture.LevelCount != 1 ||
                texture.Format is not (SurfaceFormat.Color or SurfaceFormat.ColorBgraEXT))
                throw new InvalidDataException($"Unexpected prompt texture: {asset} ({texture.Width}x{texture.Height}, {texture.Format}).");
            var pixels = (Color[])state.Original.Clone();
            bool bgra = texture.Format == SurfaceFormat.ColorBgraEXT;
            if (bgra) SwapChannels(pixels);
            vectors.Paint(pixels, texture.Width, texture.Height, layout);
            if (bgra) SwapChannels(pixels);
            state.Keyboard = pixels;
            state.Revision = revision;
            state.Applied = false;
        }
        if (!state.Applied) texture.SetData(state.Keyboard!);
        state.Applied = true;
    }

    private static void SwapChannels(Color[] pixels)
    {
        for (int i = 0; i < pixels.Length; i++) (pixels[i].R, pixels[i].B) = (pixels[i].B, pixels[i].R);
    }

    internal void Reload(bool force = false)
    {
        if (!force && Environment.TickCount64 < nextCheck) return;
        nextCheck = Environment.TickCount64 + 1000;
        string? directory = Environment.GetEnvironmentVariable("PIXEL_PORT_PROMPT_ART_DIR");
        directory ??= Path.Combine(AppContext.BaseDirectory, "assets", "keyboard-prompts");
        string svg = Path.Combine(directory, "keyboard.svg"), json = Path.Combine(directory, "layouts.json");
        string current = $"{directory}|{File.GetLastWriteTimeUtc(svg).Ticks}|{File.GetLastWriteTimeUtc(json).Ticks}";
        if (current == stamp || current == rejected) return;
        try
        {
            var candidate = File.Exists(svg) && File.Exists(json)
                ? PromptVectors.Parse(File.ReadAllText(svg), File.ReadAllText(json)) : PromptVectors.Embedded();
            var supported = PromptVectors.Embedded().Layouts;
            if (candidate.Layouts.Any(row => !supported.TryGetValue(row.Key, out var expected) || !row.Value.Size.SequenceEqual(expected.Size)))
                throw new InvalidDataException("Live prompt layouts must use supported asset names and texture sizes.");
            vectors = candidate;
            stamp = current;
            revision++;
            rejected = "";
        }
        catch (Exception error) when (error is IOException or System.Xml.XmlException or JsonException or InvalidDataException or FormatException or ArgumentException)
        {
            // Editors may briefly expose partial files. Keep the last valid art.
            rejected = current;
            Console.Error.WriteLine($"PROMPT_ART_RELOAD_FAILED {error.Message}");
        }
    }
}

internal sealed class PromptLayout
{
    public int[] Size { get; set; } = [];
    public PromptPatch[] Patches { get; set; } = [];
}

internal sealed class PromptPatch
{
    public string Label { get; set; } = "";
    public int[] Bounds { get; set; } = [];
    public int[]? Clear { get; set; }
    public bool Badge { get; set; } = true;
    public bool Background { get; set; }
    public string Style { get; set; } = "white";
}

// Deliberately small SVG vocabulary: symbols contain axis-aligned rectangles.
// Both the SVG preview and game use the same geometry, with no installed fonts
// or rasterization dependencies. Edits remain readable vector source.
internal sealed class PromptVectors
{
    internal sealed record Glyph(float Width, float Height, Vector4[] Rectangles);
    private readonly Dictionary<string, Glyph> symbols;
    internal readonly Dictionary<string, PromptLayout> Layouts;
    private PromptVectors(Dictionary<string, Glyph> symbols, Dictionary<string, PromptLayout> layouts)
    { this.symbols = symbols; Layouts = layouts; }

    internal static PromptVectors Embedded()
    {
        string Read(string name)
        {
            using var stream = typeof(PromptVectors).Assembly.GetManifestResourceStream("KeyboardPrompts." + name)!;
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        return Parse(Read("keyboard.svg"), Read("layouts.json"));
    }

    internal static PromptVectors Parse(string svg, string json)
    {
        XNamespace ns = "http://www.w3.org/2000/svg";
        var document = XDocument.Parse(svg);
        var symbols = new Dictionary<string, Glyph>(StringComparer.Ordinal);
        foreach (var symbol in document.Descendants(ns + "symbol"))
        {
            float Number(XElement element, string name) => float.Parse((string?)element.Attribute(name)
                ?? throw new InvalidDataException($"Missing SVG attribute: {name}"), CultureInfo.InvariantCulture);
            var box = ((string?)symbol.Attribute("viewBox") ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(value => float.Parse(value, CultureInfo.InvariantCulture)).ToArray();
            if (box.Length != 4 || box[0] != 0 || box[1] != 0 || box[2] <= 0 || box[3] <= 0 || box.Any(v => !float.IsFinite(v)) ||
                symbol.Elements().Any(element => element.Name != ns + "rect"))
                throw new InvalidDataException("Prompt symbols need a positive, zero-origin viewBox and rectangle geometry.");
            var rects = symbol.Elements(ns + "rect").Select(rect => new Vector4(Number(rect, "x"), Number(rect, "y"), Number(rect, "width"), Number(rect, "height"))).ToArray();
            if (rects.Length == 0 || rects.Any(rect => !float.IsFinite(rect.X + rect.Y + rect.Z + rect.W) || rect.X < 0 || rect.Y < 0 ||
                rect.Z <= 0 || rect.W <= 0 || rect.X + rect.Z > box[2] || rect.Y + rect.W > box[3]))
                throw new InvalidDataException("Prompt symbol rectangles must fit their viewBox.");
            symbols.Add((string?)symbol.Attribute("id") ?? throw new InvalidDataException("Unnamed prompt symbol."), new(box[2], box[3], rects));
        }
        if (!symbols.ContainsKey("circle") || "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789".Any(c => !symbols.ContainsKey("glyph-" + c)))
            throw new InvalidDataException("Prompt SVG must contain the circle, alphabet and digits.");
        var layouts = JsonSerializer.Deserialize<Dictionary<string, PromptLayout>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("Missing prompt layouts.");
        foreach (var layout in layouts.Values)
        {
            if (layout is null || layout.Size is null || layout.Size.Length != 2 || layout.Size.Any(value => value <= 0 || value > 8192) ||
                layout.Patches is null || layout.Patches.Length == 0)
                throw new InvalidDataException("Invalid prompt texture size or patches.");
            foreach (var patch in layout.Patches)
            {
                static bool Rect(int[] values) => values.Length == 4 && values[2] > 0 && values[3] > 0;
                if (patch is null || patch.Bounds is null || !Rect(patch.Bounds) || patch.Bounds[2] > 2048 || patch.Bounds[3] > 2048 || patch.Clear is not null && !Rect(patch.Clear) ||
                    string.IsNullOrWhiteSpace(patch.Label) || patch.Label.Length > 128 || patch.Badge && patch.Label.Length > 3 ||
                    patch.Label.Any(c => c != ' ' && !symbols.ContainsKey("glyph-" + c)) ||
                    patch.Style is not ("white" or "blue" or "orange"))
                    throw new InvalidDataException("Invalid prompt rectangle, label (badges allow 1-3 characters) or style.");
                int[] clear = patch.Clear ?? patch.Bounds;
                if (clear[0] < 0 || clear[1] < 0 || clear[0] + clear[2] > layout.Size[0] || clear[1] + clear[3] > layout.Size[1])
                    throw new InvalidDataException("Prompt clear rectangle leaves the texture.");
            }
        }
        return new(symbols, new(layouts, StringComparer.OrdinalIgnoreCase));
    }

    internal void Paint(Color[] pixels, int width, int height, PromptLayout layout)
    {
        foreach (var patch in layout.Patches)
        {
            int[] clear = patch.Clear ?? patch.Bounds;
            for (int y = clear[1]; y < clear[1] + clear[3]; y++)
            for (int x = clear[0]; x < clear[0] + clear[2]; x++)
            {
                // Baked menu backgrounds are opaque. Interpolate the two clean
                // scanlines around the old badge to avoid transparent holes.
                pixels[y * width + x] = patch.Background
                    ? Color.Lerp(pixels[Math.Max(0, clear[1] - 1) * width + x],
                        pixels[Math.Min(height - 1, clear[1] + clear[3]) * width + x], (y - clear[1] + 1f) / (clear[3] + 1))
                    : Color.Transparent;
            }
            var layer = Render(patch);
            int[] b = patch.Bounds;
            for (int y = 0; y < b[3]; y++)
            for (int x = 0; x < b[2]; x++)
            {
                int dx = b[0] + x, dy = b[1] + y;
                if (dx < 0 || dy < 0 || dx >= width || dy >= height) continue;
                int index = dy * width + dx;
                var foreground = layer[y * b[2] + x];
                if (foreground.A == 0) continue;
                var background = pixels[index];
                float a = foreground.A / 255f, ba = background.A / 255f * (1 - a), alpha = a + ba;
                pixels[index] = new Color(new Vector4((foreground.ToVector3() * a + background.ToVector3() * ba) / alpha, alpha));
            }
        }
    }

    internal Color[] Render(PromptPatch patch)
    {
        int width = patch.Bounds[2], height = patch.Bounds[3];
        var mask = new float[width * height];
        void GlyphAt(string id, float left, float top, float w, float h, bool grid)
        {
            var glyph = symbols[id];
            foreach (var rect in glyph.Rectangles)
            {
                float x1 = left + rect.X / glyph.Width * w, y1 = top + rect.Y / glyph.Height * h;
                float x2 = left + (rect.X + rect.Z) / glyph.Width * w, y2 = top + (rect.Y + rect.W) / glyph.Height * h;
                if (grid) { x2 -= Math.Min(0.7f, w / glyph.Width * 0.12f); y2 -= Math.Min(0.7f, h / glyph.Height * 0.12f); }
                for (int y = Math.Max(0, (int)MathF.Floor(y1)); y < Math.Min(height, (int)MathF.Ceiling(y2)); y++)
                for (int x = Math.Max(0, (int)MathF.Floor(x1)); x < Math.Min(width, (int)MathF.Ceiling(x2)); x++)
                {
                    float coverage = Math.Clamp(Math.Min(x + 1, x2) - Math.Max(x, x1), 0, 1) * Math.Clamp(Math.Min(y + 1, y2) - Math.Max(y, y1), 0, 1);
                    mask[y * width + x] = Math.Max(mask[y * width + x], coverage);
                }
            }
        }
        if (patch.Badge) GlyphAt("circle", 0, 0, width, height, false);
        string label = patch.Label;
        string? face = label.Length == 1 && symbols.ContainsKey("face-" + label) ? "face-" + label : null;
        float textWidth = label.Sum(c => c == ' ' ? 3 : symbols["glyph-" + c].Width) + label.Length - 1;
        float sx = patch.Badge ? width / 128f * (label.Length == 1 ? 7 : label.Length == 2 ? 4 : 3)
            : width / textWidth;
        float h = patch.Badge ? height * 35 / 128f : height;
        if (face is not null && patch.Badge) GlyphAt(face, width * 44 / 128f, height * 44 / 128f, width * 40 / 128f, height * 40 / 128f, true);
        else
        {
            float x = (width - textWidth * sx) / 2;
            foreach (char c in label)
            {
                float w = (c == ' ' ? 3 : symbols["glyph-" + c].Width) * sx;
                if (c != ' ') GlyphAt("glyph-" + c, x, (height - h) / 2, w, h, patch.Badge);
                x += w + sx;
            }
        }
        var halo = (float[])mask.Clone();
        var buffer = new float[mask.Length];
        int radius = patch.Style == "white" ? 2 : 3;
        for (int pass = 0; pass < 3; pass++)
        {
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                float sum = 0;
                for (int d = -radius; d <= radius; d++) if (x + d >= 0 && x + d < width) sum += halo[y * width + x + d];
                buffer[y * width + x] = sum / (radius * 2 + 1);
            }
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                float sum = 0;
                for (int d = -radius; d <= radius; d++) if (y + d >= 0 && y + d < height) sum += buffer[(y + d) * width + x];
                halo[y * width + x] = sum / (radius * 2 + 1);
            }
        }
        var result = new Color[mask.Length];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int index = y * width + x;
            float core = mask[index], glow = Math.Clamp(halo[index] * 1.7f, 0, 1) * (1 - core), alpha = core + glow;
            if (alpha == 0) continue;
            var tint = patch.Style switch
            {
                "blue" => Color.Lerp(Color.White, new Color(143, 180, 255), Math.Clamp((y / (float)height - 0.25f) * 1.6f, 0, 1)),
                "orange" => new Color(255, 182, 26),
                _ => Color.White
            };
            var haloTint = patch.Style switch { "blue" => new Color(50, 146, 255), "orange" => new Color(255, 116, 0), _ => new Color(185, 195, 205) };
            result[index] = new Color(new Vector4((tint.ToVector3() * core + haloTint.ToVector3() * glow) / alpha, alpha));
        }
        return result;
    }
}

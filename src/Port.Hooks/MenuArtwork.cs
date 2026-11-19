using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Content;
using System.Runtime.CompilerServices;

namespace Port.Hooks;

// Retain the original menu atlases. New labels use their measured glyph sizes,
// baselines and blue color ramp through the same sprite/Z-sort pipeline.
internal sealed class MenuArtwork(GraphicsDevice device) : IDisposable
{
    private readonly Dictionary<string, Texture2D> labels = [];
    private readonly ConditionalWeakTable<object, Texture2D> backgrounds = new();
    private Texture2D? backBackground;
    private const int Width = 512, Height = 244;
    private MenuStyle[]? styles;
    private sealed record MenuStyle(Vector2 Scale, int Baseline, Color[] Tint, float Halo);
    internal float LabelWidth(SpriteFont font, string label) => font.MeasureString(label).X * styles![0].Scale.X;
    internal static string? OriginalAsset(string label) => label switch
    {
        "New Game" => "TITLE/TITLE_Menu_new_game",
        "Continue" => "TITLE/TITLE_Menu_continue",
        "Credits" => "TITLE/TITLE_Menu_credits",
        _ => null
    };

    internal void Background(object sprite, ContentManager content, bool submenu, PromptArtwork prompts)
    {
        var original = backgrounds.GetValue(sprite, key => GameAccess.Get<Texture2D>(key, "m_texture"));
        if (submenu && backBackground is null)
        {
            // Reuse the baked BACK label beside the title's original Back/Select
            // icon. Keep that icon for keyboard and controller input alike.
            var source = content.Load<Texture2D>("CONT/CONT_Bg");
            var pixels = new Color[original.Width * original.Height];
            var back = new Color[pixels.Length];
            prompts.Original(original).CopyTo(pixels, 0);
            prompts.Original(source).CopyTo(back, 0);
            var region = new Rectangle(158, 562, 125, 115);
            for (int y = region.Top; y < region.Bottom; y++)
            for (int x = region.Left; x < region.Right; x++)
            {
                float feather = Math.Clamp(Math.Min((x - region.Left) / 4f,
                    Math.Min(region.Right - 1 - x, Math.Min(y - region.Top, region.Bottom - 1 - y)) / 18f), 0, 1);
                int index = y * original.Width + x;
                // Align BACK with EXIT's baseline and leave room for Select's
                // wider outline; the Continue label originally sits lower.
                pixels[index] = Color.Lerp(pixels[index], back[(y + 8) * original.Width + x - 8], feather);
            }
            // GetData returns the texture's native channel order. Preserve its
            // format when uploading the copied pixels (legacy assets are BGRA).
            backBackground = new Texture2D(device, original.Width, original.Height, false, original.Format);
            backBackground.SetData(pixels);
        }
        GameAccess.Set(sprite, "m_texture", submenu ? backBackground! : original);
    }

    internal void Label(object sprite, SpriteFont font, string label, ContentManager content)
    {
        Texture2D texture;
        if (OriginalAsset(label) is string asset) texture = content.Load<Texture2D>(asset);
        else
        {
            styles ??= MeasureStyle(font, content.Load<Texture2D>("TITLE/TITLE_Menu_new_game"));
            if (!labels.TryGetValue(label, out texture!)) labels[label] = texture = Create(font, label);
        }
        GameAccess.Set(sprite, "m_texture", texture);
        GameAccess.Set(sprite, "m_texWidth", texture.Width);
        GameAccess.Set(sprite, "m_texHeight", texture.Height);
        GameAccess.Set(sprite, "m_sprFrmWidth", texture.Width / 2);
        GameAccess.Set(sprite, "m_sprFrmHeight", texture.Height);
        GameAccess.Set(sprite, "m_sprOriginX", texture.Width / 4f);
        GameAccess.Set(sprite, "m_sprOriginY", texture.Height / 2f);
    }

    private Color[] ReadReference(Texture2D texture)
    {
        var previous = device.GetRenderTargets();
        using var target = new RenderTarget2D(device, texture.Width, texture.Height, false, SurfaceFormat.Color, DepthFormat.None);
        using var batch = new SpriteBatch(device);
        device.SetRenderTarget(target);
        device.Clear(Color.Transparent);
        // Copy through the GPU to normalize BGRA source assets to RGBA, keeping
        // straight alpha and all of the reference's original colors.
        batch.Begin(SpriteSortMode.Deferred, BlendState.Opaque, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullNone);
        batch.Draw(texture, Vector2.Zero, Color.White);
        batch.End();
        device.SetRenderTargets(previous);
        var pixels = new Color[texture.Width * texture.Height];
        target.GetData(pixels);
        return pixels;
    }

    private float[] Mask(SpriteFont font, string label)
    {
        var previous = device.GetRenderTargets();
        using var target = new RenderTarget2D(device, Width, 64, false, SurfaceFormat.Color, DepthFormat.None);
        using var batch = new SpriteBatch(device);
        device.SetRenderTarget(target);
        device.Clear(Color.Transparent);
        batch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullNone);
        batch.DrawString(font, label, Vector2.Zero, Color.White);
        batch.End();
        device.SetRenderTargets(previous);
        var pixels = new Color[Width * 64];
        target.GetData(pixels);
        return pixels.Select(pixel => pixel.A / 255f).ToArray();
    }

    private static Rectangle Bounds(int width, int height, Func<int, int, bool> contains)
    {
        int left = width, right = 0, top = height, bottom = 0;
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
            if (contains(x, y)) { left = Math.Min(left, x); right = Math.Max(right, x + 1); top = Math.Min(top, y); bottom = Math.Max(bottom, y + 1); }
        if (right <= left || bottom <= top) throw new InvalidDataException("Menu font/reference contains no glyph pixels.");
        return new(left, top, right - left, bottom - top);
    }

    private MenuStyle[] MeasureStyle(SpriteFont font, Texture2D reference)
    {
        var native = Mask(font, "New Game");
        var glyph = Bounds(Width, 64, (x, y) => native[y * Width + x] > 0.7f);
        var pixels = ReadReference(reference);
        int frameWidth = reference.Width / 2;
        return Enumerable.Range(0, 2).Select(frame =>
        {
            Color At(int x, int y) => pixels[y * reference.Width + frame * frameWidth + x];
            var body = Bounds(frameWidth, reference.Height, (x, y) => At(x, y).A > 180 && At(x, y).R > (frame == 0 ? 130 : 100));
            var tint = new Color[Height];
            for (int y = 0; y < Height; y++)
            {
                int row = Math.Clamp(y, body.Top, body.Bottom - 1);
                tint[y] = Enumerable.Range(body.Left, body.Width).Select(x => At(x, row))
                    .Where(p => p.A > 180).MaxBy(p => p.R);
                tint[y].A = 255;
            }
            var scale = new Vector2(body.Width / (float)glyph.Width, body.Height / (float)glyph.Height);
            var style = new MenuStyle(scale, body.Bottom - (int)Math.Round(glyph.Bottom * scale.Y), tint, 0.9f);
            if (frame == 1)
            {
                // Fit the dark outline's opacity to the baked original, rather
                // than adding an offset shadow or guessing a faint halo.
                var mask = Rasterize(native, glyph, style);
                var blur = Blur(mask, 1);
                double numerator = 0, denominator = 0;
                int offset = (Width - frameWidth) / 2;
                for (int y = 0; y < Height; y++)
                for (int x = 0; x < frameWidth; x++)
                {
                    int index = y * Width + x + offset;
                    var pixel = At(x, y);
                    if (pixel.R >= 50 || pixel.A < 5 || mask[index] >= 0.1f) continue;
                    float edge = blur[index] * (1 - mask[index]);
                    numerator += edge * pixel.A / 255f;
                    denominator += edge * edge;
                }
                style = style with { Halo = denominator > 0 ? (float)Math.Clamp(numerator / denominator, 0, 4) : 0 };
            }
            return style;
        }).ToArray();
    }

    private Texture2D Create(SpriteFont font, string label)
    {
        var native = Mask(font, label);
        var bounds = Bounds(Width, 64, (x, y) => native[y * Width + x] > 0.7f);
        var pixels = new Color[Width * 2 * Height];
        for (int frame = 0; frame < 2; frame++)
        {
            var style = styles![frame];
            var mask = Rasterize(native, bounds, style);
            // The original selected frame has a blue rim and soft halo. The
            // inactive frame is pale blue, with only a faint dark edge.
            var glow = Blur(mask, frame == 0 ? 3 : 1);
            for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                int index = y * Width + x;
                float core = mask[index], rim = core;
                if (frame == 0)
                    for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                        if (x + dx >= 0 && x + dx < Width && y + dy >= 0 && y + dy < Height)
                            rim = Math.Max(rim, mask[(y + dy) * Width + x + dx]);
                float edge = frame == 0 ? rim * (1 - core) : 0;
                float halo = Math.Clamp(glow[index] * style.Halo, 0, 1) * (1 - core - edge);
                float alpha = core + edge + halo;
                if (alpha <= 0) continue;
                var rgb = (style.Tint[y].ToVector3() * core + new Color(81, 154, 253).ToVector3() * edge +
                    (frame == 0 ? new Color(60, 130, 255) : Color.Black).ToVector3() * halo) / alpha;
                pixels[y * Width * 2 + frame * Width + x] = new Color(new Vector4(rgb, alpha));
            }
        }
        var result = new Texture2D(device, Width * 2, Height);
        result.SetData(pixels);
        return result;
    }

    private static float[] Rasterize(float[] native, Rectangle bounds, MenuStyle style)
    {
        var mask = new float[Width * Height];
        float startX = Width / 2f - (bounds.Left + bounds.Width / 2f) * style.Scale.X;
        float Sample(int x, int y) => x >= 0 && x < Width && y >= 0 && y < 64 ? native[y * Width + x] : 0;
        for (int y = 0; y < Height; y++)
        for (int x = 0; x < Width; x++)
        {
            float sx = (x + 0.5f - startX) / style.Scale.X - 0.5f;
            float sy = (y + 0.5f - style.Baseline) / style.Scale.Y - 0.5f;
            int ix = (int)MathF.Floor(sx), iy = (int)MathF.Floor(sy);
            mask[y * Width + x] = MathHelper.Lerp(
                MathHelper.Lerp(Sample(ix, iy), Sample(ix + 1, iy), sx - ix),
                MathHelper.Lerp(Sample(ix, iy + 1), Sample(ix + 1, iy + 1), sx - ix), sy - iy);
        }
        return mask;
    }

    private static float[] Blur(float[] source, int radius)
    {
        float[] buffer = new float[source.Length], result = (float[])source.Clone();
        for (int pass = 0; pass < 3; pass++)
        {
            for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                float sum = 0;
                for (int d = -radius; d <= radius; d++) sum += result[y * Width + Math.Clamp(x + d, 0, Width - 1)];
                buffer[y * Width + x] = sum / (2 * radius + 1);
            }
            for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                float sum = 0;
                for (int d = -radius; d <= radius; d++) sum += buffer[Math.Clamp(y + d, 0, Height - 1) * Width + x];
                result[y * Width + x] = sum / (2 * radius + 1);
            }
        }
        return result;
    }

    public void Dispose() { foreach (var texture in labels.Values) texture.Dispose(); backBackground?.Dispose(); }
}

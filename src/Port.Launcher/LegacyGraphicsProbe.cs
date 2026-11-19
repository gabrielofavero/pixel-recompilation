using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Port.Bridge;

internal static class LegacyGraphicsProbe
{
    internal static void Run(GraphicsDevice device)
    {
        var previous = device.GetRenderTargets();
        using var target = new RenderTarget2D(device, 8, 1, false, SurfaceFormat.Color, DepthFormat.None);
        using var texture = new Texture2D(device, 2, 1);
        using var batch = new SpriteBatch(device);
        texture.SetData(new[] { Color.Red, Color.Blue });
        var singletonFilter = SamplerState.LinearClamp.Filter;
        var singletonAddress = SamplerState.LinearClamp.AddressU;
        foreach (int address in new[] { 1, 2, 3 })
        {
            device.SetRenderTarget(target);
            device.Clear(Color.Black);
            LegacyGraphics.Begin(batch, SpriteBlendMode.AlphaBlend, SpriteSortMode.Deferred, SaveStateMode.None);
            LegacyGraphics.SetMagFilter(device.SamplerStates[0], TextureFilter.Point);
            LegacyGraphics.SetAddressU(device.SamplerStates[0], (TextureAddressMode)address);
            LegacyGraphics.SetAddressV(device.SamplerStates[0], (TextureAddressMode)3);
            batch.Draw(texture, new Rectangle(0, 0, 8, 1), new Rectangle(0, 0, 4, 1), Color.White);
            batch.End();
            device.SetRenderTargets(previous);
            var colors = new Color[8];
            target.GetData(colors);
            Color[] expected = address switch
            {
                1 => [Color.Red, Color.Red, Color.Blue, Color.Blue, Color.Red, Color.Red, Color.Blue, Color.Blue],
                2 => [Color.Red, Color.Red, Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Red, Color.Red],
                _ => [Color.Red, Color.Red, Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue]
            };
            Require(colors.SequenceEqual(expected), $"deferred point/address mode {address}: {string.Join(',', colors)}");
        }
        texture.SetData(new[] { Color.White, Color.White });
        foreach (bool additive in new[] { false, true })
        {
            device.SetRenderTarget(target);
            device.Clear(new Color(0, 80, 0));
            LegacyGraphics.Begin(batch, SpriteBlendMode.AlphaBlend, SpriteSortMode.Deferred, SaveStateMode.None);
            var state = LegacyGraphics.GetRenderState(device);
            state.AlphaBlendEnable = true;
            state.SourceBlend = (Blend)5;
            state.DestinationBlend = (Blend)(additive ? 2 : 6);
            batch.Draw(texture, new Rectangle(0, 0, 8, 1), new Color(255, 0, 0, 128));
            batch.End();
            device.SetRenderTargets(previous);
            var colors = new Color[8];
            target.GetData(colors);
            Require(colors.All(c => Math.Abs(c.R - 128) <= 1 && Math.Abs(c.G - (additive ? 80 : 40)) <= 1 && c.B == 0),
                $"deferred {(additive ? "additive" : "alpha")} blending: {colors[0]}");
        }
        Require(SamplerState.LinearClamp.Filter == singletonFilter && SamplerState.LinearClamp.AddressU == singletonAddress,
            "shared FNA sampler was modified");
        Console.WriteLine("LEGACY_GRAPHICS_RESULT point=ok wrap=ok mirror=ok clamp=ok alpha=ok additive=ok singletons=ok");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Legacy graphics probe: " + message);
    }
}

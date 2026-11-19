using System.Runtime.CompilerServices;
using Microsoft.Xna.Framework.Graphics;

namespace Microsoft.Xna.Framework.Graphics
{
    public sealed class EffectPool;
    public enum SpriteBlendMode { None, AlphaBlend, Additive }
    public enum SaveStateMode { None, SaveState }

    public sealed class RenderState(GraphicsDevice device)
    {
        private bool enabled;
        private Blend source = Blend.SourceAlpha;
        private Blend destination = Blend.InverseSourceAlpha;

        public bool AlphaBlendEnable
        {
            set => enabled = value;
        }
        public Blend SourceBlend
        {
            set => source = Port.Bridge.LegacyGraphics.MapLegacyBlend(value);
        }
        public Blend DestinationBlend
        {
            set { destination = Port.Bridge.LegacyGraphics.MapLegacyBlend(value); Apply(); }
        }
        private void Apply()
        {
            if (!enabled) { device.BlendState = BlendState.Opaque; return; }
            device.BlendState = (source, destination) switch
            {
                (Blend.SourceAlpha, Blend.One) => BlendState.Additive,
                (Blend.SourceAlpha, Blend.InverseSourceAlpha) => BlendState.NonPremultiplied,
                _ => throw new NotSupportedException($"Unsupported XNA3 blend mode: {source}, {destination}")
            };
        }
    }
}

namespace Port.Bridge
{
    public static class LegacyGraphics
    {
        private static readonly ConditionalWeakTable<GraphicsDevice, RenderState> States = new();

        public static BasicEffect CreateBasicEffect(GraphicsDevice device, EffectPool? pool)
        {
            if (pool is not null) throw new NotSupportedException("XNA EffectPool is unsupported.");
            return new BasicEffect(device);
        }
        public static void Begin(SpriteBatch batch, SpriteBlendMode blend, SpriteSortMode sort, SaveStateMode save)
        {
            if (save != SaveStateMode.None) throw new NotSupportedException("XNA SaveStateMode is unsupported.");
            batch.Begin(sort, blend switch
            {
                SpriteBlendMode.AlphaBlend => BlendState.NonPremultiplied,
                SpriteBlendMode.Additive => BlendState.Additive,
                _ => BlendState.Opaque
            });
        }
        public static RenderState GetRenderState(GraphicsDevice device) => States.GetValue(device, d => new RenderState(d));
        public static VertexElement[] GetVertexElements() => VertexPositionColor.VertexDeclaration.GetVertexElements();
        public static VertexDeclaration CreateVertexDeclaration(GraphicsDevice device, VertexElement[] elements)
        {
            ArgumentNullException.ThrowIfNull(device);
            return new VertexDeclaration(elements);
        }
        public static void SetMagFilter(SamplerState state, TextureFilter filter)
        {
            ArgumentNullException.ThrowIfNull(state);
            if (filter != TextureFilter.Point)
                throw new NotSupportedException($"Unsupported XNA3 magnification filter: {filter}");
            state.Filter = TextureFilter.Point;
        }

        public static Blend MapLegacyBlend(Blend legacy) => (int)legacy switch
        {
            2 => Blend.One,
            5 => Blend.SourceAlpha,
            6 => Blend.InverseSourceAlpha,
            _ => legacy
        };
    }
}

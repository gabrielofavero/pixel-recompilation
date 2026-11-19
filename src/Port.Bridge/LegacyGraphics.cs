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
        internal BlendState Pipeline { get; private set; } = new();
        internal SamplerState Sampler { get; } = new()
        {
            Filter = TextureFilter.Point,
            AddressU = TextureAddressMode.Clamp,
            AddressV = TextureAddressMode.Clamp
        };

        internal void Begin(SpriteBlendMode blend)
        {
            // FNA caches applied blend states by identity. A batch gets its own
            // state so changing the next batch cannot modify an applied object.
            Pipeline = new BlendState();
            enabled = blend != SpriteBlendMode.None;
            source = Blend.SourceAlpha;
            destination = blend == SpriteBlendMode.Additive ? Blend.One : Blend.InverseSourceAlpha;
            Apply();
            device.SamplerStates[0] = Sampler;
        }

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
            var desired = !enabled ? BlendState.Opaque : (source, destination) switch
            {
                (Blend.SourceAlpha, Blend.One) => BlendState.Additive,
                (Blend.SourceAlpha, Blend.InverseSourceAlpha) => BlendState.NonPremultiplied,
                _ => throw new NotSupportedException($"Unsupported XNA3 blend mode: {source}, {destination}")
            };
            Pipeline.ColorSourceBlend = desired.ColorSourceBlend;
            Pipeline.ColorDestinationBlend = desired.ColorDestinationBlend;
            Pipeline.AlphaSourceBlend = desired.AlphaSourceBlend;
            Pipeline.AlphaDestinationBlend = desired.AlphaDestinationBlend;
            device.BlendState = Pipeline;
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
            // XNA3 permits state changes between Begin and End. FNA reapplies its
            // Begin arguments when flushing deferred sprites, so pass the same
            // mutable states that the legacy setters update. Never mutate FNA's
            // shared LinearClamp/PointClamp or blend-state singletons.
            var state = GetRenderState(batch.GraphicsDevice);
            state.Begin(blend);
            batch.Begin(sort, state.Pipeline, state.Sampler, DepthStencilState.None, RasterizerState.CullNone);
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

        public static TextureAddressMode MapLegacyAddress(TextureAddressMode legacy) => (int)legacy switch
        {
            1 => TextureAddressMode.Wrap,
            2 => TextureAddressMode.Mirror,
            3 => TextureAddressMode.Clamp,
            _ => throw new NotSupportedException($"Unsupported XNA3 texture address mode: {(int)legacy}")
        };
        public static void SetAddressU(SamplerState state, TextureAddressMode value) => state.AddressU = MapLegacyAddress(value);
        public static void SetAddressV(SamplerState state, TextureAddressMode value) => state.AddressV = MapLegacyAddress(value);

        public static Blend MapLegacyBlend(Blend legacy) => (int)legacy switch
        {
            2 => Blend.One,
            5 => Blend.SourceAlpha,
            6 => Blend.InverseSourceAlpha,
            _ => legacy
        };
    }
}

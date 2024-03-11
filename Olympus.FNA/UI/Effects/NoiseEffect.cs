using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Diagnostics.CodeAnalysis;

namespace OlympUI {
    public class NoiseEffect : MiniEffect {

        public static readonly new MiniEffectCache Cache = new(
            $"effects/{nameof(NoiseEffect)}.fxo",
            (gd, _) => new NoiseEffect(gd)
        );

        private MiniEffectParam<Vector4> MinMaxParam;
        // private EffectParameter MinMaxParam;
        // private bool MinMaxValid;
        // private Vector4 MinMaxValue = new(0f, 0f, 1f, 1f);
        public Vector2 Min {
            get => new(MinMaxParam.Value.X, MinMaxParam.Value.Y);
            set => MinMaxParam.Value = new Vector4(value.X, value.Y, MinMaxParam.Value.Z, MinMaxParam.Value.W);
        }
        public Vector2 Max {
            get => new(MinMaxParam.Value.Z, MinMaxParam.Value.W);
            set => MinMaxParam.Value = new Vector4(MinMaxParam.Value.X, MinMaxParam.Value.Y, value.X, value.Y);
        }

        private MiniEffectParam<Vector3> NoiseParam;
        // private EffectParameter NoiseParam;
        // private bool NoiseValid;
        // private Vector3 NoiseValue;
        public Vector2 Spread {
            get => new(NoiseParam.Value.X, NoiseParam.Value.Y);
            set => NoiseParam.Value = new Vector3(value.X, value.Y, NoiseParam.Value.Z);
        }
        public float Blend {
            get => NoiseParam.Value.Z;
            set => NoiseParam.Value = new Vector3(NoiseParam.Value.X, NoiseParam.Value.Y, value);
        }

        public NoiseEffect(GraphicsDevice graphicsDevice)
            : base(graphicsDevice, Cache.GetData()) {
            SetupParams();
        }

        protected NoiseEffect(GraphicsDevice graphicsDevice, byte[]? effectCode)
            : base(graphicsDevice, effectCode) {
            SetupParams();
        }

        protected NoiseEffect(NoiseEffect cloneSource)
            : base(cloneSource) {
            SetupParams();
        }

        [MemberNotNull(nameof(MinMaxParam))]
        [MemberNotNull(nameof(NoiseParam))]
        private void SetupParams() {
            MinMaxParam = new MiniEffectParam<Vector4>(Parameters[MiniEffectParamCount + 0], new Vector4(0f, 0f, 1f, 1f));
            NoiseParam = new MiniEffectParam<Vector3>(Parameters[MiniEffectParamCount + 1], default);
        }

        public override Effect Clone()
            => new NoiseEffect(this);

        protected override void OnApply() {
            base.OnApply();

            MinMaxParam.Apply();

            NoiseParam.Apply();
        }

    }
}

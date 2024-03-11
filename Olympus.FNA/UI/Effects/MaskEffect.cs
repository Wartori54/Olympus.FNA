using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Diagnostics.CodeAnalysis;

namespace OlympUI {
    public class MaskEffect : MiniEffect {

        public static readonly new MiniEffectCache Cache = new(
            $"effects/{nameof(MaskEffect)}.fxo",
            (gd, _) => new MaskEffect(gd)
        );

        protected MiniEffectParam<Vector4> MaskXYWHParam;
        // protected EffectParameter MaskXYWHParam;
        // protected bool MaskXYWHValid;
        // protected Vector4 MaskXYWHValue = new(0f, 0f, 1f, 1f);
        // public Vector4 MaskXYWH {
            // get => MaskXYWHValue;
            // set => _ = (MaskXYWHValue = value, MaskXYWHValid = false);
        // }

        public MaskEffect(GraphicsDevice graphicsDevice)
            : base(graphicsDevice, Cache.GetData()) {
            SetupParams();
        }

        protected MaskEffect(GraphicsDevice graphicsDevice, byte[]? effectCode)
            : base(graphicsDevice, effectCode) {
            SetupParams();
        }

        protected MaskEffect(MaskEffect cloneSource)
            : base(cloneSource) {
            SetupParams();
        }

        [MemberNotNull(nameof(MaskXYWHParam))]
        private void SetupParams() {
            MaskXYWHParam = new MiniEffectParam<Vector4>(Parameters[MiniEffectParamCount + 0], new Vector4(0f, 0f, 1f, 1f));
        }

        public override Effect Clone()
            => new MaskEffect(this);

        protected override void OnApply() {
            base.OnApply();

            MaskXYWHParam.Apply();
        }

    }
}

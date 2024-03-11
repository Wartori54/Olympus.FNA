using Microsoft.Xna.Framework.Graphics;
using OlympUI;
using System.Diagnostics.CodeAnalysis;

namespace Olympus.Effects;

public class GrayscaleEffect : MiniEffect {
    public new static readonly MiniEffectCache Cache = new(
                $"effects/{nameof(GrayscaleEffect)}.fxo",
                (gd, _) => new GrayscaleEffect(gd)
            );

    private MiniEffectParam<float> GrayscaleParam;

    public float Intensity {
        get => GrayscaleParam.Value;
        set => GrayscaleParam.Value = value;
    }

    protected GrayscaleEffect(GraphicsDevice graphicsDevice) : base(graphicsDevice, Cache.GetData()) {
        SetupParams();
    }
    
    protected GrayscaleEffect(GraphicsDevice graphicsDevice, byte[]? effectCode) : base(graphicsDevice, effectCode) {
        SetupParams();
    }

    protected GrayscaleEffect(MiniEffect cloneSource) : base(cloneSource) {
        SetupParams();
    }

    [MemberNotNull(nameof(GrayscaleParam))]
    private void SetupParams() {
        GrayscaleParam = new MiniEffectParam<float>(Parameters[MiniEffectParamCount + 0], 0f);
    }

    public override Effect Clone()
        => new GrayscaleEffect(this);

    protected override void OnApply() {
        base.OnApply();
        GrayscaleParam.Apply();
    }
}
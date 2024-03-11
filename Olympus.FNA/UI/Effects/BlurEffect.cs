using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace OlympUI {
    // See ContentSrc/effects/BlurEffect/BlurEffectTemplate for more info.
    public sealed unsafe partial class BlurEffect : MiniEffect {

        public static readonly new BlurEffectCache Cache = new(
            $"effects/{nameof(BlurEffect)}/{{0}}.fxo"
        );

        public readonly int RadiusCeil;
        public readonly int KernelSizeCeil;

        private EffectParameter OffsetsWeightsParam;
        private bool OffsetsWeightsValid;
        private readonly Vector4[] OffsetsWeightsValue;

        private BlurAxis AxisValue;
        public BlurAxis Axis {
            get => AxisValue;
            set => _ = (AxisValue = value, OffsetsWeightsValid = false);
        }

        private int RadiusValue;
        public int Radius {
            get => RadiusValue;
            set => _ = (RadiusValue = value, OffsetsWeightsValid = false);
        }

        public int KernelSize => RadiusValue * 2 + 1;

        private float StrengthValue = 2f;
        public float Strength {
            get => StrengthValue;
            set => _ = (StrengthValue = value, OffsetsWeightsValid = false);
        }


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

        private float TextureSizeValue;

        public BlurEffect(GraphicsDevice graphicsDevice, int radius)
            : base(graphicsDevice, Cache.GetData(radius)) {
            RadiusValue = radius;
            RadiusCeil = (int) MathF.Ceiling((float) radius / Step) * Step;
            KernelSizeCeil = RadiusCeil * 2 + 1;
            OffsetsWeightsValue = new Vector4[KernelSizeCeil];
            SetupParams();
        }

        private BlurEffect(BlurEffect cloneSource)
            : base(cloneSource) {
            Axis = cloneSource.Axis;
            RadiusValue = cloneSource.RadiusValue;
            RadiusCeil = cloneSource.RadiusCeil;
            KernelSizeCeil = cloneSource.KernelSizeCeil;
            OffsetsWeightsValue = new Vector4[KernelSizeCeil];
            SetupParams();
        }

        [MemberNotNull(nameof(OffsetsWeightsParam))]
        [MemberNotNull(nameof(MinMaxParam))]
        private void SetupParams() {
            OffsetsWeightsParam = Parameters[MiniEffectParamCount + 0];
            MinMaxParam = new MiniEffectParam<Vector4>(Parameters[MiniEffectParamCount + 1], 
                new Vector4(0, 0, 1, 1));
        }

        public override Effect Clone()
            => new BlurEffect(this);

        protected override void OnApply() {
            base.OnApply();

            Texture2D? tex = GraphicsDevice.Textures[0] as Texture2D;
            float size = Axis == BlurAxis.X ? tex?.Width ?? 1f : tex?.Height ?? 1f;
            if (size != TextureSizeValue) {
                TextureSizeValue = size;
                OffsetsWeightsValid = false;
            }

            if (!OffsetsWeightsValid) {
                OffsetsWeightsValid = true;

                int radius = RadiusValue;
                int kernelSize = KernelSize;
                int kernelSizeCeil = KernelSizeCeil;
                float strength = StrengthValue;

                Vector4[] values = OffsetsWeightsValue;
                fixed (Vector4* to = values) {
                    Unsafe.InitBlock(to, 0x00, (uint) (sizeof(Vector4) * kernelSizeCeil));

                    if (strength > PrebakedWeightsStrengthMax) {
                        // Slow path, unknown strength, generate on the fly.
                        float sigma = radius / strength;
                        float twoSigmaSquare = 2f * sigma * sigma;
                        float sigmaRoot = MathF.Sqrt(twoSigmaSquare * MathF.PI);
                        float total = 0f;

                        if (Axis == BlurAxis.X) {
                            for (int i = 0; i < kernelSize; i++) {
                                float ri = i - radius;
                                float weight = MathF.Exp(-(ri * ri) / twoSigmaSquare) / sigmaRoot;
                                total += weight;
                                to[i] = new(ri / size, 0f, weight, 0f);
                            }
                        } else {
                            for (int i = 0; i < kernelSize; i++) {
                                float ri = i - radius;
                                float weight = MathF.Exp(-(ri * ri) / twoSigmaSquare) / sigmaRoot;
                                total += weight;
                                to[i] = new(0f, ri / size, weight, 0f);
                            }
                        }

                        for (int i = 0; i < kernelSize; i++)
                            to[i].Z /= total;

                    } else {
                        // Fast path, known strength, either blend or straight up copy.
                        float strengthIndex = strength * PrebakedWeightsStrengthDivs;
                        float strengthWeight = strengthIndex % 1f;
                        if (0.001f < strengthWeight && strengthWeight < 0.999f) {
                            int strengthIndexA = (int) MathF.Floor(strengthIndex);
                            int strengthIndexB = (int) MathF.Ceiling(strengthIndex);
                            fixed (uint* fromABin = PrebakedWeights[radius][strengthIndexA])
                            fixed (uint* fromBBin = PrebakedWeights[radius][strengthIndexB]) {
                                Vector4* fromA = (Vector4*) fromABin;
                                Vector4* fromB = (Vector4*) fromBBin;
                                for (int i = 0; i < kernelSize; i++) {
                                    to[i].Z = fromA[i].Z * strengthWeight + fromB[i].Z * (1f - strengthWeight);
                                }
                            }

                        } else {
                            int strengthIndexA = (int) MathF.Round(strength * PrebakedWeightsStrengthDivs);
                            fixed (uint* fromA = PrebakedWeights[radius][strengthIndexA]) {
                                Buffer.MemoryCopy(fromA, to, sizeof(Vector4) * kernelSizeCeil, sizeof(Vector4) * kernelSize);
                            }
                        }

                        if (Axis == BlurAxis.X) {
                            for (int i = 0; i < kernelSize; i++)
                                to[i].X = (i - radius) / size;
                        } else {
                            for (int i = 0; i < kernelSize; i++)
                                to[i].Y = (i - radius) / size;
                        }
                    }
                }

                OffsetsWeightsParam.SetValue(values);
            }

            MinMaxParam.Apply();
        }

    }

    public enum BlurAxis {
        X,
        Y
    }

    public sealed partial class BlurEffectCache : MiniEffectCache<int> {

        private readonly string Path;

        private readonly Dictionary<int, string> KeyToName = new();
        private readonly Dictionary<string, byte[]?> NameToData = new();

        private readonly Dictionary<int, Reloadable<MiniEffect, NullMeta>> Effects = new();

        public BlurEffectCache(string path) {
            Path = path;
        }

        public override object?[] GetWarmupKeys() {
            return WarmupKeys;
        }

        public override byte[]? GetData(int radius) {
            int radiusCeil = (int) MathF.Ceiling((float) radius / BlurEffect.Step) * BlurEffect.Step;

            if (!KeyToName.TryGetValue(radiusCeil, out string? name) && !Names.TryGetValue(radiusCeil, out name))
                throw new IndexOutOfRangeException(nameof(radiusCeil));

            if (NameToData.TryGetValue(name, out byte[]? data))
                return data;

            return NameToData[name] = Assets.OpenData(string.Format(Path, name));
        }

        public override IReloadable<MiniEffect, NullMeta> GetEffect(Func<GraphicsDevice> gd, int radius) {
            int radiusCeil = (int) MathF.Ceiling((float) radius / BlurEffect.Step) * BlurEffect.Step;

            if (Effects.TryGetValue(radiusCeil, out Reloadable<MiniEffect, NullMeta>? effect))
                return effect;

            return Effects[radiusCeil] = new($"{nameof(BlurEffect)}<{radius}>", default, () => new BlurEffect(gd(), radius));
        }

    }
}

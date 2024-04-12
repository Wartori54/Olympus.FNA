using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using OlympUI;
using OlympUI.Events;
using OlympUI.MegaCanvas;
using Olympus.Effects;
using System;

namespace Olympus {

    /// <summary>
    /// An extension to an Image to support drawing it on grayscale, this is a separate element because of
    /// the need of a shader, otherwise this could be a draw_modifier
    /// </summary>
    public partial class GrayscalableImage : Image, IMouseEventReceiver {
        protected Style.Entry StyleIntensity = new(new FloatFader(0f));
        // public float coolerIntensity = (0f);

        public new static readonly Style DefaultStyle = new() {
            { 
                StyleKeys.Normal, 
                new Style() {
                    { StyleKeys.Intensity, 0f }
                }
            },
            {
                StyleKeys.Grayed, 
                new Style() {
                    { StyleKeys.Intensity, 1f }
                }
            }
        };

        private float PrevIntensity = 0f;

        private RenderTarget2DRegion? Grayscaled;

        public GrayscalableImage(IReloadable<Texture2D, Texture2DMeta> texture) : base(texture) {
        }

        public override void DrawContent() {
            StyleIntensity.GetCurrent(out float intensity);
            // float intensity = coolerIntensity;

            if (intensity != PrevIntensity || Grayscaled == null) {
                PrevIntensity = intensity;

                Point whFull = WH;
                RenderTarget2DRegion? grayscaled = Grayscaled;
                if (grayscaled == null || grayscaled.RT.IsDisposed || 
                    grayscaled.RT.Width < whFull.X || grayscaled.RT.Height < whFull.Y) {
                    grayscaled?.Dispose();

                    grayscaled = (CachePool ?? UI.MegaCanvas.DefaultPool).Get(whFull.X, whFull.Y)
                        ?? throw new Exception($"{nameof(GrayscalableImage)} tried to obtain texture from pool but failed!");
                }

                GrayscaleEffect effect = (GrayscaleEffect)GrayscaleEffect.Cache
                    .GetEffect(() => UI.Game.GraphicsDevice, null).Value;

                Texture2D tex = Texture.Value;
                
                UIDraw.Push(grayscaled, null);
                UIDraw.Recorder.Add(
                    (grayscaled, effect, tex, new Rectangle(0, 0, tex.Width, tex.Height), new Rectangle(0, 0, whFull.X, whFull.Y), intensity),
                    static ((RenderTarget2DRegion grayscaled, GrayscaleEffect effect, Texture2D tex, Rectangle src, Rectangle dest, float intensity) data) => {
                        GraphicsDevice gd = UI.Game.GraphicsDevice;
                        SpriteBatch spriteBatch = UI.SpriteBatch;
                        
                        // Make sure to apply the effect settings right before rendering, otherwise those may be overwritten before its rendered
                        data.effect.Intensity = data.intensity;
                        
                        data.grayscaled.RT.SetRenderTargetUsage(RenderTargetUsage.PlatformContents);
                        gd.SetRenderTarget(data.grayscaled.RT);
                        gd.Clear(Color.Transparent); // Pool textures may be dirty
                        data.effect.TransformParam.Value = UI.CreateTransform(-UI.TransformOffset);
                        data.grayscaled.RT.SetRenderTargetUsage(RenderTargetUsage.PreserveContents);
                        spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.Opaque, SamplerState.LinearClamp, 
                            DepthStencilState.None, RasterizerState.CullCounterClockwise, data.effect);
                        new UICmd.Sprite(data.tex, data.src, data.dest, Color.White).Invoke();
                        spriteBatch.End();
                    }
                );
                UIDraw.Pop();
                Grayscaled = grayscaled;
            }

            DrawModifiable(new UICmd.Sprite(
                Grayscaled.RT,
                new Rectangle(0, 0, WH.X, WH.Y),
                ScreenXYWH,
                StyleColor.GetCurrent<Color>()
            ));
        }

        public void OnClick(MouseEvent.Click e) {
            // Console.WriteLine("Pre click " + StyleIntensity.GetCurrent<float>());
            // if (StyleIntensity.GetCurrent<float>() == 1f) {
            //     Style.Apply(StyleKeys.Normal);
            // } else {
            //     Style.Apply(StyleKeys.Grayed);
            // }
            // Console.WriteLine("On click " + StyleIntensity.GetCurrent<float>());
        }
        
        public new abstract partial class StyleKeys {
            public static readonly Style.Key Normal = new("Normal");
            public static readonly Style.Key Grayed = new("Grayed");
        }
    }
}
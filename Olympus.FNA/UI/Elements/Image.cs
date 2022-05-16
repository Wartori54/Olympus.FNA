﻿using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OlympUI {
    public abstract partial class ImageBase : Element {

        public IReloadable<Texture2D, Texture2DMeta> Texture;
        public bool DisposeTexture;

        public int TextureWidth => Texture.Meta.Width;
        public int TextureHeight => Texture.Meta.Height;

        public int AutoW {
            get {
                Texture2DMeta tex = Texture.Meta;
                return (int) ((H / (float) tex.Height) * tex.Width);
            }
            set {
                W = value;
                H = AutoH;
            }
        }

        public int AutoH {
            get {
                Texture2DMeta tex = Texture.Meta;
                return (int) ((W / (float) tex.Width) * tex.Height);
            }
            set {
                H = value;
                W = AutoW;
            }
        }

        protected Style.Entry StyleColor;

        public ImageBase(IReloadable<Texture2D, Texture2DMeta> texture) {
            Style.GetEntry(out StyleColor);

            Texture = texture;
            Texture2DMeta tex = texture.Meta;
            WH = new Point(tex.Width, tex.Height);
        }

        protected override void Dispose(bool disposing) {
            if (IsDisposed)
                return;
            base.Dispose(disposing);

            if (DisposeTexture)
                Texture.Dispose();
        }

        public override void Update(float dt) {
            base.Update(dt);
            Texture.LifeBump();
        }

        public override void UpdateHidden(float dt) {
            base.UpdateHidden(dt);
            Texture.LifeBump();
        }

        public override void DrawContent() {
            SpriteBatch.Draw(Texture.Value, ScreenXYWH, StyleColor.GetCurrent<Color>());
        }

    }

    public partial class Image : ImageBase {

        public static readonly new Style DefaultStyle = new() {
            new ColorFader(Color.White),
        };

        public static readonly Style DefaultStyleLight = new() {
            new ColorFader(Color.White),
        };

        public Image(IReloadable<Texture2D, Texture2DMeta> texture)
            : base(texture) {
        }

    }

    public partial class Icon : ImageBase {

        public static readonly new Style DefaultStyle = new() {
            Label.DefaultStyle.GetLink<Color>(),
        };

        public Icon(IReloadable<Texture2D, Texture2DMeta> texture)
            : base(texture) {
        }

    }
}

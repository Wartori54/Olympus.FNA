using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;

namespace OlympUI {
    public static class UICmd {

        public record struct Sprite : IRecorderCmd {
            public Texture2D Texture;
            public Rectangle Source;
            public Vector2 Position;
            public Vector2 Scale;
            public float Rotation;
            public Vector2 Origin;
            public Color Color;

            public Sprite(Texture2D texture, Rectangle source, Rectangle destination, Color color) {
                Texture = texture;
                Source = source;
                Position = new(destination.X, destination.Y);
                Scale = new(destination.Width / (float) source.Width, destination.Height / (float) source.Height);
                Rotation = 0f;
                Origin = new(0f, 0f);
                Color = color;
            }

            public Sprite(Texture2D texture, Rectangle source, Vector2 position, Vector2 scale, Color color) {
                Texture = texture;
                Source = source;
                Position = position;
                Scale = scale;
                Rotation = 0f;
                Origin = new(0f, 0f);
                Color = color;
            }

            public Sprite(Texture2D texture, Rectangle source, Vector2 position, Vector2 scale, float rotation, Vector2 origin, Color color) {
                Texture = texture;
                Source = source;
                Position = position;
                Scale = scale;
                Rotation = rotation;
                Origin = origin;
                Color = color;
            }

            public void Invoke() => UI.SpriteBatch.Draw(
                Texture,
                Position,
                Source,
                Color,
                Rotation,
                Origin,
                Scale,
                SpriteEffects.None,
                0f
            );
        }

        public record struct DebugRect(Color Color, Rectangle Rectangle) : IRecorderCmd {
            public void Invoke() => UI.SpriteBatch.DrawDebugRect(Color, Rectangle);
        }

        public record struct Text(DynamicSpriteFont Font, string Value, Vector2 Position, Color Color, 
            FontSystemEffect effect = FontSystemEffect.None, int effectAmount = 0) : IRecorderCmd {
            public void Invoke() => UI.SpriteBatch.DrawString(Font, Value, Position, Color, effect: effect, effectAmount: effectAmount);
        }

    }
}

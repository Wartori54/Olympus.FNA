using FontStashSharp;

namespace OlympUI;

public static class CustomGlyphRenderer {
    public enum FontSystemEffectExtension {
        None,
        Blurry,
        Stroked,
        // Vanilla ends here
        Outline
    }

    public static GlyphRenderer Custom => (input, output, options) => {
        if (options.Effect.Is(FontSystemEffectExtension.Outline)) {
            int outLineSize = options.EffectAmount/2;
            int outLineDistSq = outLineSize * outLineSize;
			int bufferSize = options.Size.X * options.Size.Y;
            for (int i = 0; i < bufferSize; ++i) {
            	int ci = i * 4;
            	byte c = input[i];
                byte originalC = c;
                int newI = 0;
                if (c == 255) goto afterLoops;
                // Check surroundings and use the biggest value
                for (int j = -outLineSize; j <= outLineSize; j++) {
                    if (j < 0 && i % options.Size.X == 0) continue; // we are on an edge and going left
                    if (j > 0 && i % options.Size.X == options.Size.X - 1) continue; // same thing the other way
                    for (int k = -outLineSize; k <= outLineSize; k++) {
                        if (k*k + j*j >= outLineDistSq) continue;
                        newI = i + j + k * options.Size.X;
                        if (newI < 0 || newI >= bufferSize) continue; // OOB check!
                        if (input[newI] > c)
                            c = input[newI];
                        if (c == 255) // We cannot get any bigger than this
                            goto afterLoops;
                    }
                }

                afterLoops:
                c -= (byte) (originalC / 2);
                
            	if (options.PremultiplyAlpha)
            	{
            		output[ci] = output[ci + 1] = output[ci + 2] = output[ci + 3] = c;
            	}
            	else
            	{
            		output[ci] = output[ci + 1] = output[ci + 2] = 255;
            		output[ci + 3] = c;
            	}
            }
        } else {
            GlyphRenderers.Default(input, output, options);
        }
    };

    public static bool Is(this FontSystemEffect value, FontSystemEffectExtension target) {
        return (int) value == (int) target;
    }

    public static FontSystemEffect AsVanilla(this FontSystemEffectExtension value) => (FontSystemEffect) value;
}
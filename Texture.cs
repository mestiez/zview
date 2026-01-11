using SDL;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace zview;

public unsafe class Texture : IDisposable
{
    public required SDL_Texture* Handle;
    public required int Width, Height;

    public static Texture Load<TPixel>(SDL_Renderer* renderer, Image<TPixel> image) where TPixel : unmanaged, IPixel<TPixel>
    {
        using var img = image;
        var surface1 = SDL3.SDL_CreateSurface(img.Width, img.Height, SDL_PixelFormat.SDL_PIXELFORMAT_RGBA8888);
        var pixelsIn = image.Frames.RootFrame.PixelBuffer;
        var pixelsOut = (byte*)surface1->pixels;

        long i = 0;
        var pitchIndex = 0;
        var pixel = new Rgba32();
        for (int y = 0; y < surface1->h; y++)
        {
            pitchIndex = 0;
            
            for (int x = 0; x < surface1->w; x++)
            {
                pixelsIn[x, y].ToRgba32(ref pixel);
                pixelsOut[i++] = pixel.A;
                pixelsOut[i++] = pixel.B;
                pixelsOut[i++] = pixel.G;
                pixelsOut[i++] = pixel.R;
                pitchIndex+=4;
            }

            while (pitchIndex < surface1->pitch) // you can probably do this in a smarter way but
            {
                pitchIndex++;
                i++; // im kind of retarded
            }
        }

        var t = SDL3.SDL_CreateTextureFromSurface(renderer, surface1);
        SDL3.SDL_DestroySurface(surface1);
        return new Texture
        {
            Height = img.Height,
            Width = img.Width,
            Handle = t
        };
    }

    public static Texture Load(SDL_Renderer* renderer, string path) => Load(renderer, Image.Load<Rgba32>(path));

    public void Dispose()
    {
        SDL3.SDL_DestroyTexture(Handle);
    }
}
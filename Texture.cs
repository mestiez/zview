using SDL;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Image = SixLabors.ImageSharp.Image;

namespace zview;

public unsafe class Texture : IDisposable
{
    public required Image<Rgba32>? Image;
    public required SDL_Surface* SurfaceHandle;
    public required SDL_Texture* TextureHandle;
    public required int Width, Height;
    public FileInfo? SourceFile;

    private double time;
    private int frameIndex = 0;
    private int lastFrameRendered = -1;

    public void Update(double dt)
    {
        if (Image is null || Image.Frames.Count <= 1)
            return;

        // if the image is animated, we should probably animate it
        time += dt;

        var frame = Image.Frames[frameIndex % Image.Frames.Count];
        if (lastFrameRendered != frameIndex)
        {
            lastFrameRendered = frameIndex;
            UploadFrameToTexture(frame);
        }

        if (frame.Metadata.TryGetGifMetadata(out var gif))
        {
            if (gif.FrameDelay > 0 && time * 100 > gif.FrameDelay)
            {
                time = 0;
                frameIndex++;
            }
        }
        else if (frame.Metadata.TryGetPngMetadata(out var png))
        {
            var d = png.FrameDelay.ToDouble();
            if (d > 0 &&time > d)
            {
                time = 0;
                frameIndex++;
            }
        }        
        else if (frame.Metadata.TryGetWebpFrameMetadata(out var webp))
        {
            if (webp.FrameDelay > 0 &&time * 1000 > webp.FrameDelay)
            {
                time = 0;
                frameIndex++;
            }
        }
    }

    private void UploadFrameToTexture(ImageFrame<Rgba32> frame)
    {
        var pixelsIn = frame.PixelBuffer;
        var pixelsOut = (byte*)SurfaceHandle->pixels;

        long i = 0;
        var pixel = new Rgba32();
        for (int y = 0; y < SurfaceHandle->h; y++)
        {
            for (int x = 0; x < SurfaceHandle->w; x++)
            {
                pixelsIn[x, y].ToRgba32(ref pixel);
                pixelsOut[i++] = pixel.A;
                pixelsOut[i++] = pixel.B;
                pixelsOut[i++] = pixel.G;
                pixelsOut[i++] = pixel.R;
            }

            i += SurfaceHandle->pitch - SurfaceHandle->w * 4;
        }

        var rect = new SDL_Rect
        {
            x = 0, y = 0,
            w = Width, h = Height,
        };
        SDL3.SDL_UpdateTexture(TextureHandle, &rect, SurfaceHandle->pixels, SurfaceHandle->pitch);
    }

    public static Texture Load(SDL_Renderer* renderer, Image<Rgba32> img)
    {
        var surface = SDL3.SDL_CreateSurface(img.Width, img.Height, SDL_PixelFormat.SDL_PIXELFORMAT_RGBA8888);
        var texture = new Texture
        {
            Image = img,
            Height = img.Height,
            Width = img.Width,
            TextureHandle = SDL3.SDL_CreateTexture(renderer, surface->format,
                SDL_TextureAccess.SDL_TEXTUREACCESS_STATIC, img.Width, img.Height),
            SurfaceHandle = surface
        };

        texture.UploadFrameToTexture(img.Frames.RootFrame);

        return texture;
    }

    public static Texture Load(SDL_Renderer* renderer, string path)
    {
        var x = Load(renderer, SixLabors.ImageSharp.Image.Load<Rgba32>(File.ReadAllBytes(path)));
        x.SourceFile = new(path);
        return x;
    }

    public static Texture Load(SDL_Renderer* renderer, ReadOnlySpan<byte> data) =>
        Load(renderer, SixLabors.ImageSharp.Image.Load<Rgba32>(data));

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        SDL3.SDL_DestroyTexture(TextureHandle);
        SDL3.SDL_DestroySurface(SurfaceHandle);
        Image?.Dispose();
    }
}
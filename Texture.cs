using System.Runtime.InteropServices;
using SDL3;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace zview;

public unsafe class Texture : IDisposable
{
    public required Image<Rgba32>? Image;
    public required SDL.Surface* SurfaceHandle;
    public required nint TextureHandle;
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
            if (d > 0 && time > d)
            {
                time = 0;
                frameIndex++;
            }
        }
        else if (frame.Metadata.TryGetWebpFrameMetadata(out var webp))
        {
            if (webp.FrameDelay > 0 && time * 1000 > webp.FrameDelay)
            {
                time = 0;
                frameIndex++;
            }
        }
    }

    private void UploadFrameToTexture(ImageFrame<Rgba32> frame)
    {
        var pixelsOut = (byte*)SurfaceHandle->Pixels;

        frame.ProcessPixelRows(img =>
        {
            for (var y = 0; y < img.Height; y++)
            {
                var row = img.GetRowSpan(y);
                fixed (Rgba32* rowPtr = row)
                    NativeMemory.Copy(rowPtr, &pixelsOut[y * SurfaceHandle->Pitch], (UIntPtr)(row.Length * 4));
            }
        });

        var rect = new SDL.Rect
        {
            X = 0,
            Y = 0,
            W = Width,
            H = Height,
        };

        SDL.UpdateTexture(TextureHandle, rect, SurfaceHandle->Pixels, SurfaceHandle->Pitch);
    }

    public static Texture Load(nint renderer, Image<Rgba32> img)
    {
        var surface = (SDL.Surface*)SDL.CreateSurface(img.Width, img.Height, SDL.PixelFormat.ABGR8888);
        var texture = new Texture
        {
            Image = img,
            Height = img.Height,
            Width = img.Width,
            TextureHandle = SDL.CreateTexture(renderer, surface->Format,
                SDL.TextureAccess.Static, img.Width, img.Height),
            SurfaceHandle = surface
        };

        texture.UploadFrameToTexture(img.Frames.RootFrame);

        return texture;
    }

    public static Texture Load(nint renderer, string path)
    {
        Texture x;
        x = Load(renderer, SixLabors.ImageSharp.Image.Load<Rgba32>(File.ReadAllBytes(path)));
        x.SourceFile = new(path);
        return x;
    }

    public static Texture Load(nint renderer, ReadOnlySpan<byte> data) =>
        Load(renderer, SixLabors.ImageSharp.Image.Load<Rgba32>(data));

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        SDL.DestroyTexture(TextureHandle);
        SDL.DestroySurface((nint)SurfaceHandle);
        Image?.Dispose();
    }
}
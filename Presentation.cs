using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using SDL3;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Qoi;

namespace zview;

public unsafe class Presentation : IDisposable
{
    public Texture? Texture { get; private set; }
    public bool IsOpen = true;

    public SmoothVec2 Pan = new(Vector2.Zero);
    public SmoothDouble Zoom = new(1);
    public SmoothDouble Rotation = new(0);
    public SmoothVec2 Scale = new(Vector2.One);
    public SmoothDouble Background = new(0, 1e-3);

    // currently accepted by default imagesharp decoder config
    public static readonly string[] AcceptedExtensions =
    [
        ".tga",
        ".tiff",
        ".png",
        ".jpeg",
        ".jpg",
        ".pbm",
        ".webp",
        ".qoi",
        ".gif"
    ];

    private double time;
    private bool autoFit = true;
    private bool autoSizeWindow = false;
    private SDL.ScaleMode filter;
    private bool showInfo;

    private readonly nint window;
    private readonly nint renderer;
    private readonly SDL.Event[] eventBuffer = new SDL.Event[8];

    private float mouseX, mouseY;
    private float mouseWheel;
    private readonly MouseBtnState[] mouseBtns = new MouseBtnState[8];
    private readonly ulong[] touchDevices;
    private readonly TouchInterpreter touchInterpreter = new();
    private Matrix4x4 canvasToScreenMat;
    private FontTextureAtlas? fontAtlas;
    private string? currentInfo;
    private FileInfo[] currentQueue = [];
    private int currentIndexInQueue = 0;

    private readonly SDL.ClipboardDataCallback clipboardDataCallback;
    private readonly SDL.ClipboardCleanupCallback clipboardCleanupCallback;

    private Dictionary<nint, nint> clipboardData = [];

    private static readonly SDL.FColor White = new SDL.FColor { R = 1, G = 1, B = 1, A = 1 };

    private readonly SDL.Vertex[] verts =
    [
        new SDL.Vertex
        {
            Color = White, Position = new SDL.FPoint { X = 0, Y = 0 },
            TexCoord = new SDL.FPoint { X = 0, Y = 0 }
        },
        new SDL.Vertex
        {
            Color = White, Position = new SDL.FPoint { X = 1, Y = 0 },
            TexCoord = new SDL.FPoint { X = 1, Y = 0 }
        },
        new SDL.Vertex
        {
            Color = White, Position = new SDL.FPoint { X = 1, Y = 1 },
            TexCoord = new SDL.FPoint { X = 1, Y = 1 }
        },
        new SDL.Vertex
        {
            Color = White, Position = new SDL.FPoint { X = 0, Y = 1 },
            TexCoord = new SDL.FPoint { X = 0, Y = 1 }
        },
    ];

    private readonly int[] idx =
    [
        0, 1, 3,
        3, 2, 1
    ];

    public Presentation()
    {
        const SDL.WindowFlags flags = SDL.WindowFlags.Vulkan | SDL.WindowFlags.Resizable;
        SDL.Init(SDL.InitFlags.Events | SDL.InitFlags.Video);

        SDL.CreateWindowAndRenderer(nameof(zview), 512, 512, flags, out var w, out var r);

        window = w;
        renderer = r;

        using var iconTex = Texture.Load(renderer, GetResource("zview.icon.qoi"));
        SDL.SetWindowIcon(window, (nint)iconTex.SurfaceHandle);

        var td = SDL.GetTouchDevices(out var touchDeviceCount);
        if (td is not null)
        {
            touchDevices = new ulong[touchDeviceCount];
            for (var i = 0; i < touchDevices.Length; i++)
                touchDevices[i] = td[i];
        }
        else
            touchDevices = [];

        using var s = GetResourceStream("zview.haxor-12.bdf");
        var font = BdfFont.Load(s);
        fontAtlas = font.CreateAtlas(renderer);

        clipboardDataCallback = OnSetClipboard;
        clipboardCleanupCallback = OnSetClipboardCleanup;

        GCHandle.Alloc(clipboardDataCallback);
        GCHandle.Alloc(clipboardCleanupCallback);
    }

    public void SetTexture(Texture texture)
    {
        if (autoSizeWindow)
            SDL.SetWindowSize(window, texture.Width, texture.Height);
        else if (Texture is null)
        {
            SDL.GetDisplayBounds(SDL.GetDisplayForWindow(window), out var screen);
            var sx = int.Clamp(texture.Width, 256, int.Max(256, screen.W / 2));
            var sy = int.Clamp(texture.Height, 256, int.Max(256, screen.H / 2));
            SDL.SetWindowSize(window, sx, sy);
            if (sx != texture.Width || sy != texture.Height) // we mustve clamped it... auto fit
                autoFit = true;
            else
                ResetView();
        }

        Texture?.Dispose();
        Texture = texture;
        GetQueue(out currentIndexInQueue, out currentQueue);

        var b = new StringBuilder();
        if (Texture?.SourceFile is not null)
        {
            if (currentQueue is { Length: > 1 })
                b.AppendLine($"queue: {currentIndexInQueue + 1} / {currentQueue.Length}");

            b.AppendLine($"file: {Texture.SourceFile.Name}");
            b.AppendLine($"path: {Texture.SourceFile.FullName}");
            b.AppendLine($"size: {HumanReadableByteCount(Texture.SourceFile.Length)}");
            b.AppendLine();

            static string HumanReadableByteCount(long b)
            {
                return b switch
                {
                    >= 1000_000_000 => $"{(b / 1e9):N1} GB",
                    >= 1000_000 => $"{(b / 1e6):N1} MB",
                    >= 1000 => $"{(b / 1e3):N1} kB",
                    _ => $"{b} B"
                };
            }
        }
        else
            b.AppendLine("image: raw");

        if (Texture is not null)
        {
            b.AppendLine($"dimensions: {Texture.Width}x{Texture.Height}");
            if (Texture.Image is not null)
            {
                var img = Texture.Image;
                if (img.Metadata.DecodedImageFormat is not null)
                {
                    if (img.Frames.Count > 1)
                    {
                        b.AppendLine($"frame count: {img.Frames.Count}");
                        float w = 0, averageFrameRate = 0;
                        foreach (var frame in img.Frames)
                        {
                            if (frame.Metadata.TryGetGifMetadata(out var gif))
                            {
                                w++;
                                averageFrameRate += 100f / gif.FrameDelay;
                            }
                            else if (frame.Metadata.TryGetWebpFrameMetadata(out var webp))
                            {
                                w++;
                                averageFrameRate += 1000f / webp.FrameDelay;
                            }
                            else if (frame.Metadata.TryGetPngMetadata(out var png))
                            {
                                w++;
                                averageFrameRate += 1 / png.FrameDelay.ToSingle();
                            }
                        }

                        b.AppendLine($"average framerate: {(averageFrameRate / w):N} FPS");
                    }

                    b.AppendLine($"format: {img.Metadata.DecodedImageFormat.Name}");
                    switch (img.Metadata.DecodedImageFormat)
                    {
                        case PngFormat:
                        {
                            var m = img.Metadata.GetPngMetadata();
                            b.AppendLine($"\tcolor type: {m.ColorType}");
                        }
                            break;
                        case QoiFormat:
                        {
                            var m = img.Metadata.GetQoiMetadata();
                            b.AppendLine($"\tcolorspace: {m.ColorSpace}");
                        }
                            break;
                        case JpegFormat:
                        {
                            var m = img.Metadata.GetJpegMetadata();
                            b.AppendLine($"\tcolor type: {m.ColorType}");
                            b.AppendLine($"\tquality: {m.Quality}");
                        }
                            break;
                    }
                }
            }
        }

        currentInfo = b.ToString();
    }

    public void RunLoop()
    {
        var clock = new Stopwatch();
        clock.Start();
        while (IsOpen)
        {
            var dt = clock.Elapsed.TotalSeconds;
            clock.Restart();

            ProcessEvents();
            ProcessControls();

            var b = (byte)(255 * (Background.Smoothed));
            SDL.SetRenderDrawColor(renderer, b, b, b, 255);
            SDL.RenderClear(renderer);

            Render(dt);

            SDL.RenderPresent(renderer);
        }
    }

    private static Stream GetResourceStream(string path)
    {
        var a = Assembly.GetCallingAssembly()!;
        return a!.GetManifestResourceStream(path)!;
    }

    private static ReadOnlySpan<byte> GetResource(string path, int bufferSize = 2048)
    {
        using var s = GetResourceStream(path);
        var data = new byte[bufferSize];
        return data.AsSpan(..s!.Read(data, 0, data.Length));
    }

    private void ResetView()
    {
        autoFit = false;
        Pan.Value = Vector2.Zero;
        Scale.Value = Vector2.One;
        Zoom.Value = 1;
        Rotation.Value = 0;
        Rotation.Smoothed %= float.Tau;
    }

    private void Render(double dt)
    {
        time += dt;
        Texture?.Update(dt);

        Matrix4x4.Invert(canvasToScreenMat, out var screenToCanvasMat);

        var mouseDown = SDL.GetMouseState(out var mX, out var mY);
        var canvasMouse = Vector2.Transform(new Vector2(mX, mY), screenToCanvasMat);
        var canvasMouseDelta = canvasMouse - Vector2.Transform(new Vector2(mouseX, mouseY), screenToCanvasMat);

        mouseX = mX;
        mouseY = mY;

        SDL.GetWindowSize(window, out var w, out var h);

        if (SDL.GetMouseFocus() == window)
        {
            if (mouseDown != 0 && !touchInterpreter.IsBeingUsed)
            {
                if (mouseBtns[1] != MouseBtnState.Pressed)
                {
                    Pan.Value.X -= canvasMouseDelta.X;
                    Pan.Value.Y -= canvasMouseDelta.Y;
                    autoFit = false;
                }
            }

            {
                const float epsilon = 0.1f;
                if (mouseWheel > epsilon)
                    AdjustZoom(0.1f);
                else if (mouseWheel < -epsilon)
                    AdjustZoom(-0.1f);

                void AdjustZoom(float s)
                {
                    Pan.Value += (canvasMouse - Pan.Value) * s;
                    Zoom.Value -= Zoom.Value * s;
                    autoFit = false;
                }
            }

            foreach (var touchDevice in touchDevices)
            {
                var fingers = SDL.GetTouchFingers(touchDevice, out var fingerCount);
                if (fingers is not null)
                    if (touchInterpreter.Update(fingers, w, h, ref Pan.Value, ref Zoom.Value, screenToCanvasMat))
                        autoFit = false;
            }
        }

        Background.Update(dt);
        Pan.Update(dt);
        Zoom.Update(dt);
        Rotation.Update(dt);
        Scale.Update(dt);

        if (Texture is not null)
        {
            if (autoFit)
            {
                var size = new Vector2(Texture.Width, Texture.Height);
                size = Vector2.Abs(Vector2.Transform(size, Matrix3x2.CreateRotation((float)Rotation.Value)));
                var aspectRatio = (size.Y / size.X);

                if (h / (float)w < aspectRatio)
                    Zoom.Value = size.Y / h;
                else
                    Zoom.Value = size.X / w;

                Pan.Value = default;
                Pan.Smoothed = Pan.Value;
                Zoom.Smoothed = Zoom.Value;
            }

            var s = (float)(Zoom.Smoothed * 2);
            canvasToScreenMat =
                Matrix4x4.CreateTranslation(new Vector3(-Pan.Smoothed, 0)) *
                Matrix4x4.CreateOrthographic(s, s, 0.01f, 1) *
                Matrix4x4.CreateTranslation(new Vector3(w / 2f, h / 2f, 0));

            var o = Vector2.Zero;

            SDL.SetTextureScaleMode(Texture.TextureHandle, filter);

            SDL.Vertex[] vertsCopy = [.. verts];

            var transform = new Matrix4x4(
                Matrix3x2.CreateScale(Texture.Width, Texture.Height) *
                Matrix3x2.CreateTranslation(o + new Vector2(Texture.Width, Texture.Height) * -0.5f) *
                Matrix3x2.CreateScale(Scale.Smoothed, o) *
                Matrix3x2.CreateRotation((float)Rotation.Smoothed, o)
            ) * canvasToScreenMat;

            for (var i = 0; i < vertsCopy.Length; i++)
            {
                ref var v = ref vertsCopy[i];
                var p = Vector2.Transform(new Vector2(v.Position.X, v.Position.Y), transform);
                v.Position = new SDL.FPoint
                {
                    X = p.X,
                    Y = p.Y
                };
            }

            SDL.RenderGeometry(renderer, Texture.TextureHandle, vertsCopy, 4, idx, 6);

            if (showInfo)
            {
                var rect = new SDL.FRect
                {
                    X = 0,
                    Y = 0,
                    W = w,
                    H = h,
                };

                SDL.SetRenderDrawBlendMode(renderer, SDL.BlendMode.Blend);
                SDL.SetRenderDrawColorFloat(renderer, 0, 0, 0, 0.8f);
                SDL.RenderFillRect(renderer, rect);

                if (!string.IsNullOrWhiteSpace(currentInfo) && fontAtlas is not null)
                    RenderText(fontAtlas, currentInfo, 16, 16);
            }
        }
    }

    private void ProcessControls()
    {
        var keyState = SDL.GetKeyboardState(out var keyCount);

        if (keyState[(int)SDL.Scancode.Escape] || keyState[(int)SDL.Scancode.Q])
            IsOpen = false;

        if (keyState[(int)SDL.Scancode.Home])
            ResetView();

        showInfo = keyState[(int)SDL.Scancode.I];
    }

    private void ProcessKeyDown(SDL.KeyboardEvent e)
    {
        switch (e.Scancode)
        {
            case SDL.Scancode.F:
            {
                filter = filter switch
                {
                    SDL.ScaleMode.Nearest => SDL.ScaleMode.Linear,
                    _ => SDL.ScaleMode.Nearest
                };
                break;
            }
            case SDL.Scancode.W:
            {
                if (e.Mod.HasFlag(SDL.Keymod.LCtrl))
                {
                    autoSizeWindow = !autoSizeWindow;
                    if (autoSizeWindow && Texture is not null)
                        SDL.SetWindowSize(window, Texture.Width, Texture.Height);
                }
                else if (Texture is not null)
                {
                    ResetView();
                    SDL.SetWindowSize(window, Texture.Width, Texture.Height);
                }

                break;
            }
            case SDL.Scancode.R:
            {
                var reverse = e.Mod.HasFlag(SDL.Keymod.LShift);
                Rotation.Value += 1.5707963268 * (reverse ? -1 : 1);
                break;
            }
            case SDL.Scancode.H:
            {
                Scale.Value.X *= -1;
                break;
            }
            case SDL.Scancode.C:
            {
                if (Texture is not null && e.Mod.HasFlag(SDL.Keymod.LCtrl))
                {
                    try
                    {
                        var id = Random.Shared.Next();
                        string[] m = ["image/png", "image/jpeg"];
                        
                        if (!SDL.SetClipboardData(clipboardDataCallback, clipboardCleanupCallback, id, m, (nuint)m.Length))
                        {
                            var err = SDL.GetError();
                            Console.Error.WriteLine(err);
                        }
                        else
                        {
                            Background.Smoothed =
                                Background.Value > 0.5 ? 0.7 : 0.3;
                        }
                    }
                    catch (Exception exception)
                    {
                        Console.Error.WriteLine(exception);
                        throw;
                    }
                }

                break;
            }
            case SDL.Scancode.V:
            {
                if (e.Mod.HasFlag(SDL.Keymod.LCtrl))
                {
                    if (SDL.HasClipboardData("image/png"))
                    {
                        SDL.SetWindowTitle(window, nameof(zview) + " - loading...");
                        var clipboard = SDL.GetClipboardData("image/png", out var clipboardSize);
                        try
                        {
                            var data = new byte[clipboardSize];
                            Marshal.Copy(clipboard, data, 0, data.Length);
                            SetTexture(Texture.Load(renderer, data));
                        }
                        finally
                        {
                            SDL.SetWindowTitle(window, nameof(zview));
                            SDL.Free(clipboard);
                        }
                    }
                    else if (SDL.HasClipboardText())
                    {
                        var p = SDL.GetClipboardText();
                        if (!string.IsNullOrWhiteSpace(p))
                            SetTexture(p);
                    }
                }
                else
                    Scale.Value.Y *= -1;

                break;
            }
            case SDL.Scancode.Right:
            {
                NextInDirectory();
                break;
            }
            case SDL.Scancode.Left:
            {
                PreviousInDirectory();
                break;
            }
            case SDL.Scancode.Period:
            {
                autoFit = true;
                break;
            }
            case SDL.Scancode.B:
            {
                Background.Value = Background.Value > 0.5f ? 0 : 1;
                break;
            }
            case SDL.Scancode.F5:
            {
                if (Texture?.SourceFile is not null)
                {
                    Background.Smoothed =
                        Background.Value > 0.5 ? 0.7 : 0.3; // a little mild flash to indicate refresh :) 
                    SetTexture(Texture.SourceFile.FullName);
                }

                break;
            }
        }
    }

    private void GetQueue(out int index, out FileInfo[] files)
    {
        files = [];
        index = -1;

        if (Texture?.SourceFile is null)
            return;

        var all = Texture.SourceFile.Directory?.GetFiles() ?? [];
        FileInfo[] filtered =
        [
            ..all.Where(f => AcceptedExtensions.Any(l => f.Name.EndsWith(l, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(d => d.Name)
        ];

        if (filtered.Length == 0)
            return;

        files = filtered;
        for (var i = 0; i < files.Length; i++)
        {
            if (files[i].FullName.Equals(Texture.SourceFile.FullName))
            {
                index = i;
                return;
            }
        }
    }

    private void NextInDirectory()
    {
        if (currentQueue is not { Length: > 1 } || currentIndexInQueue == -1)
            return;

        SetTexture(currentQueue[(currentIndexInQueue + 1) % currentQueue.Length].FullName);
        autoFit = true;
    }

    private void PreviousInDirectory()
    {
        if (currentQueue is not { Length: > 1 } || currentIndexInQueue == -1)
            return;

        SetTexture(currentQueue[Wrap(currentIndexInQueue - 1, 0, currentQueue.Length - 1)].FullName);
        autoFit = true;
    }

    // https://stackoverflow.com/questions/707370/clean-efficient-algorithm-for-wrapping-integers-in-c
    // Posted by Lara Bailey, modified by community. See post 'Timeline' for change history
    // Retrieved 2026-01-11, License - CC BY-SA 2.5
    private static int Wrap(int kX, int kLowerBound, int kUpperBound)
    {
        var rangeSize = kUpperBound - kLowerBound + 1;

        if (kX < kLowerBound)
            kX += rangeSize * ((kLowerBound - kX) / rangeSize + 1);

        return kLowerBound + (kX - kLowerBound) % rangeSize;
    }

    private void ProcessEvents()
    {
        Array.Fill(mouseBtns, MouseBtnState.None);
        mouseWheel = 0;

        SDL.PumpEvents();
        var c = SDL.PeepEvents(eventBuffer, eventBuffer.Length, SDL.EventAction.GetEvent,
            (uint)SDL.EventType.First, (uint)SDL.EventType.Last);

        for (int i = 0; i < c; i++)
        {
            var e = eventBuffer[i];
            switch ((SDL.EventType)e.Type)
            {
                case SDL.EventType.WindowCloseRequested:
                    IsOpen = false;
                    break;
                case SDL.EventType.DropFile:
                    var path = Marshal.PtrToStringUTF8(e.Drop.Data);
                    if (path is not null)
                        SetTexture(path);
                    break;
                case SDL.EventType.MouseButtonDown:
                    mouseBtns[e.Button.Button] = MouseBtnState.Pressed;
                    break;
                case SDL.EventType.MouseButtonUp:
                    mouseBtns[e.Button.Button] = MouseBtnState.Released;
                    break;
                case SDL.EventType.MouseWheel:
                    mouseWheel = e.Wheel.Y;
                    break;
                case SDL.EventType.KeyDown:
                {
                    ProcessKeyDown(e.Key);
                    break;
                }
            }
        }
    }

    public bool SetTexture(string path)
    {
        SDL.SetWindowTitle(window, nameof(zview) + " - loading...");
        try
        {
            if (Directory.Exists(path))
            {
                var p = Directory.GetFiles(path).FirstOrDefault(d =>
                    AcceptedExtensions.Any(e => d.EndsWith(e, StringComparison.OrdinalIgnoreCase)));
                if (p is not null)
                    return SetTexture(p);
            }

            var tex = Texture.Load(renderer, path);
            SetTexture(tex);
            SDL.SetWindowTitle(window, nameof(zview) + " - " + Path.GetFileName(path));
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Failed to open \"{path}\": " + exception.Message);
            SDL.SetWindowTitle(window, nameof(zview));
            return false;
        }

        return true;
    }

    private void RenderText(FontTextureAtlas font, ReadOnlySpan<char> text, int x, int y,
        float scale = 1)
    {
        if (font is not { Font: not null, Atlas: not null })
            throw new Exception("Invalid font provided");

        var t = font.Atlas.TextureHandle;
        var cursor = new Vector2(x, y);
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] is '\n')
            {
                cursor.X = x;
                cursor.Y += font.Font.FontSize + 5;
            }
            else if (!char.IsControl(text[i]) && font.Entries.TryGetValue(text[i], out var entry))
            {
                var src = new SDL.FRect
                {
                    X = entry.TextureRect.X,
                    Y = entry.TextureRect.Y,
                    W = entry.TextureRect.W,
                    H = entry.TextureRect.H,
                };

                var dst = new SDL.FRect
                {
                    X = cursor.X + entry.Glyph.Box.X,
                    Y = cursor.Y + entry.Glyph.Box.Y,
                    W = entry.Glyph.Box.W * scale,
                    H = entry.Glyph.Box.H * scale,
                };

                SDL.RenderTexture(renderer, t, src, dst);

                cursor.X += entry.Glyph.MoveX * scale;
                cursor.Y += entry.Glyph.MoveY * scale;
            }
        }
    }

    private void OnSetClipboardCleanup(nint userdata)
    {
        if (clipboardData.Remove(userdata, out var data))
            NativeMemory.Free((void*)data);
    }

    private nint OnSetClipboard(nint userdata, string mimeType, out nuint size)
    {
        size = 0;
        
        if (Texture is null)
            return nint.Zero;
        
        using var dst = new MemoryStream();

        switch (mimeType)
        {
            case "image/png":
                Texture.Image.SaveAsPng(dst);
                break;
            case "image/jpg":
                Texture.Image.SaveAsJpeg(dst);
                break; 
            default:
                return nint.Zero;
        }
        
        var d = dst.ToArray();
        size = (nuint)d.Length;
        var data = (nint)NativeMemory.Alloc(size);
        Marshal.Copy(d, 0, data, d.Length);
        
        clipboardData.Add(userdata, data);
        
        return data;
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        SDL.DestroyRenderer(renderer);
        SDL.DestroyWindow(window);
        Texture?.Dispose();
        fontAtlas?.Dispose();
    }
}
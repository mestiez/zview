using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using SDL;

namespace zview;

public unsafe class Presentation : IDisposable
{
    public Texture? Texture { get; private set; }
    public bool IsOpen = true;

    public SmoothVec2 Pan = new(Vector2.Zero);
    public SmoothDouble Zoom = new(1);
    public SmoothDouble Rotation = new(0);
    public SmoothVec2 Scale = new(Vector2.One);
    public SmoothDouble Background = new(0, 1e-7);

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
    private SDL_ScaleMode filter;

    private readonly SDL_Window* window;
    private readonly SDL_Renderer* renderer;
    private readonly SDL_Event[] eventBuffer = new SDL_Event[8];

    private float mouseX, mouseY;
    private float mouseWheel;
    private readonly MouseBtnState[] mouseBtns = new MouseBtnState[8];
    private readonly SDL_TouchID[] touchDevices;
    private readonly TouchInterpreter touchInterpreter = new();
    private FileInfo? currentFile;
    private Matrix4x4 canvasToScreenMat;

    private static readonly SDL_FColor White = new SDL_FColor { r = 1, g = 1, b = 1, a = 1 };

    private readonly SDL_Vertex[] verts =
    [
        new SDL_Vertex
        {
            color = White, position = new SDL_FPoint { x = 0, y = 0 },
            tex_coord = new SDL_FPoint { x = 0, y = 0 }
        },
        new SDL_Vertex
        {
            color = White, position = new SDL_FPoint { x = 1, y = 0 },
            tex_coord = new SDL_FPoint { x = 1, y = 0 }
        },
        new SDL_Vertex
        {
            color = White, position = new SDL_FPoint { x = 1, y = 1 },
            tex_coord = new SDL_FPoint { x = 1, y = 1 }
        },
        new SDL_Vertex
        {
            color = White, position = new SDL_FPoint { x = 0, y = 1 },
            tex_coord = new SDL_FPoint { x = 0, y = 1 }
        },
    ];

    private readonly int[] idx =
    [
        0, 1, 3,
        3, 2, 1
    ];


    public Presentation()
    {
        SDL3.SDL_Init(SDL_InitFlags.SDL_INIT_EVENTS | SDL_InitFlags.SDL_INIT_VIDEO);
        var flags = SDL_WindowFlags.SDL_WINDOW_VULKAN | SDL_WindowFlags.SDL_WINDOW_RESIZABLE;
        
        SDL_Window* w;
        SDL_Renderer* r;
        SDL3.SDL_CreateWindowAndRenderer(nameof(zview), 512, 512, flags, &w, &r);
        
        window = w;
        renderer = r;
        
        SDL3.SDL_SetRenderVSync(renderer, SDL3.SDL_WINDOW_SURFACE_VSYNC_ADAPTIVE);

        {
            var a = Assembly.GetCallingAssembly()!;
            using var iconStream = a!.GetManifestResourceStream("zview.icon.qoi");
            var data = new byte[2048];
            data = data[..iconStream!.Read(data, 0, data.Length)];
            using var iconTex = Texture.Load(renderer, data);
            SDL3.SDL_SetWindowIcon(window, iconTex.SurfaceHandle);
        }

        using var td = SDL3.SDL_GetTouchDevices();
        if (td is not null)
        {
            touchDevices = new SDL_TouchID[td.Count];
            for (var i = 0; i < touchDevices.Length; i++)
                touchDevices[i] = td[i];
        }
        else
            touchDevices = [];
    }

    public void SetTexture(Texture texture)
    {
        if (Texture == null)
        {
            var screen = new SDL_Rect();
            SDL3.SDL_GetDisplayBounds(SDL3.SDL_GetDisplayForWindow(window), &screen);
            var sx = int.Clamp(texture.Width, 256, int.Max(256, screen.w / 2));
            var sy = int.Clamp(texture.Height, 256, int.Max(256, screen.h / 2));
            SDL3.SDL_SetWindowSize(window, sx, sy);
        }

        Texture?.Dispose();
        Texture = texture;
        ResetView();
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

            SDL3.SDL_RenderClear(renderer);

            var b = (byte)(255 * (Background.Smoothed));
            SDL3.SDL_SetRenderDrawColor(renderer, b, b, b, 255);

            Render(dt);

            SDL3.SDL_RenderPresent(renderer);
        }
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

        float mX = 0, mY = 0;
        var mouseDown = SDL3.SDL_GetMouseState(&mX, &mY);
        var canvasMouse = Vector2.Transform(new Vector2(mX, mY), screenToCanvasMat);
        var canvasMouseDelta = canvasMouse - Vector2.Transform(new Vector2(mouseX, mouseY), screenToCanvasMat);

        mouseX = mX;
        mouseY = mY;

        int w = 0, h = 0;
        SDL3.SDL_GetWindowSize(window, &w, &h);

        if (SDL3.SDL_GetMouseFocus() == window)
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
                using var fingers = SDL3.SDL_GetTouchFingers(touchDevice);
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

        // i actually sincerely do not know why it has to be doubled. it came to me in a dream
        var s = (float)(Zoom.Smoothed * 2);
        canvasToScreenMat =
            Matrix4x4.CreateTranslation(new Vector3(-Pan.Smoothed, 0)) *
            Matrix4x4.CreateOrthographic(s, s, 0.01f, 1) *
            Matrix4x4.CreateTranslation(new Vector3(w / 2f, h / 2f, 0));

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

            var o = Vector2.Zero;

            SDL3.SDL_SetTextureScaleMode(Texture.TextureHandle, filter);

            SDL_Vertex[] vertsCopy = [..verts];

            var transform = new Matrix4x4(
                Matrix3x2.CreateScale(Texture.Width, Texture.Height) *
                Matrix3x2.CreateTranslation(o + new Vector2(Texture.Width, Texture.Height) * -0.5f) *
                Matrix3x2.CreateScale(Scale.Smoothed, o) *
                Matrix3x2.CreateRotation((float)Rotation.Smoothed, o)
            ) * canvasToScreenMat;
            for (var i = 0; i < vertsCopy.Length; i++)
            {
                ref var v = ref vertsCopy[i];
                var p = Vector2.Transform(new Vector2(v.position.x, v.position.y), transform);
                v.position = new SDL_FPoint
                {
                    x = p.X,
                    y = p.Y
                };
            }

            fixed (int* indices = idx)
            fixed (SDL_Vertex* vertices = vertsCopy)
            {
                SDL3.SDL_RenderGeometry(renderer, Texture.TextureHandle, vertices, 4, indices, 6);
            }
        }
    }

    private void ProcessControls()
    {
        var keyCount = 0;
        var keyStateF = SDL3.SDL_GetKeyboardState(&keyCount);
        var keyState = new Span<SDLBool>(keyStateF, keyCount);

        if (keyState[(int)SDL_Scancode.SDL_SCANCODE_ESCAPE] || keyState[(int)SDL_Scancode.SDL_SCANCODE_Q])
            IsOpen = false;

        if (keyState[(int)SDL_Scancode.SDL_SCANCODE_W])
        {
            if (Texture is not null)
            {
                ResetView();
                SDL3.SDL_SetWindowSize(window, Texture.Width, Texture.Height);
            }
        }

        if (keyState[(int)SDL_Scancode.SDL_SCANCODE_HOME])
            ResetView();
    }

    private void ProcessKeyDown(SDL_KeyboardEvent e)
    {
        switch (e.scancode)
        {
            case SDL_Scancode.SDL_SCANCODE_F:
            {
                filter = filter switch
                {
                    SDL_ScaleMode.SDL_SCALEMODE_NEAREST => SDL_ScaleMode.SDL_SCALEMODE_LINEAR,
                    _ => SDL_ScaleMode.SDL_SCALEMODE_NEAREST
                };
                break;
            }
            case SDL_Scancode.SDL_SCANCODE_R:
            {
                var reverse = e.mod.HasFlag(SDL_Keymod.SDL_KMOD_LSHIFT);
                Rotation.Value += 1.5707963268 * (reverse ? -1 : 1);
                break;
            }
            case SDL_Scancode.SDL_SCANCODE_H:
            {
                Scale.Value.X *= -1;
                break;
            }
            case SDL_Scancode.SDL_SCANCODE_V:
            {
                if (e.mod.HasFlag(SDL_Keymod.SDL_KMOD_LCTRL))
                {
                    if (SDL3.SDL_HasClipboardData("image/png"))
                    {
                        UIntPtr clipboardSize = 0;
                        SDL3.SDL_SetWindowTitle(window, nameof(zview) + " - loading...");
                        var clipboard = SDL3.SDL_GetClipboardData("image/png", &clipboardSize);
                        try
                        {
                            var data = new byte[clipboardSize];
                            Marshal.Copy(clipboard, data, 0, data.Length);
                            SetTexture(Texture.Load(renderer, data));
                            currentFile = null;
                        }
                        finally
                        {
                            SDL3.SDL_SetWindowTitle(window, nameof(zview));
                            SDL3.SDL_free(clipboard);
                        }
                    }
                    else if (SDL3.SDL_HasClipboardText())
                    {
                        var p = SDL3.SDL_GetClipboardText();
                        if (!string.IsNullOrWhiteSpace(p))
                        {
                            SetTexture(p);
                            currentFile = null;
                        }
                    }
                }
                else
                    Scale.Value.Y *= -1;

                break;
            }
            case SDL_Scancode.SDL_SCANCODE_RIGHT:
            {
                NextInDirectory();
                break;
            }
            case SDL_Scancode.SDL_SCANCODE_LEFT:
            {
                PreviousInDirectory();
                break;
            }
            case SDL_Scancode.SDL_SCANCODE_PERIOD:
            {
                autoFit = true;
                break;
            }
            case SDL_Scancode.SDL_SCANCODE_B:
            {
                Background.Value = Background.Value > 0.5f ? 0 : 1;
                break;
            }
            case SDL_Scancode.SDL_SCANCODE_F5:
            {
                if (currentFile is not null)
                {
                    Background.Smoothed =
                        Background.Value > 0.5 ? 0.7 : 0.3; // a little mild flash to indicate refresh :) 
                    SetTexture(currentFile.FullName);
                }

                break;
            }
        }
    }

    private void GetQueue(out int index, out FileInfo[] files)
    {
        files = [];
        index = -1;

        if (currentFile is null)
            return;

        var all = currentFile.Directory?.GetFiles() ?? [];
        FileInfo[] filtered =
            [..all.Where(f => AcceptedExtensions.Any(l => f.Name.EndsWith(l, StringComparison.OrdinalIgnoreCase)))];

        if (filtered.Length == 0)
            return;

        files = filtered;
        for (var i = 0; i < files.Length; i++)
        {
            if (files[i].FullName.Equals(currentFile.FullName))
            {
                index = i;
                return;
            }
        }
    }

    private void NextInDirectory()
    {
        GetQueue(out var index, out var files);
        if (index == -1)
            return;

        SetTexture(files[(index + 1) % files.Length].FullName);
        autoFit = true;
    }

    private void PreviousInDirectory()
    {
        GetQueue(out var index, out var files);
        if (index == -1)
            return;

        SetTexture(files[Wrap(index - 1, 0, files.Length - 1)].FullName);
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

        SDL3.SDL_PumpEvents();
        var c = SDL3.SDL_PeepEvents(eventBuffer, SDL_EventAction.SDL_GETEVENT,
            SDL_EventType.SDL_EVENT_FIRST, SDL_EventType.SDL_EVENT_LAST);

        for (int i = 0; i < c; i++)
        {
            var e = eventBuffer[i];
            switch (e.Type)
            {
                case SDL_EventType.SDL_EVENT_WINDOW_CLOSE_REQUESTED:
                    IsOpen = false;
                    break;
                case SDL_EventType.SDL_EVENT_DROP_FILE:
                    var path = e.drop.GetData();
                    if (path is not null)
                        SetTexture(path);
                    break;
                case SDL_EventType.SDL_EVENT_MOUSE_BUTTON_DOWN:
                    mouseBtns[e.button.button] = MouseBtnState.Pressed;
                    break;
                case SDL_EventType.SDL_EVENT_MOUSE_BUTTON_UP:
                    mouseBtns[e.button.button] = MouseBtnState.Released;
                    break;
                case SDL_EventType.SDL_EVENT_MOUSE_WHEEL:
                    mouseWheel = e.wheel.y;
                    break;
                case SDL_EventType.SDL_EVENT_KEY_DOWN:
                {
                    ProcessKeyDown(e.key);
                    break;
                }
            }
        }
    }

    public bool SetTexture(string path)
    {
        SDL3.SDL_SetWindowTitle(window, nameof(zview) + " - loading...");
        try
        {
            if (Directory.Exists(path))
            {
                var p = Directory.GetFiles(path).FirstOrDefault(d =>
                    AcceptedExtensions.Any(e => d.EndsWith(e, StringComparison.OrdinalIgnoreCase)));
                if (p is not null)
                    return SetTexture(p);
            }

            currentFile = null;
            var tex = Texture.Load(renderer, path);
            SetTexture(tex);
            SDL3.SDL_SetWindowTitle(window, nameof(zview) + " - " + Path.GetFileName(path));
            currentFile = new FileInfo(path);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Failed to open \"{path}\": " + exception.Message);
            SDL3.SDL_SetWindowTitle(window, nameof(zview));
            return false;
        }

        return true;
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        SDL3.SDL_DestroyRenderer(renderer);
        SDL3.SDL_DestroyWindow(window);
    }
}
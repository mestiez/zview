using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using SDL;

namespace zview;

public unsafe class Presentation : IDisposable
{
    public Texture? Texture { get; private set; }
    public bool IsOpen = true;
    public Vector2 Pan;
    public float Zoom = 1;
    public float Rotation;
    public Vector2 Scale = Vector2.One;

    private double time;
    private Vector2 smoothedPan;
    private float smoothedZoom = 1;
    private float smoothedRot;
    private Vector2 smoothedScale = Vector2.One;

    private readonly SDL_Window* window;
    private readonly SDL_Renderer* renderer;
    private readonly SDL_Event[] eventBuffer = new SDL_Event[8];

    private float mouseX, mouseY;
    private readonly MouseBtnState[] mouseBtns = new MouseBtnState[8];
    private readonly SDL_TouchID[] touchDevices;
    private readonly TouchInterpreter touchInterpreter = new();
    private float mouseWheel;
    private Matrix4x4 canvasToScreenMat;

    private SDL_ScaleMode filter;

    private static readonly SDL_FColor white = new SDL_FColor { r = 1, g = 1, b = 1, a = 1 };

    private readonly SDL_Vertex[] verts =
    [
        new SDL_Vertex
        {
            color = white, position = new SDL_FPoint { x = 0, y = 0 },
            tex_coord = new SDL_FPoint { x = 0, y = 0 }
        },
        new SDL_Vertex
        {
            color = white, position = new SDL_FPoint { x = 1, y = 0 },
            tex_coord = new SDL_FPoint { x = 1, y = 0 }
        },
        new SDL_Vertex
        {
            color = white, position = new SDL_FPoint { x = 1, y = 1 },
            tex_coord = new SDL_FPoint { x = 1, y = 1 }
        },
        new SDL_Vertex
        {
            color = white, position = new SDL_FPoint { x = 0, y = 1 },
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
        var flags = SDL_WindowFlags.SDL_WINDOW_VULKAN | SDL_WindowFlags.SDL_WINDOW_RESIZABLE;
        SDL_Window* w;
        SDL_Renderer* r;
        SDL3.SDL_CreateWindowAndRenderer(nameof(zview), 512, 512, flags, &w, &r);
        SDL3.SDL_SetRenderVSync(renderer, SDL3.SDL_WINDOW_SURFACE_VSYNC_ADAPTIVE);

        using var td = SDL3.SDL_GetTouchDevices();
        if (td is not null)
        {
            touchDevices = new SDL_TouchID[td.Count];
            for (var i = 0; i < touchDevices.Length; i++)
                touchDevices[i] = td[i];
        }
        else
            touchDevices = [];

        window = w;
        renderer = r;
    }

    public void SetTexture(Texture texture)
    {
        if (Texture == null)
        {
            var screen = new SDL_Rect();
            SDL3.SDL_GetDisplayBounds(SDL3.SDL_GetDisplayForWindow(window), &screen);
            var sx = int.Min(texture.Width, screen.w / 2);
            var sy = int.Min(texture.Height, screen.h / 2);
            SDL3.SDL_SetWindowSize(window, sx, sy);
            SDL3.SDL_SetWindowPosition(window, (screen.w - sx) / 2, (screen.h - sy) / 2);
        }

        Texture = texture;
        ResetView();
    }

    public void RunLoop()
    {
        var clock = new Stopwatch();
        clock.Start();
        while (IsOpen)
        {
            ProcessEvents();
            ProcessControls();

            SDL3.SDL_RenderClear(renderer);
            Render(clock.Elapsed.TotalSeconds);
            clock.Restart();
            SDL3.SDL_RenderPresent(renderer);
        }
    }

    private void ResetView()
    {
        Pan = default;
        Zoom = 1;
        Rotation = 0;
        Scale = Vector2.One;
        smoothedRot %= float.Tau;
    }

    private void Render(double dt)
    {
        time += dt;
        Matrix4x4.Invert(canvasToScreenMat, out var screenToCanvasMat);

        float x = 0, y = 0;
        var mouseDown = SDL3.SDL_GetMouseState(&x, &y);
        var mouseDelta = new Vector2(x - mouseX, y - mouseY);
        var canvasMouse = Vector2.Transform(new Vector2(x, y), screenToCanvasMat);
        var canvasMouseDelta = canvasMouse - Vector2.Transform(new Vector2(mouseX, mouseY), screenToCanvasMat);

        mouseX = x;
        mouseY = y;

        int w = 0, h = 0;
        SDL3.SDL_GetWindowSize(window, &w, &h);

        if (SDL3.SDL_GetMouseFocus() == window)
        {
            if (mouseDown != 0 && !touchInterpreter.IsBeingUsed)
            {
                if (mouseBtns[1] != MouseBtnState.Pressed)
                {
                    Pan.X -= canvasMouseDelta.X;
                    Pan.Y -= canvasMouseDelta.Y;
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
                    Pan += (canvasMouse - Pan) * s;
                    Zoom -= Zoom * s;
                }
            }

            foreach (var touchDevice in touchDevices)
            {
                using var fingers = SDL3.SDL_GetTouchFingers(touchDevice);
                if (fingers is not null)
                    touchInterpreter.Update(fingers, w, h, ref Pan, ref Zoom, screenToCanvasMat);
            }
        }

        var lerpFactor = 1 - (float)double.Pow(1e-13d, dt);
        smoothedPan = Vector2.Lerp(smoothedPan, Pan, lerpFactor);
        smoothedZoom = float.Lerp(smoothedZoom, Zoom, lerpFactor);
        smoothedRot = float.Lerp(smoothedRot, Rotation, lerpFactor);
        smoothedScale = Vector2.Lerp(smoothedScale, Scale, lerpFactor);
        var s = smoothedZoom * 2f; // i actually sincerely do not know why it has to be 2x
        Matrix4x4.Invert(Matrix4x4.CreateTranslation(new Vector3(smoothedPan, 0)), out var view);
        canvasToScreenMat =
            view *
            Matrix4x4.CreateOrthographic(s, s, 0.01f, 1);

        if (Texture is not null)
        {
            var o = new Vector2(w / 2, h / 2);

            SDL3.SDL_SetTextureScaleMode(Texture.Handle, filter);

            SDL_Vertex[] vertsCopy = [..verts];

            var transform = new Matrix4x4(
                Matrix3x2.CreateScale(Texture.Width, Texture.Height) *
                Matrix3x2.CreateTranslation(o + new Vector2(Texture.Width, Texture.Height) * -0.5f) *
                Matrix3x2.CreateScale(smoothedScale, o) *
                Matrix3x2.CreateRotation(smoothedRot, o)
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
                SDL3.SDL_RenderGeometry(renderer, Texture.Handle, vertices, 4, indices, 6);
            }
            // SDL3.SDL_RenderTexture(renderer, Texture.Handle, &srcRect, &dstRect);
        }
    }

    private void ProcessControls()
    {
        var keyCount = 0;
        var keyStateF = SDL3.SDL_GetKeyboardState(&keyCount);
        var keyState = new Span<SDLBool>(keyStateF, keyCount);

        if (keyState[(int)SDL_Scancode.SDL_SCANCODE_ESCAPE])
            IsOpen = false;

        if (keyState[(int)SDL_Scancode.SDL_SCANCODE_HOME])
            ResetView();
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
                case SDL_EventType.SDL_EVENT_KEY_UP:
                {
                    switch (e.key.scancode)
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
                            var reverse = e.key.mod.HasFlag(SDL_Keymod.SDL_KMOD_LSHIFT);
                            Rotation += 1.5707963268f * (reverse ? -1 : 1);
                            break;
                        }
                        case SDL_Scancode.SDL_SCANCODE_H:
                        {
                            Scale.X *= -1;
                            break;
                        }
                        case SDL_Scancode.SDL_SCANCODE_V:
                        {
                            Scale.Y *= -1;
                            break;
                        }
                    }

                    break;
                }
            }
        }
    }

    public void SetTexture(string path)
    {
        try
        {
            var tex = Texture.Load(renderer, path);
            SetTexture(tex);
        }
        catch (Exception exception)
        {
            Console.WriteLine(exception);
        }
    }

    public void Dispose()
    {
        SDL3.SDL_DestroyRenderer(renderer);
        SDL3.SDL_DestroyWindow(window);
    }
}
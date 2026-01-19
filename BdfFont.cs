using System.Collections;
using System.Globalization;
using SDL;
using SixLabors.ImageSharp.PixelFormats;

namespace zview;

public unsafe class BdfFont
{
    // this is probably the fourth time ive had to write a bdf reader from scratch
    // why? do i keep doing this?
    // its not to spec, btw

    public int FontSize, Ascent, Descent;
    public readonly Dictionary<char, Glyph> Glyphs = [];

    public struct Glyph
    {
        public char Character;
        public byte[] Rows;
        public SDL_Rect Box;
        public int MoveX, MoveY;
    }

    public FontTextureAtlas CreateAtlas(SDL_Renderer* renderer)
    {
        const int w = 256;
        const int h = 128;
        const int padding = 1;

        var surface = SDL3.SDL_CreateSurface(w, h, SDL_PixelFormat.SDL_PIXELFORMAT_RGBA8888);
        var texture = new Texture
        {
            Image = null,
            Height = w,
            Width = h,
            TextureHandle = SDL3.SDL_CreateTexture(renderer, surface->format,
                SDL_TextureAccess.SDL_TEXTUREACCESS_STATIC, w, h),
            SurfaceHandle = surface
        };

        var atlas = new FontTextureAtlas
        {
            Atlas = texture,
            Font = this
        };
        var pixelsOut = (byte*)surface->pixels;

        for (var x = 0; x < w; x++)
        for (var y = 0; y < h; y++)
            SetPixel(x, y, 0x00);

        int cursorX = padding, cursorY = padding;
        int maxHeightThisRow = 0;
        foreach (var glyph in Glyphs.Values)
        {
            maxHeightThisRow = int.Max(glyph.Box.h, maxHeightThisRow);
            cursorX += glyph.Box.w + padding;
            if (cursorX + padding + glyph.Box.w >= w)
            {
                cursorX = padding;
                cursorY += maxHeightThisRow + padding;
                maxHeightThisRow = 0;
            }

            for (var y = 0; y < glyph.Box.h; y++)
            {
                var row = glyph.Rows[y];
                for (var x = 0; x < glyph.Box.w; x++)
                {
                    if ((row & (1 << (glyph.Box.w - x - 1))) != 0)
                        SetPixel(x + cursorX, y + cursorY, 0xFF);
                }
            }

            FragToUv(cursorX, cursorY, out float uvx, out float uvy);
            FragToUv(glyph.Box.w, glyph.Box.h, out float uxw, out float uvh);

            atlas.Entries.Add(glyph.Character, new FontTextureAtlas.Entry
            {
                Glyph = glyph,
                TextureRect = new SDL_Rect
                {
                    x = cursorX,
                    y = cursorY,
                    w = glyph.Box.w,
                    h = glyph.Box.h,
                },
                UvRect = new SDL_FRect
                {
                    x = uvx, y = uvy,
                    w = uxw, h = uvh
                }
            });
        }

        void FragToUv(float x, float y, out float uvx, out float uvy)
        {
            uvx = x / w;
            uvy = y / h;
        }

        void SetPixel(int x, int y, byte value)
        {
            if (x < 0 || x >= w || y < 0 || y >= h)
                throw new IndexOutOfRangeException();

            var i = (y * surface->pitch) + (x * 4);
            pixelsOut[i + 0] = value;
            pixelsOut[i + 1] = value;
            pixelsOut[i + 2] = value;
            pixelsOut[i + 3] = value;
        }

        var rect = new SDL_Rect
        {
            x = 0, y = 0,
            w = w, h = h,
        };

        SDL3.SDL_SetTextureScaleMode(texture.TextureHandle, SDL_ScaleMode.SDL_SCALEMODE_NEAREST);
        SDL3.SDL_UpdateTexture(texture.TextureHandle, &rect, surface->pixels, surface->pitch);

        return atlas;
    }

    public static BdfFont Load(Stream stream)
    {
        using var read = new StreamReader(stream, leaveOpen: false);

        BdfFont font = new();
        Glyph? readingChar = null;
        var ints = new int[8];
        var writingBitmap = false;
        var bitmapRowIndex = 0;

        while (true)
        {
            var l = read.ReadLine();
            if (l == null)
                break;
            var line = l.AsSpan();
            if (line.StartsWith("COMMENT"))
                continue;

            if (readingChar.HasValue)
            {
                var glyph = readingChar.Value;

                if (line.StartsWith("ENDCHAR"))
                {
                    font.Glyphs.Add(glyph.Character, glyph);
                    readingChar = null;
                    continue;
                }

                if (writingBitmap)
                    glyph.Rows![bitmapRowIndex++] = byte.Parse(line, NumberStyles.HexNumber);
                else if (TryRead(line, "ENCODING", ints) == 1)
                    glyph.Character = (char)ints[0];
                else if (TryRead(line, "DWIDTH", ints) == 2) // we do not support SWIDTH
                {
                    glyph.MoveX = ints[0];
                    glyph.MoveY = ints[1];
                }
                else if (TryRead(line, "BBX", ints) == 4)
                {
                    glyph.Box.w = ints[0];
                    glyph.Box.h = ints[1];
                    glyph.Box.x = ints[2];
                    glyph.Box.y = ints[3];
                    glyph.Rows = new byte[glyph.Box.h];
                }
                else if (line.StartsWith("BITMAP"))
                {
                    bitmapRowIndex = 0;
                    writingBitmap = true;
                }

                readingChar = glyph;
            }
            else
            {
                if (TryRead(line, "SIZE", ints) == 3)
                {
                    font.FontSize = ints[0];
                    // dpi ignored because scalable values are also ignored :)
                }
                else if (TryRead(line, "FONT_ASCENT", ints) == 1)
                    font.Ascent = ints[0];
                else if (TryRead(line, "FONT_DESCENT", ints) == 1)
                    font.Descent = ints[0];
                else if (line.StartsWith("STARTCHAR"))
                {
                    writingBitmap = false;
                    readingChar = new();
                }
            }
        }

        return font;

        int TryRead(in ReadOnlySpan<char> line, in ReadOnlySpan<char> key, in int[] values)
        {
            int i = 0;
            if (line.StartsWith(key))
            {
                var parts = line[key.Length..].Trim().ToString().Split(' ');
                foreach (var part in parts)
                    if (int.TryParse(part, out var x))
                        values[i++] = x;
            }

            return i;
        }
    }
}
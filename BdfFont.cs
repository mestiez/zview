using System.Collections;
using System.Globalization;
using SDL3;
using SixLabors.ImageSharp.PixelFormats;

namespace zview;

public unsafe class BdfFont
{
    // this is probably the fourth time ive had to write a bdf reader from scratch
    // why? do i keep doing this?
    // its not to spec, btw

    public int FontSize;
    public readonly Dictionary<char, Glyph> Glyphs = [];

    public struct Glyph
    {
        public char Character;
        public byte[] Rows;
        public SDL.Rect Box;
        public int MoveX, MoveY;
    }

    public FontTextureAtlas CreateAtlas(nint renderer)
    {
        const int w = 256;
        const int h = 128;
        const int padding = 1;

        var surface = (SDL.Surface*)SDL.CreateSurface(w, h, SDL.PixelFormat.RGBA8888);
        var texture = new Texture
        {
            Image = null,
            Height = w,
            Width = h,
            TextureHandle = SDL.CreateTexture(renderer, surface->Format,
                SDL.TextureAccess.Static, w, h),
            SurfaceHandle = surface
        };

        var atlas = new FontTextureAtlas
        {
            Atlas = texture,
            Font = this
        };
        var pixelsOut = (byte*)surface->Pixels;

        for (var x = 0; x < w; x++)
        for (var y = 0; y < h; y++)
            SetPixel(x, y, 0x00);

        int cursorX = padding, cursorY = padding;
        int maxHeightThisRow = 0;
        foreach (var glyph in Glyphs.Values)
        {
            maxHeightThisRow = int.Max(glyph.Box.H, maxHeightThisRow);
            cursorX += glyph.Box.W + padding;
            if (cursorX + padding + glyph.Box.W >= w)
            {
                cursorX = padding;
                cursorY += maxHeightThisRow + padding;
                maxHeightThisRow = 0;
            }

            for (var y = 0; y < glyph.Box.H; y++)
            {
                var row = glyph.Rows[y];
                for (var x = 0; x < glyph.Box.W; x++)
                {
                    if ((row & (1 << (glyph.Box.W - x - 1))) != 0)
                        SetPixel(x + cursorX, y + cursorY, 0xFF);
                }
            }

            atlas.Entries.Add(glyph.Character, new FontTextureAtlas.Entry
            {
                Glyph = glyph,
                TextureRect = new SDL.Rect()
                {
                    X = cursorX,
                    Y = cursorY,
                    W = glyph.Box.W,
                    H = glyph.Box.H,
                },
            });
        }

        void SetPixel(int x, int y, byte value)
        {
            if (x < 0 || x >= w || y < 0 || y >= h)
                throw new IndexOutOfRangeException();

            var i = (y * surface->Pitch) + (x * 4);
            pixelsOut[i + 0] = value;
            pixelsOut[i + 1] = value;
            pixelsOut[i + 2] = value;
            pixelsOut[i + 3] = value;
        }

        var rect = new SDL.Rect
        {
            X = 0, Y = 0,
            W = w, H = h,
        };

        SDL.SetTextureScaleMode(texture.TextureHandle, SDL.ScaleMode.Nearest);
        SDL.UpdateTexture(texture.TextureHandle, rect, surface->Pixels, surface->Pitch);

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
                    glyph.Box.W = ints[0];
                    glyph.Box.H = ints[1];
                    glyph.Box.X = ints[2];
                    glyph.Box.Y = ints[3];
                    glyph.Rows = new byte[glyph.Box.H];
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
                // else if (TryRead(line, "FONT_ASCENT", ints) == 1)
                //     font.Ascent = ints[0];
                // else if (TryRead(line, "FONT_DESCENT", ints) == 1)
                //     font.Descent = ints[0];
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
            if (!line.StartsWith(key)) 
                return 0;
            
            var i = 0;
            var parts = line[key.Length..].Trim().ToString().Split(' ');
            foreach (var part in parts)
                if (int.TryParse(part, out var x))
                    values[i++] = x;

            return i;
        }
    }
}
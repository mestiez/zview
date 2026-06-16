using SDL3;

namespace zview;

public class FontTextureAtlas : IDisposable
{
    public BdfFont? Font;
    public Texture? Atlas;
    public Dictionary<char, Entry> Entries = [];

    public struct Entry
    {
        public BdfFont.Glyph Glyph;
        public SDL.Rect TextureRect;
    }

    public void Dispose()
    {
        Atlas?.Dispose();
    }
}
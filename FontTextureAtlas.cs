using SDL;

namespace zview;

public class FontTextureAtlas : IDisposable
{
    public BdfFont Font;
    public Texture Atlas;
    public Dictionary<char, Entry> Entries = [];

    public struct Entry
    {
        public BdfFont.Glyph Glyph;
        public SDL_FRect UvRect;
        public SDL_Rect TextureRect;
    }

    public void Dispose()
    {
        Atlas?.Dispose();
    }
}
use cgmath::num_traits::zero;
use cgmath::Vector2;
use sdl3::pixels::PixelFormat;
use sdl3::rect::Rect;
use sdl3::render::ScaleMode::Nearest;
use sdl3::render::TextureAccess::Static;
use sdl3::render::{Texture, TextureCreator};
use sdl3::surface::Surface;
use sdl3::video::WindowContext;
use std::collections::HashMap;
use std::io::{BufRead, BufReader, Read};

pub struct BdfFont {
    pub font_size: u32,
    pub glyphs: HashMap<char, Glyph>,
}

#[derive(Clone)]
pub struct Glyph {
    pub character: char,
    pub rows: [u8; 32],
    pub row_count: usize,
    pub bounding_box: Rect,
    pub advance: Vector2<i32>,
}

pub struct FontTextureAtlas<'a> {
    pub font: &'a BdfFont,
    pub texture: Texture<'a>,
    pub entries: HashMap<char, AtlasEntry>,
}

pub struct AtlasEntry {
    pub glyph: Glyph,
    pub tex_rect: Rect,
}

impl Glyph {
    pub fn new() -> Glyph {
        Glyph {
            character: '\0',
            advance: zero(),
            row_count: 0,
            bounding_box: Rect::new(0, 0, 0, 0),
            rows: [0; 32],
        }
    }
}

impl<'a> FontTextureAtlas<'a> {
    pub fn new(
        font: &'a BdfFont,
        tex_creator: &'a TextureCreator<WindowContext>,
    ) -> Result<FontTextureAtlas<'a>, String> {
        const W: u32 = 256;
        const H: u32 = 128;
        const PADDING: u32 = 1;

        let mut surface = Surface::new(W, H, PixelFormat::RGBA8888);
        match surface {
            Ok(mut surface) => {
                let texture = tex_creator.create_texture(surface.pixel_format(), Static, W, H);
                if let Ok(texture) = texture {
                    let mut atlas = FontTextureAtlas {
                        texture,
                        entries: HashMap::new(),
                        font,
                    };
                    surface.with_lock_mut(|buffer| buffer.fill(0));

                    let mut cursor_x = PADDING;
                    let mut cursor_y = PADDING;
                    let mut max_height_this_row = 0_u32;

                    for glyph in font.glyphs.values() {
                        max_height_this_row = max_height_this_row.max(glyph.bounding_box.height());
                        cursor_x += glyph.bounding_box.width() + PADDING;

                        if cursor_x + PADDING + glyph.bounding_box.width() >= W {
                            cursor_x = PADDING;
                            cursor_y += glyph.bounding_box.height() + PADDING;
                            max_height_this_row = 0;
                        }

                        for y in 0..glyph.bounding_box.height() {
                            let row = glyph.rows[y as usize];
                            let b_w = glyph.bounding_box.width();
                            for x in 0..b_w {
                                if (row & (1 << (b_w - x - 1))) != 0 {
                                    Self::set_pixel(&mut surface, x + cursor_x, y + cursor_y, 0xFF);
                                }
                            }
                        }

                        atlas.entries.insert(glyph.character, AtlasEntry {
                            glyph: glyph.to_owned(),
                            tex_rect: Rect::new(
                                cursor_x as i32,
                                cursor_y as i32,
                                glyph.bounding_box.width(),
                                glyph.bounding_box.height()
                            ),
                        });
                    }

                    let pitch = surface.pitch();
                    surface.with_lock_mut(|buffer| {
                        let rect = Rect::new(0,0,W,H);
                       atlas.texture.update(rect, buffer, pitch as usize).ok();
                    });

                    atlas.texture.set_scale_mode(Nearest);

                    return Ok(atlas);
                }

                Err("Failed to create font texture".into())
            }
            Err(e) => Err(format!("Failed to create font texture surface: {e}")),
        }
    }

    fn set_pixel(surface: &mut Surface, x: u32, y: u32, value: u8) {
        let i = (y * surface.pitch() + x * 4) as usize;
        surface.with_lock_mut(|buffer| {
            buffer[i + 0] = value;
            buffer[i + 1] = value;
            buffer[i + 2] = value;
            buffer[i + 3] = value;
        });
    }
}

impl BdfFont {
    pub fn load(r: impl Read) -> BdfFont {
        let mut reader = BufReader::new(r);

        let mut font = BdfFont {
            font_size: 0,
            glyphs: HashMap::new(),
        };

        let mut reading_glyph = Glyph::new();
        let mut has_reading_glyph = false;
        let mut ints = [0i32; 8];
        let mut is_writing_bitmap = false;
        let mut bitmap_row_index = 0usize;

        let mut read_str = String::new();

        while let Ok(c) = reader.read_line(&mut read_str)
            && c > 0
        {
            let s = read_str.to_owned();
            let line = s[0..c].trim();

            read_str.clear();

            if line.starts_with("COMMENT") {
                continue;
            }

            if has_reading_glyph {
                if line.starts_with("ENDCHAR") {
                    font.glyphs
                        .insert(reading_glyph.character, reading_glyph.clone());
                    has_reading_glyph = false;
                    continue;
                }

                if is_writing_bitmap {
                    reading_glyph.rows[bitmap_row_index] = u8::from_str_radix(line, 16).unwrap();
                    bitmap_row_index += 1;
                } else if Self::try_read(line, "ENCODING", &mut ints) == 1 {
                    reading_glyph.character = char::from_u32(ints[0] as u32).unwrap();
                } else if Self::try_read(line, "DWIDTH", &mut ints) == 2 {
                    // we do not support SWIDTH
                    reading_glyph.advance = Vector2::new(ints[0], ints[1]);
                } else if Self::try_read(line, "BBX", &mut ints) == 4 {
                    reading_glyph.bounding_box = Rect::new(
                        ints[2],        // x
                        ints[3],        // y
                        ints[0] as u32, // w
                        ints[1] as u32, // h
                    );
                    reading_glyph.row_count = ints[1] as usize;
                } else if line.starts_with("BITMAP") {
                    bitmap_row_index = 0;
                    is_writing_bitmap = true;
                }
            } else {
                if Self::try_read(line, "SIZE", &mut ints) == 3 {
                    font.font_size = ints[0] as u32;
                } else if line.starts_with("STARTCHAR") {
                    is_writing_bitmap = false;
                    reading_glyph = Glyph::new();
                    has_reading_glyph = true;
                }
            }
        }

        font
    }

    fn try_read(line: &str, key: &str, values: &mut [i32]) -> usize {
        if !line.starts_with(key) {
            return 0;
        }

        let mut i = 0_usize;
        let parts = line[key.len()..].trim().split_whitespace();

        for part in parts {
            if let Ok(num) = part.parse::<i32>() {
                values[i] = num;
                i += 1;
            }
        }

        i
    }
}

use crate::context::Context;
use crate::smoothed::Smoothed;
use cgmath::num_traits::zero;
use cgmath::{Matrix4, SquareMatrix, Vector2, Zero};
use image::codecs::gif::GifDecoder;
use image::codecs::png::PngDecoder;
use image::codecs::webp::WebPDecoder;
use image::{AnimationDecoder, DynamicImage, EncodableLayout, Frames, ImageFormat, ImageReader};
use sdl3::pixels::PixelFormat;
use sdl3::render::{ScaleMode, Texture, TextureCreator};
use sdl3::surface::Surface;
use sdl3::video::WindowContext;
use std::fs;
use std::path::{Path, PathBuf};

pub struct Presentation {
    pub path: Option<PathBuf>,
    pub animated_frame_index: usize,
    pub animated_timer: f32,

    pub prev_mouse: Vector2<f32>,
    pub mouse_down_frames: u32,

    pub pan: Vector2<f32>,
    pub zoom: f32,

    pub autofit: bool,

    pub scale: Smoothed<Vector2<f32>>,
    pub orientation: Smoothed<f32>,
    pub bg: Smoothed<f32>,
    pub filter: ScaleMode,

    pub canvas_to_screen: Matrix4<f32>,
    pub screen_to_canvas: Matrix4<f32>,

    pub sm_canvas_to_screen: Matrix4<f32>,

    dir: Option<PathBuf>,
    paths: Vec<PathBuf>,
}

impl Presentation {
    pub fn new() -> Presentation {
        Presentation {
            animated_frame_index: 0,
            animated_timer: 0.0,
            path: None,
            dir: None,
            paths: Vec::new(),

            prev_mouse: Vector2::zero(),
            mouse_down_frames: 0,

            pan: Vector2::zero(),
            zoom: 1.0,
            scale: Smoothed {
                value: Vector2::new(1.0, 1.0),
                smoothed: Vector2::new(1.0, 1.0),
                coefficient: 1e-8_f32,
            },
            orientation: Smoothed {
                value: 0.0,
                smoothed: 0.0,
                coefficient: 1e-8_f32,
            },
            bg: Smoothed {
                value: 0.0,
                smoothed: 0.0,
                coefficient: 1e-4_f32,
            },
            filter: ScaleMode::Linear,
            autofit: false,

            canvas_to_screen: Matrix4::identity(),
            screen_to_canvas: Matrix4::identity(),

            sm_canvas_to_screen: Matrix4::identity(),
        }
    }

    pub fn update_transforms(&mut self, window_size: Vector2<f32>) {
        self.canvas_to_screen = Matrix4::from_translation(window_size.extend(0.0) * 0.5)
            * Matrix4::from_scale(self.zoom)
            * Matrix4::from_translation(-self.pan.extend(0.0));

        self.screen_to_canvas = self
            .canvas_to_screen
            .invert()
            .unwrap_or(Matrix4::identity());
    }

    pub fn ensure_dir(&mut self) {
        if let Some(path) = &self.path
            && let Some(parent) = path.parent()
        {
            if let Some(dir) = &self.dir
                && dir.eq(parent)
            {
                return;
            }

            // set dir
            self.dir = Some(parent.to_path_buf());

            // populate paths
            let p = fs::read_dir(parent);
            self.paths.clear();
            if let Ok(p) = p {
                for file in p {
                    self.paths.push(PathBuf::from(file.unwrap().path()));
                }
            }
        } else {
            self.dir = None;
            self.paths.clear();
        }
    }

    pub fn cycle_next(&mut self, ctx: &mut Context) {
        self.ensure_dir();
        if self.paths.len() == 0 {
            return;
        }

        if let Some(path) = self.path.clone() {
            let c = self.paths.clone();
            let mut paths = c.iter().cycle();
            let mut acc = false;
            for _ in 0..c.len() {
                let f = paths.next().unwrap();
                acc |= f.eq(&path);
                if acc {
                    let next = paths.next().unwrap();
                    match self.set_texture(next, ctx) {
                        Ok(_) => break,
                        Err(e) => {
                            eprintln!("{e}");
                            // we do nothing and just skip to the next one
                        }
                    }
                }
            }
        }
    }

    // TODO this is the same as cycle_next but with a rev() somewhere
    // so it would be cool if it can be generalised somehow
    pub fn cycle_prev(&mut self, ctx: &mut Context) {
        self.ensure_dir();
        if self.paths.len() == 0 {
            return;
        }

        if let Some(path) = self.path.clone() {
            let c = self.paths.clone();
            let mut paths = c.iter().rev().cycle();
            let mut acc = false;
            for _ in 0..c.len() {
                let f = paths.next().unwrap();
                acc |= f.eq(&path);
                if acc {
                    let next = paths.next().unwrap();
                    match self.set_texture(next, ctx) {
                        Ok(_) => break,
                        Err(e) => {
                            eprintln!("{e}");
                            // we do nothing and just skip to the next one
                        }
                    }
                }
            }
        }
    }

    pub fn copy_image_to_texture<'a>(
        w: u32,
        h: u32,
        rgba32: &[u8],
        tex_creator: &'a TextureCreator<WindowContext>,
    ) -> Result<Texture<'a>, String> {
        if let Ok(surface) = &mut Surface::new(w, h, PixelFormat::RGBA32) {
            surface.with_lock_mut(|buffer| {
                buffer.copy_from_slice(rgba32);
            });

            let tex = surface.as_texture(tex_creator);
            return tex.map_err(|e| e.to_string());
        }

        Err("Failed to create surface".to_string())
    }

    fn direct_set_animated(frames: Frames, ctx: &mut Context) {
        for x in frames {
            if let Ok(frame) = x {
                let delay = frame.delay();
                let buffer = &frame.into_buffer();

                if let Ok(tex) = Self::copy_image_to_texture(
                    buffer.width(),
                    buffer.height(),
                    buffer.as_bytes(),
                    ctx.tex_creator,
                ) {
                    ctx.delays.push(delay);
                    ctx.textures.push(tex);
                }
            }
        }
    }

    fn direct_set_single(img: DynamicImage, ctx: &mut Context) {
        let l = img.into_rgba8();
        let data = l.as_bytes();
        if let Ok(tex) = Self::copy_image_to_texture(l.width(), l.height(), data, ctx.tex_creator) {
            ctx.textures.push(tex);
        }
    }

    pub fn reset_transform(&mut self) {
        self.pan = zero();
        self.zoom = 1.0;

        self.orientation.value = 0.0;
        self.scale.value = Vector2::new(1.0, 1.0);
    }

    pub fn set_texture(&mut self, path: &Path, ctx: &mut Context) -> Result<(), String> {
        self.path = None;
        ctx.textures.clear();
        ctx.delays.clear();

        if let Ok(reader) = ImageReader::open(path) {
            match reader.format() {
                Some(ImageFormat::Gif) => {
                    if let Ok(decoder) = GifDecoder::new(reader.into_inner()) {
                        Self::direct_set_animated(decoder.into_frames(), ctx);
                    } else {
                        return Err("Failed to decode gif".to_string());
                    }
                }
                Some(ImageFormat::WebP) => {
                    if let Ok(decoder) = WebPDecoder::new(reader.into_inner()) {
                        if decoder.has_animation() {
                            Self::direct_set_animated(decoder.into_frames(), ctx);
                        } else if let Ok(decoded) = DynamicImage::from_decoder(decoder) {
                            Self::direct_set_single(decoded, ctx);
                        } else {
                            return Err("Failed to decode static webp".to_string());
                        }
                    } else {
                        return Err("Failed to decode webp".to_string());
                    }
                }
                Some(ImageFormat::Png) => {
                    if let Ok(decoder) = PngDecoder::new(reader.into_inner()) {
                        match decoder.is_apng() {
                            Ok(true) => {
                                if let Ok(apng) = decoder.apng() {
                                    Self::direct_set_animated(apng.into_frames(), ctx);
                                } else {
                                    return Err("Failed to decode animated png".to_string());
                                }
                            }
                            _ => {
                                if let Ok(decoded) = DynamicImage::from_decoder(decoder) {
                                    Self::direct_set_single(decoded, ctx);
                                } else {
                                    return Err("Failed to decode png".to_string());
                                }
                            }
                        }
                    }
                }
                _ => {
                    if let Ok(decoded) = reader.decode() {
                        Self::direct_set_single(decoded, ctx);
                    } else {
                        return Err("Failed to decode image".to_string());
                    }
                }
            }
        } else {
            return Err("Failed to open image".to_string());
        }

        self.animated_frame_index = 0;
        self.path = Some(PathBuf::from(path));

        if let Some(file) = path.file_name()
            && let Some(s) = file.to_str()
        {
            ctx.canvas
                .window_mut()
                .set_title(&format!("{} - {}", env!("CARGO_PKG_NAME"), s))
                .ok();
        } else {
            ctx.canvas
                .window_mut()
                .set_title(&format!("{}", env!("CARGO_PKG_NAME")))
                .ok();
        }

        Ok(())
    }
}

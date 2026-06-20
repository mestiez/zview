use crate::context::Context;
use crate::smooth::Smoothed;
use cgmath::{Matrix4, SquareMatrix, Vector2, Zero};
use image::codecs::gif::GifDecoder;
use image::codecs::png::PngDecoder;
use image::codecs::webp::WebPDecoder;
use image::{AnimationDecoder, DynamicImage, EncodableLayout, Frames, ImageFormat, ImageReader};
use sdl3::pixels::PixelFormat;
use sdl3::render::{Texture, TextureCreator};
use sdl3::surface::Surface;
use sdl3::video::WindowContext;
use std::fs;
use std::path::{Path, PathBuf};

pub struct Presentation {
    pub path: Option<PathBuf>,
    pub time: f32,
    pub frame_index: usize,

    pub prev_mouse: Vector2<f32>,

    pub pan: Vector2<f32>,
    pub zoom: f32,

    pub canvas_to_screen: Matrix4<f32>,
    pub screen_to_canvas: Matrix4<f32>,
    
    pub sm_canvas_to_screen: Matrix4<f32>,

    dir: Option<PathBuf>,
    paths: Vec<PathBuf>,
}

impl Presentation {
    pub fn new() -> Presentation {
        Presentation {
            time: 0.0,
            frame_index: 0,
            path: None,
            dir: None,
            paths: Vec::new(),

            prev_mouse: Vector2::zero(),

            pan: Vector2::zero(),
            zoom: 1.0,

            canvas_to_screen: Matrix4::identity(),
            screen_to_canvas: Matrix4::identity(),
            
            sm_canvas_to_screen: Matrix4::identity(),
        }
    }

    pub fn update_transforms(&mut self, window_size : Vector2<f32>) {
        self.canvas_to_screen =
            Matrix4::from_translation(window_size.extend(0.0) * 0.5) *
                Matrix4::from_scale(self.zoom) *
                Matrix4::from_translation(-self.pan.extend(0.0));
        
        self.screen_to_canvas = self.canvas_to_screen.invert().unwrap_or(Matrix4::identity());
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
            for _ in 0..c.len() {
                let f = paths.next().unwrap();
                if f.eq(&path) {
                    self.set_texture(paths.next().unwrap(), ctx)
                        .expect("Failed to set texture");
                }
            }
        }
    }

    pub fn cycle_prev(&mut self, ctx: &mut Context) {
        self.ensure_dir();
        if self.paths.len() == 0 {
            return;
        }

        if let Some(path) = self.path.clone() {
            let c = self.paths.clone();
            let mut paths = c.iter().rev().cycle();
            for _ in 0..c.len() {
                let f = paths.next().unwrap();
                if f.eq(&path) {
                    self.set_texture(paths.next().unwrap(), ctx)
                        .expect("Failed to set texture");
                }
            }
        }
    }

    fn copy_image_to_texture<'a>(
        w: u32,
        h: u32,
        src: &[u8],
        tex_creator: &'a TextureCreator<WindowContext>,
    ) -> Result<Texture<'a>, String> {
        let mut surface =
            Surface::new(w, h, PixelFormat::RGBA32).expect("Failed to create surface");

        surface.with_lock_mut(|buffer| {
            buffer.copy_from_slice(src);
        });

        let tex = surface
            .as_texture(tex_creator)
            .expect("Failed to convert surface");

        Ok(tex)
    }

    fn set_animated(frames: Frames, ctx: &mut Context) {
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

    fn set_single(img: DynamicImage, ctx: &mut Context) {
        let l = img.into_rgba8();
        let data = l.as_bytes();
        let tex = Self::copy_image_to_texture(l.width(), l.height(), data, ctx.tex_creator)
            .expect("Failed to copy image to texture");
        ctx.textures.push(tex);
    }

    pub fn set_texture(&mut self, path: &Path, ctx: &mut Context) -> Result<(), String> {
        ctx.delays.clear();
        ctx.textures.clear();

        let reader = ImageReader::open(path).expect("Can't open image");

        match reader.format() {
            Some(ImageFormat::Gif) => {
                let decoder = GifDecoder::new(reader.into_inner()).expect("Failed to decode gif");
                Self::set_animated(decoder.into_frames(), ctx);
            }
            Some(ImageFormat::WebP) => {
                let decoder = WebPDecoder::new(reader.into_inner()).expect("Failed to decode webp");
                Self::set_animated(decoder.into_frames(), ctx);
            }
            Some(ImageFormat::Png) => {
                let decoder = PngDecoder::new(reader.into_inner()).expect("Failed to decode webp");
                match decoder.is_apng() {
                    Ok(true) => {
                        let apng = decoder.apng().expect("Failed to decode apng");
                        Self::set_animated(apng.into_frames(), ctx);
                    }
                    _ => {
                        let decoded =
                            DynamicImage::from_decoder(decoder).expect("Failed to decode image");
                        Self::set_single(decoded, ctx);
                    }
                }
            }
            _ => {
                let decoded = reader.decode().expect("Can't decode image");
                Self::set_single(decoded, ctx);
            }
        }

        self.time = 0.0;
        self.frame_index = 0;
        self.path = Some(PathBuf::from(path));

        if let Some(file) = path.file_name()
            && let Some(s) = file.to_str()
        {
            ctx.canvas
                .window_mut()
                .set_title(&format!("{} - {}", env!("CARGO_PKG_NAME"), s))
                .ok();
        }

        Ok(())
    }
}

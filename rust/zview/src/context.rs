use crate::touch::TouchState;
use image::{Delay, Frame};
use sdl3::render::{Texture, TextureCreator, WindowCanvas};
use sdl3::video::{Window, WindowContext};
use sdl3::{Sdl, VideoSubsystem};

pub struct Context<'a> {
    pub sdl: &'a Sdl,
    pub video: &'a VideoSubsystem,
    pub canvas: &'a mut WindowCanvas,
    pub tex_creator: &'a TextureCreator<WindowContext>,

    pub textures: Vec<Texture<'a>>,
    pub delays: Vec<Delay>,

    pub touch: TouchState,
}

impl Context<'_> {
    pub fn get_window_mut(&mut self) -> &mut Window {
        self.canvas.window_mut()
    }

    pub fn get_window(&self) -> &Window {
        self.canvas.window()
    }
}

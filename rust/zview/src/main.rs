mod context;
mod presentation;
mod smoothed;
mod text;
mod touch;
mod viewer;

use crate::context::Context;
use crate::presentation::Presentation;
use crate::text::{BdfFont, FontTextureAtlas};
use crate::touch::TouchState;
use crate::viewer::run;
use std::env;
use std::path::Path;

/* Things I would like to implement:
   - Fine touchscreen rotation
   - HDR support (tonemapping selector)
   - Sprite sheet support
   - Auto-play sequence (adjustable fps)
*/

fn main() {
    let args: Vec<String> = env::args().collect();

    for arg in &args {
        if arg.starts_with('-') {
            match arg.as_ref() {
                "-v" | "--version" => println!("v{}", env!("CARGO_PKG_VERSION")),
                "-h" | "--help" => {
                    println!(
                        "zview - image viewer for x11

Usage:
    zview [options] [path]

Arguments:
    path              Path to the image or directory (optional).

Options:
    -v, --version     Display version information.
    -h, --help        Show help and usage information.
"
                    );
                }
                _ => {
                    eprintln!("Unrecognized flag '{}'", arg);
                    eprintln!("Use --help or -h for more information");
                }
            }
            return;
        }
    }

    let sdl = sdl3::init().expect("failed to init SDL");
    let video = sdl.video().expect("failed to get video context");
    let window = video
        .window(env!("CARGO_PKG_NAME"), 512, 512)
        .resizable()
        .build()
        .expect("failed to create window");

    let mut canvas = window.into_canvas();
    let tc = canvas.texture_creator();

    let mut ctx = Context {
        sdl: &sdl,
        canvas: &mut canvas,
        video: &video,
        tex_creator: &tc,
        textures: Vec::new(),
        delays: Vec::new(),
        touch: TouchState::new(),
    };


    let font = BdfFont::load(
        &include_bytes!("./assets/haxor-12.bdf")[..],
        // will shit the bed if the font is invalid or whatever
    );

    run(
        if args.len() > 1 {
            Some(Path::new(&args[1]))
        } else {
            None
        },
        &mut Presentation::new(FontTextureAtlas::new(&font, ctx.tex_creator).unwrap()),
        &mut ctx,
    );
}

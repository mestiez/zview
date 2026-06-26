use crate::context::Context;
use crate::presentation::Presentation;
use crate::text::FontTextureAtlas;
use cgmath::num_traits::{FloatConst, zero};
use cgmath::{EuclideanSpace, Matrix4, Point3, Rad, Transform, Vector2, VectorSpace};
use hjkl_clipboard::{Clipboard, MimeType, Selection};
use image::{DynamicImage, EncodableLayout, ImageFormat, ImageReader};
use sdl3::event::{Event, WindowEvent};
use sdl3::keyboard::{Keycode, Mod, Scancode};
use sdl3::pixels::{Color, FColor};
use sdl3::rect::Rect;
use sdl3::render::{
    BlendMode, FPoint, FRect, ScaleMode, Texture, Vertex, VertexIndices, WindowCanvas,
};
use sdl3::sys::stdinc::SDL_free;
use sdl3::sys::touch::{SDL_GetTouchFingers, SDL_TouchID};
use sdl3::touch::Finger;
use std::ffi::{CStr, CString};
use std::io::Cursor;
use std::path::Path;
use std::time::{Duration, Instant};
use std::{fs, slice};


pub fn run(path: Option<&Path>, state: &mut Presentation, ctx: &mut Context) {
    if let Some(path) = &path {
        match state.set_texture(path, ctx) {
            Ok(_) => fit_window(state, ctx),
            Err(e) => panic!("{}", e),
        };
    }

    let verts = [
        Vertex {
            color: FColor::WHITE,
            position: FPoint::new(0.0, 0.0),
            tex_coord: FPoint::new(0.0, 0.0),
        },
        Vertex {
            color: FColor::WHITE,
            position: FPoint::new(1.0, 0.0),
            tex_coord: FPoint::new(1.0, 0.0),
        },
        Vertex {
            color: FColor::WHITE,
            position: FPoint::new(1.0, 1.0),
            tex_coord: FPoint::new(1.0, 1.0),
        },
        Vertex {
            color: FColor::WHITE,
            position: FPoint::new(0.0, 1.0),
            tex_coord: FPoint::new(0.0, 1.0),
        },
    ];
    let mut verts_transformed = verts.clone();

    let idx = VertexIndices::U8(&[0, 1, 3, 3, 2, 1]);
    let mut should_update_instantly = true;

    ctx.canvas.set_draw_color(Color::BLACK);
    ctx.canvas.set_blend_mode(BlendMode::Blend);
    ctx.canvas.clear();
    ctx.canvas.present();
    let mut event_pump = ctx.sdl.event_pump().unwrap();

    let mut frame_clock = Instant::now();

    let mut dt: Duration;
    let mut dt_secs = 0.0_f32;

    'running: loop {
        ctx.canvas.set_draw_color({
            let x = (state.bg.smoothed * state.bg.smoothed * u8::MAX as f32) as u8;
            Color::RGB(x, x, x)
        });
        ctx.canvas.clear();

        let mut mouse_wheel = 0.0f64;
        let show_info = event_pump.keyboard_state().is_scancode_pressed(Scancode::I);

        for event in event_pump.poll_iter() {
            match event {
                Event::Quit { .. }
                | Event::KeyDown {
                    keycode: Some(Keycode::Q),
                    ..
                }
                | Event::KeyDown {
                    keycode: Some(Keycode::Escape),
                    ..
                } => break 'running,

                Event::MouseWheel { y, .. } => {
                    mouse_wheel = -y as f64;
                }

                Event::Window { win_event, .. } => {
                    if let WindowEvent::Resized(_, _) = win_event {
                        should_update_instantly = true;
                    }
                }

                // keybinds
                Event::KeyDown {
                    keycode, keymod, ..
                } => match (keycode, keymod) {
                    (Some(Keycode::Left), Mod::NOMOD) => {
                        state.cycle_prev(ctx);
                        state.reset_transform();
                        should_update_instantly = true;
                    }
                    (Some(Keycode::Right), Mod::NOMOD) => {
                        state.cycle_next(ctx);
                        state.reset_transform();
                        should_update_instantly = true;
                    }
                    (Some(Keycode::B), Mod::NOMOD) => {
                        state.bg.value = if state.bg.value > 0.5 { 0.0 } else { 1.0 }
                    }
                    (Some(Keycode::F), Mod::NOMOD) => {
                        state.filter = match state.filter {
                            ScaleMode::Linear => ScaleMode::Nearest,
                            _ => ScaleMode::Linear,
                        }
                    }
                    (Some(Keycode::Home), Mod::NOMOD) | (Some(Keycode::Backspace), Mod::NOMOD) => {
                        state.autofit = false;
                        state.reset_transform();
                    }
                    (Some(Keycode::F5), Mod::NOMOD) => {
                        let p = state.path.clone();
                        if let Some(path) = &p {
                            state.bg.smoothed = 0.5;
                            state.set_texture(path.as_path(), ctx).ok();
                        }
                    }
                    (Some(Keycode::Period), Mod::NOMOD) => {
                        state.autofit = !state.autofit;
                    }
                    (Some(Keycode::W), Mod::NOMOD) => {
                        fit_window(state, ctx);
                    }

                    // flipping heck
                    (Some(Keycode::H), Mod::NOMOD) => {
                        state.scale.value.x *= -1.0;
                    }
                    (Some(Keycode::V), Mod::NOMOD) => {
                        state.scale.value.y *= -1.0;
                    }

                    // tiling
                    (Some(Keycode::T), Mod::LSHIFTMOD) => {
                        state.tiling.value = (state.tiling.value - 2.0).max(1.0);
                    }
                    (Some(Keycode::T), Mod::NOMOD) => {
                        state.tiling.value = (state.tiling.value + 2.0).min(17.0);
                    }

                    // rotate
                    (Some(Keycode::R), Mod::LSHIFTMOD | Mod::RSHIFTMOD | Mod::NOMOD) => {
                        let half_pi = f32::FRAC_PI_2();

                        state.orientation.value += if keymod.contains(Mod::LSHIFTMOD) {
                            -half_pi
                        } else {
                            half_pi
                        };

                        // snap
                        state.orientation.value =
                            (state.orientation.value / half_pi).round() * half_pi
                    }

                    // clipboard shit
                    (Some(Keycode::V), Mod::LCTRLMOD) => {
                        // paste
                        let clipboard = ctx.video.clipboard();
                        if paste_from_clipboard(state, ctx).is_err() {
                            if let Ok(text) = clipboard.clipboard_text() {
                                if fs::exists(&text).unwrap_or(false) {
                                    state.set_texture(Path::new(&text), ctx).ok();
                                    state.reset_transform();
                                    state.tiling.value = 0.0;
                                }
                            }
                        }
                    }
                    (Some(Keycode::C), Mod::LCTRLMOD) => {
                        // copy
                        copy_to_clipboard(state);
                    }
                    _ => {}
                },

                // on drop
                Event::DropFile { filename, .. } => {
                    println!("File: {}", filename);
                    match state.set_texture(filename.as_ref(), ctx) {
                        Ok(_) => {
                            state.reset_transform();
                            state.autofit = true;
                            should_update_instantly = true;
                        }
                        Err(e) => eprintln!("{}", e),
                    }
                }
                _ => {}
            }
        }

        let window_size = {
            let a = ctx.get_window().size_in_pixels();
            Vector2::new(a.0 as f32, a.1 as f32)
        };

        if ctx.get_window().has_mouse_focus() {
            state.update_transforms(window_size);

            let mouse = event_pump.mouse_state();
            let m = Point3::new(mouse.x(), mouse.y(), 0.0);
            let delta = m.xy() - state.prev_mouse;
            state.prev_mouse = m.xy().to_vec();

            if mouse.left() && !ctx.touch.is_being_used() {
                state.mouse_down_frames += 1;

                if state.mouse_down_frames > 1 {
                    state.pan -= delta.xy().to_vec() / state.zoom;
                }

                state.autofit = false;
            } else {
                state.mouse_down_frames = 0;
            }

            if mouse_wheel.abs() > 0.001 {
                let cursor_before = state.screen_to_canvas.transform_point(m).xy().to_vec();

                state.zoom *= if mouse_wheel < 0.0 { 1.1 } else { 0.9 };

                state.update_transforms(window_size);
                let cursor_after = state.screen_to_canvas.transform_point(m).xy().to_vec();
                let d = cursor_before - cursor_after;
                state.pan += d;

                state.autofit = false;
            }

            state.update_transforms(window_size);

            for touch_id in sdl3::touch::num_touch_devices() {
                unsafe {
                    let mut finger_count = 0i32;
                    let ptr = SDL_GetTouchFingers(SDL_TouchID::from(touch_id), &mut finger_count);

                    let fingers: Vec<Finger> = {
                        let v = slice::from_raw_parts(ptr, finger_count as usize).to_vec();
                        v.iter()
                            .map(|f| {
                                let ff = **f;
                                Finger {
                                    x: ff.x as f32 * window_size.x,
                                    y: ff.y as f32 * window_size.y,
                                    id: ff.id,
                                    pressure: ff.pressure as f32,
                                }
                            })
                            .collect()
                    };
                    SDL_free(ptr as _);

                    if ctx.touch.update(fingers, state, window_size) {
                        state.autofit = false;
                        break;
                    }
                }
            }
        }

        let mut tex: Option<&mut Texture> = None;
        let texture_count = ctx.textures.len();

        if texture_count == 1 {
            tex = Some(&mut ctx.textures[0]);
        } else if ctx.textures.len() > 1 {
            tex = Some(&mut ctx.textures[state.animated_frame_index % texture_count]);
            let delay = ctx.delays[state.animated_frame_index % texture_count].numer_denom_ms();
            let s = (delay.0 as f32 / delay.1 as f32) / 1000.0;

            while state.animated_timer > s {
                state.animated_frame_index += 1;
                state.animated_timer -= s;
            }

            state.animated_timer += dt_secs;
        }

        if let Some(tex) = tex {
            let tex_w = tex.width() as f32;
            let tex_h = tex.height() as f32;
            let tex_size = Vector2::new(tex_w, tex_h);

            // autofit logic goes here
            if state.autofit {
                let transformed_size = Matrix4::from_angle_z(Rad(state.orientation.value))
                    .transform_vector(tex_size.extend(0.0))
                    .xy()
                    .map(|x| x.abs());

                let aspect_ratio = transformed_size.y / transformed_size.x;

                if window_size.y / window_size.x < aspect_ratio {
                    state.zoom = window_size.y / transformed_size.y;
                } else {
                    state.zoom = window_size.x / transformed_size.x;
                }

                state.pan = zero();
            }

            state.update_transforms(window_size);

            if should_update_instantly {
                should_update_instantly = false;
                state.scale.smoothed = state.scale.value;
                state.orientation.smoothed = state.orientation.value;
                state.tiling.smoothed = state.tiling.value;
                state.sm_canvas_to_screen = state.canvas_to_screen;
            } else {
                state.scale.update(dt_secs);
                state.tiling.update(dt_secs);
                state.orientation.update(dt_secs);

                state.sm_canvas_to_screen = {
                    const COEFFICIENT: f32 = 1e-13;
                    let f = 1.0 - COEFFICIENT.powf(dt_secs);
                    state.sm_canvas_to_screen.lerp(state.canvas_to_screen, f)
                };
            }
            state.bg.update(dt_secs);

            ctx.canvas.set_draw_color(Color::WHITE);

            // final transformation
            let transform = state.sm_canvas_to_screen
                * Matrix4::from_angle_z(Rad(state.orientation.smoothed))
                * Matrix4::from_nonuniform_scale(
                    state.scale.smoothed.x,
                    state.scale.smoothed.y,
                    1.0,
                )
                * Matrix4::from_translation((tex_size * -0.5).extend(0.0))
                * Matrix4::from_nonuniform_scale(tex_w, tex_h, 1.0);

            let tiling = state.tiling.smoothed;
            for i in 0..verts.len() {
                let v = &mut verts_transformed[i];
                verts[i].clone_into(v);

                let p = transform.transform_point(Point3::new(v.position.x, v.position.y, 0.0));

                v.position.x = p.x;
                v.position.y = p.y;

                v.tex_coord.x *= tiling;
                v.tex_coord.y *= tiling;

                v.tex_coord.x -= (tiling - 1.0) * 0.5;
                v.tex_coord.y -= (tiling - 1.0) * 0.5;
            }

            tex.set_scale_mode(state.filter);
            ctx.canvas
                .render_geometry(&verts_transformed, Some(tex), idx)
                .ok();

            if show_info && !state.info.is_empty() {
                ctx.canvas.set_draw_color(Color::RGBA(0, 0, 0, 200));
                ctx.canvas
                    .fill_rect(Rect::new(0, 0, window_size.x as u32, window_size.y as u32))
                    .ok();

                draw_text(ctx.canvas, &state.font, state.info.as_str(), 16, 16);
            }
        }

        ctx.canvas.present();

        dt = frame_clock.elapsed();
        dt_secs = dt.as_secs_f32();
        frame_clock = Instant::now();

        let target_frame_time_secs = {
            const FALLBACK: f32 = 1.0 / 300.0;
            if let Ok(display) = ctx.get_window().get_display()
                && let Ok(mode) = display.get_mode()
            {
                if mode.refresh_rate_numerator > 0 {
                    mode.refresh_rate_denominator as f32 / mode.refresh_rate_numerator as f32
                } else {
                    // reported mode has invalid refresh rate...
                    FALLBACK
                }
            } else {
                // no display found (???)
                FALLBACK
            }
        };
        let r = target_frame_time_secs - frame_clock.elapsed().as_secs_f32();
        if r > 0.0 {
            std::thread::sleep(Duration::from_secs_f32(r));
        }
    }
}

fn copy_to_clipboard(state: &mut Presentation) {
    if let Ok(clipboard) = hjkl_clipboard::Clipboard::new()
        && let Some(path) = &state.path
    {
        let mut result = false;

        // text
        // result |= clipboard
        //     .set(
        //         Selection::Clipboard,
        //         MimeType::Text,
        //         path.to_str().unwrap().as_bytes(),
        //     )
        //     .is_ok();
        //
        // result |= clipboard
        //     .set_uri_list(Selection::Clipboard, &[Uri::File(path.to_owned())])
        //     .is_ok();

        if let Ok(dec) = ImageReader::open(path)
            && let Ok(img) = dec.decode()
        {
            result |= write_to_clipboard(&clipboard, ImageFormat::Png, &img);
        }

        if result {
            state.bg.smoothed = 0.5;
        }
    }
}

fn draw_text(canvas: &mut WindowCanvas, atlas: &FontTextureAtlas, text: &str, x: i32, y: i32) {
    let mut cursor = Vector2::new(x, y);

    for char in text.chars() {
        if char == '\n' {
            cursor.x = x;
            cursor.y += atlas.font.font_size as i32 + 5;
        } else if !char.is_control()
            && let Some(entry) = atlas.entries.get(&char)
        {
            let src = FRect::new(
                entry.tex_rect.x as f32,
                entry.tex_rect.y as f32,
                entry.tex_rect.w as f32,
                entry.tex_rect.h as f32,
            );

            let dst = FRect::new(
                (cursor.x + entry.glyph.bounding_box.x) as f32,
                (cursor.y + entry.glyph.bounding_box.y) as f32,
                entry.glyph.bounding_box.w as f32,
                entry.glyph.bounding_box.h as f32,
            );

            canvas.copy(&atlas.texture, src, dst).ok();
            cursor += entry.glyph.advance;
        }
    }
}

/* TODO
All this clipboard shit is fucked. All we need really is x11rb and provide the data manually.
What we have now does not work reliably and comes with a load of bs that we don't need anyway.
 */

fn write_to_clipboard(clipboard: &Clipboard, format: ImageFormat, img: &DynamicImage) -> bool {
    let mut dst = Vec::new();
    match img.write_to(&mut Cursor::new(&mut dst), format) {
        Ok(_) => {
            let mime = format.to_mime_type().into();
            match clipboard.set(Selection::Clipboard, MimeType::Custom(mime), &dst[..]) {
                Ok(_) => true,
                Err(e) => {
                    println!(
                        "Could not write to clipboard ({}): {}",
                        format.to_mime_type(),
                        e
                    );
                    false
                }
            }
        }
        Err(e) => {
            println!(
                "Could not encode image for clipboard ({}): {}",
                format.to_mime_type(),
                e
            );
            false
        }
    }
}

fn paste_from_clipboard(state: &mut Presentation, ctx: &mut Context) -> Result<(), String> {
    unsafe {
        let mut size = 0_usize;
        let mimes_ptr = sdl3::sys::clipboard::SDL_GetClipboardMimeTypes(&mut size);
        if mimes_ptr.is_null() {
            return Err("Could not get clipboard mime types".into());
        }

        let formats = slice::from_raw_parts(mimes_ptr, size)
            .iter()
            .filter_map(|mime| ImageFormat::from_mime_type(CStr::from_ptr(*mime).to_str().unwrap()))
            .collect::<Vec<_>>();
        SDL_free(mimes_ptr as _);

        let mut result: Result<(), String> = Err("Failed to load image from clipboard".into());
        for format in formats {
            let mime = format.to_mime_type();
            let mime_cstr = CString::new(mime.to_string()).unwrap();
            let data = sdl3::sys::clipboard::SDL_GetClipboardData(mime_cstr.as_ptr(), &mut size);
            if !data.is_null() && size > 0 {
                let buffer = slice::from_raw_parts(data as *mut u8, size);
                result = match image::load_from_memory_with_format(&buffer, format) {
                    Ok(img) => {
                        let rgba = img.into_rgba8();

                        Presentation::copy_image_to_texture(
                            rgba.width(),
                            rgba.height(),
                            rgba.as_bytes(),
                            ctx.tex_creator,
                        )
                        .and_then(|tex| {
                            ctx.textures.clear();
                            ctx.textures.push(tex);
                            state.path = None;
                            Ok(())
                        })
                    }
                    _ => Err("Failed to load image".into()),
                };
            }
            SDL_free(data);
            if result.is_ok() {
                break;
            }
        }

        result
    }
}

fn fit_window(state: &mut Presentation, ctx: &mut Context){
    let m_w = ctx.textures.iter().map(|t| t.width()).max();
    let m_h = ctx.textures.iter().map(|t| t.height()).max();
    if let (Some(w), Some(h)) = (m_w, m_h)
        && ctx.get_window_mut().set_size(w, h).is_ok()
    {
        ctx.get_window_mut().restore();
        state.reset_transform();
        state.autofit = false;
    }
}
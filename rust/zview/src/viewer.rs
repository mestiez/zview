use crate::context::Context;
use crate::presentation::Presentation;
use cgmath::num_traits::FloatConst;
use cgmath::num_traits::real::Real;
use cgmath::{EuclideanSpace, Matrix4, Point3, Rad, Transform, Vector2, VectorSpace};
use sdl3::event::{Event, WindowEvent};
use sdl3::keyboard::{Keycode, Mod};
use sdl3::pixels::{Color, FColor};
use sdl3::render::{FPoint, Texture, Vertex, VertexIndices};
use sdl3::sys::stdinc::SDL_free;
use sdl3::sys::touch::{SDL_GetTouchFingers, SDL_TouchID};
use sdl3::touch::Finger;
use std::path::Path;
use std::slice;
use std::time::Duration;

pub fn run(path: Option<&Path>, state: &mut Presentation, ctx: &mut Context) {
    let dt = Duration::from_secs_f32(1f32 / 120f32);
    let dt_secs = dt.as_secs_f32();

    if let Some(path) = &path {
        match state.set_texture(path, ctx) {
            Ok(_) => (),
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
    ctx.canvas.clear();
    ctx.canvas.present();
    let mut event_pump = ctx.sdl.event_pump().unwrap();

    'running: loop {
        ctx.canvas.set_draw_color(Color::BLACK);
        ctx.canvas.clear();

        let mut mouse_wheel = 0.0f64;

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
                } => match keycode {
                    Some(Keycode::Left) => {
                        state.cycle_prev(ctx);
                    }
                    Some(Keycode::Right) => {
                        state.cycle_next(ctx);
                    }

                    // flipping heck
                    Some(Keycode::H) => {
                        state.scale.value.x *= -1.0;
                    }
                    Some(Keycode::V) => {
                        state.scale.value.y *= -1.0;
                    }

                    // rotate
                    Some(Keycode::R) => {
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
                    _ => {}
                },

                // on drop
                Event::DropFile { filename, .. } => {
                    println!("File: {}", filename);
                    match state.set_texture(filename.as_ref(), ctx) {
                        Ok(_) => (),
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
            }

            state.update_transforms(window_size);
            for touch_id in sdl3::touch::num_touch_devices() {
                unsafe {
                    let mut finger_count = 0;
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
                        should_update_instantly = true;
                        break;
                    }
                }
            }
        }

        state.update_transforms(window_size);

        let mut tex: Option<&Texture> = None;

        if ctx.textures.len() == 1 {
            tex = Some(&ctx.textures[0]);
        } else if ctx.textures.len() > 1 {
            let delay = ctx.delays[state.frame_index % ctx.delays.len()].numer_denom_ms();
            let s = 0.001 * (delay.0 as f32 / delay.1 as f32);
            if state.time > s {
                state.time = 0.0;
                state.frame_index += 1;
            }

            tex = Some(&ctx.textures[state.frame_index % ctx.textures.len()]);
        }

        if let Some(tex) = tex {
            let tex_w = tex.width() as f32;
            let tex_h = tex.height() as f32;
            let tex_size = Vector2::new(tex_w, tex_h);

            ctx.canvas.set_draw_color(Color::WHITE);

            // final transformation
            let transform = state.sm_canvas_to_screen
                * Matrix4::from_nonuniform_scale(
                    state.scale.smoothed.x,
                    state.scale.smoothed.y,
                    1.0,
                )
                * Matrix4::from_angle_z(Rad(state.orientation.smoothed))
                * Matrix4::from_translation((tex_size * -0.5).extend(0.0))
                * Matrix4::from_nonuniform_scale(tex_w, tex_h, 1.0);

            for i in 0..verts.len() {
                let v = &mut verts_transformed[i];
                verts[i].clone_into(v);

                let p = transform.transform_point(Point3::new(v.position.x, v.position.y, 0.0));

                v.position.x = p.x;
                v.position.y = p.y;
            }

            ctx.canvas
                .render_geometry(&verts_transformed, Some(tex), idx)
                .ok();
        }

        ctx.canvas.present();

        state.scale.update(dt_secs);
        state.orientation.update(dt_secs);

        state.sm_canvas_to_screen = if should_update_instantly {
            should_update_instantly = false;
            state.canvas_to_screen
        } else {
            const COEFFICIENT: f32 = 1e-13;
            let f = 1.0 - COEFFICIENT.powf(dt_secs);
            state.sm_canvas_to_screen.lerp(state.canvas_to_screen, f)
        };

        state.time += dt_secs;
        std::thread::sleep(dt);
    }
}

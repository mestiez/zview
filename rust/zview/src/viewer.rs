use crate::context::Context;
use crate::presentation::Presentation;
use cgmath::{ortho, EuclideanSpace, Matrix4, Point2, Point3, SquareMatrix, Transform, Vector2, Vector3, VectorSpace};
use sdl3::event::Event;
use sdl3::keyboard::Keycode;
use sdl3::pixels::{Color, FColor};
use sdl3::render::{FPoint, Texture, Vertex, VertexIndices};
use std::path::Path;
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

                // keybinds
                Event::KeyDown { keycode, .. } => match keycode {
                    Some(Keycode::Left) => {
                        state.cycle_prev(ctx);
                    }
                    Some(Keycode::Right) => {
                        state.cycle_next(ctx);
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

            if mouse.left()
            {
                state.pan -= delta.xy().to_vec() / state.zoom;
            }

            if mouse_wheel.abs() > 0.001
            {
                let cursor_before = state.screen_to_canvas.transform_point(m).xy().to_vec();

                state.zoom *= if mouse_wheel < 0.0 {
                    1.1
                } else {
                    0.9
                };

                state.update_transforms(window_size);
                let cursor_after = state.screen_to_canvas.transform_point(m).xy().to_vec();
                let d = cursor_before - cursor_after;
                state.pan += d;
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
        state.sm_canvas_to_screen = {
            const COEFFICIENT : f32 = 1e-8;
            let f = 1.0 - COEFFICIENT.powf(dt_secs);
            // self.smoothed = self.smoothed + (self.value - self.smoothed) * f;
            state.sm_canvas_to_screen.lerp(state.canvas_to_screen, f)
        };

        state.time += dt_secs;
        std::thread::sleep(dt);
    }
}

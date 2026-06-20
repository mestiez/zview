use crate::presentation::Presentation;
use cgmath::{EuclideanSpace, MetricSpace, Point3, Transform, Vector2, Zero};
use sdl3::touch::Finger;

pub struct TouchState {
    is_pinch_zooming: bool,
    initial_distance: f32,
    initial_zoom: f32,

    initial_positions: [Vector2<f32>; 2],

    frame_count: u32,
}

impl TouchState {
    pub fn new() -> Self {
        TouchState {
            initial_zoom: 1.0,
            initial_positions: [Vector2::zero(), Vector2::zero()],
            initial_distance: 0.0,
            is_pinch_zooming: false,
            frame_count: 0,
        }
    }

    pub fn is_being_used(&self) -> bool {
        self.is_pinch_zooming
    }

    pub fn update(&mut self, fingers: Vec<Finger>, state: &mut Presentation) -> bool {
        // figure out pan, rotation, and zoom
        if fingers.len() < 2 {
            self.is_pinch_zooming = false;
            return false;
        }

        let f1 = state
            .screen_to_canvas
            .transform_point(Point3::new(fingers[0].x, fingers[0].y, 0.0))
            .to_vec()
            .xy();
        let f2 = state
            .screen_to_canvas
            .transform_point(Point3::new(fingers[1].x, fingers[1].y, 0.0))
            .to_vec()
            .xy();
        let midpoint = (f1 + f2) * 0.5;

        if self.is_pinch_zooming {
            if self.frame_count > 2 {
                // todo this could be cached lol
                let prev_midpoint = (self.initial_positions[0] + self.initial_positions[1]) * 0.5;
                let delta = midpoint - prev_midpoint;
                state.pan -= delta;

                let dst = Vector2::distance(
                    Vector2::new(fingers[0].x, fingers[0].y),
                    Vector2::new(fingers[1].x, fingers[1].y),
                );

                let ratio = dst / self.initial_distance;

                state.zoom = self.initial_zoom * ratio;

                return true;
            } else {
                self.frame_count += 1;
            }
        } else {
            self.is_pinch_zooming = true;

            self.initial_positions[0] = f1;
            self.initial_positions[1] = f2;

            self.initial_distance = Vector2::distance(
                Vector2::new(fingers[0].x, fingers[0].y),
                Vector2::new(fingers[1].x, fingers[1].y),
            );

            self.frame_count = 0;
            self.initial_zoom = state.zoom;
        }

        false
    }
}

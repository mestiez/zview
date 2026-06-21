use crate::presentation::Presentation;
use cgmath::{EuclideanSpace, MetricSpace, Point3, Rad, Transform, Vector2, Zero};
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

    fn get_canvas_fingers(
        screen_finger_1: Vector2<f32>,
        screen_finger_2: Vector2<f32>,
        state: &Presentation,
    ) -> (Vector2<f32>, Vector2<f32>, Vector2<f32>) {
        let f1 = state
            .screen_to_canvas
            .transform_point(Point3::from_vec(screen_finger_1.extend(0.0)))
            .xy()
            .to_vec();
        let f2 = state
            .screen_to_canvas
            .transform_point(Point3::from_vec(screen_finger_2.extend(0.0)))
            .xy()
            .to_vec();
        let midpoint = (f1 + f2) * 0.5;

        (f1, f2, midpoint)
    }

    pub fn update(
        &mut self,
        fingers: Vec<Finger>,
        state: &mut Presentation,
        window_size: Vector2<f32>,
    ) -> bool {
        // figure out pan, rotation, and zoom
        if fingers.len() < 2 {
            self.is_pinch_zooming = false;
            return false;
        }

        let screen_finger_1 = Vector2::new(fingers[0].x as f32, fingers[0].y as f32);
        let screen_finger_2 = Vector2::new(fingers[1].x as f32, fingers[1].y as f32);

        let (f1, f2, midpoint) = Self::get_canvas_fingers(screen_finger_1, screen_finger_2, &state);

        if self.is_pinch_zooming {
            if self.frame_count > 2 {
                // panning
                // todo this could be cached lol
                let prev_midpoint = (self.initial_positions[0] + self.initial_positions[1]) * 0.5;
                let delta = midpoint - prev_midpoint;
                state.pan -= delta;

                // zoomer shit
                let dst = Vector2::distance(screen_finger_1, screen_finger_2);
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

            self.initial_distance = Vector2::distance(screen_finger_1, screen_finger_2);

            self.frame_count = 0;
            self.initial_zoom = state.zoom;
        }

        false
    }
}

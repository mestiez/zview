use crate::presentation::Presentation;
use cgmath::num_traits::zero;
use cgmath::{EuclideanSpace, InnerSpace, Matrix4, MetricSpace, Point3, Transform, Vector2, Zero};
use sdl3::touch::Finger;

pub struct TouchState {
    is_pinch_zooming: bool,
    initial_distance: f32,
    initial_zoom: f32,
    initial_pan: Vector2<f32>,
    initial_orientation: f32,

    initial_canvas_positions: [Vector2<f32>; 2],
    initial_screen_positions: [Vector2<f32>; 2],

    frame_count: u32,
}

impl TouchState {
    pub fn new() -> Self {
        TouchState {
            initial_zoom: 1.0,
            initial_distance: 0.0,
            initial_orientation: 0.0,
            initial_pan: zero(),

            initial_canvas_positions: [zero(), zero()],
            initial_screen_positions: [zero(), zero()],
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
    ) -> (Vector2<f32>, Vector2<f32>) {
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

        (f1, f2)
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

        if self.is_pinch_zooming {
            if self.frame_count > 2 {
                // rotation
                // TODO something here is broken :)
                // state.orientation.value = {
                //     let th_prv = (self.initial_screen_positions[0] - self.initial_screen_positions[1]).angle(Vector2::unit_x());
                //     let th_now = (screen_finger_1 - screen_finger_2).angle(Vector2::unit_x());
                //     let delta = th_now - th_prv;
                //     self.initial_orientation - delta.0
                // };

                // zoom
                state.zoom = {
                    let dst = Vector2::distance(screen_finger_1, screen_finger_2);
                    let ratio = dst / self.initial_distance;
                    self.initial_zoom * ratio
                };

                // pan
                state.update_transforms(window_size);
                state.pan += {
                    let (f1, f2) =
                        Self::get_canvas_fingers(screen_finger_1, screen_finger_2, &state);
                    self.initial_canvas_positions[0] - f1
                };

                // the question is actually just:
                //  what transformation needs to be made in order to go from
                //      initial_fingers -> current_fingers
                //  in canvas space...

                return true;
            } else {
                self.frame_count += 1;
            }
        } else {
            self.is_pinch_zooming = true;

            let (f1, f2) = Self::get_canvas_fingers(screen_finger_1, screen_finger_2, &state);
            self.initial_canvas_positions[0] = f1;
            self.initial_canvas_positions[1] = f2;

            self.initial_screen_positions[0] = screen_finger_1;
            self.initial_screen_positions[1] = screen_finger_2;

            self.initial_distance = Vector2::distance(screen_finger_1, screen_finger_2);

            self.frame_count = 0;
            self.initial_zoom = state.zoom;
            self.initial_orientation = state.orientation.value;
            self.initial_pan = state.pan;
        }

        false
    }
}

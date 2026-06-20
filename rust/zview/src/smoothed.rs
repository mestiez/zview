use cgmath::{Angle, Rad, Vector2, VectorSpace};

pub struct Smoothed<T: Copy> {
    pub smoothed: T,
    pub value: T,
    pub coefficient: f32,
}

impl<T: Copy> Smoothed<T> {
    fn get_factor(&self, dt: f32) -> f32 {
        1.0 - self.coefficient.powf(dt)
    }
}

impl Smoothed<Vector2<f32>> {
    pub fn update(&mut self, dt: f32) {
        self.smoothed = self.smoothed.lerp(self.value, self.get_factor(dt));
    }
}

impl Smoothed<f32> {
    pub fn update(&mut self, dt: f32) {
        let delta = self.value - self.smoothed;
        self.smoothed += self.get_factor(dt) * delta;
    }
}

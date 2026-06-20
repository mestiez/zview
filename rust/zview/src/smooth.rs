use std::ops::{Add, Mul, Sub};

pub struct Smoothed<T>
where
    T: Mul<f32, Output = T> + Add<Output = T> + Sub<Output = T> + Copy,
{
    pub value: T,
    pub smoothed: T,
    coefficient: f32,
}

impl<T> Smoothed<T>
where
    T: Mul<f32, Output = T> + Add<Output = T> + Sub<Output = T> + Copy,
{
    pub fn new(value: T, coefficient: f32) -> Smoothed<T> {
        Smoothed {
            coefficient,
            value,
            smoothed: value,
        }
    }

    pub fn update(&mut self, dt: f32) {
        let f = 1.0 - self.coefficient.powf(dt);
        self.smoothed = self.smoothed + (self.value - self.smoothed) * f;
    }
}

namespace zview;

public struct SmoothDouble(double v, double coefficient = 1e-13)
{
    public double Value = v, Smoothed = v;

    public void Update(double dt)
    {
        Smoothed = double.Lerp(Smoothed, Value, 1 - double.Pow(coefficient, dt));
    }
}
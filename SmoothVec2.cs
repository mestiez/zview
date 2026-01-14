using System.Numerics;

namespace zview;

public struct SmoothVec2(Vector2 v, double coefficient = 1e-13)
{
    public Vector2 Value = v, Smoothed = v;

    public void Update(double dt)
    {
        Smoothed = Vector2.Lerp(Smoothed, Value, (float)(1 - double.Pow(coefficient, dt)));
    }
}
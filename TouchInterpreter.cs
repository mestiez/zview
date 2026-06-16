using System.Numerics;
using SDL3;

namespace zview;

public class TouchInterpreter
{
    private bool isPinchZooming = false;
    private float pinchZoomCalibrateDistance = 0;
    private double zoomAtPinchStart = 1;
    private Vector2 panAtPinchStart = default;

    private readonly Vector2[] previousPositions = new Vector2[2];

    public bool IsBeingUsed => isPinchZooming;

    public bool Update(SDL.Finger[] fingers, int w, int h, ref Vector2 pan, ref double zoom,
        Matrix4x4 screenToCanvas)
    {
        if (fingers.Length < 2)
        {
            isPinchZooming = false;
            return false;
        }

        var finger1 = Vector2.Transform(new Vector2(fingers[0].X * w, fingers[0].Y * h), screenToCanvas);
        var finger2 = Vector2.Transform(new Vector2(fingers[1].X * w, fingers[1].Y * h), screenToCanvas);
        var midpoint = (finger1 + finger2) * 0.5f;

        var finger1Delta = finger1 - previousPositions[0];
        var finger2Delta = finger2 - previousPositions[1];

        previousPositions[0] = finger1;
        previousPositions[1] = finger2;

        var dist = Vector2.Distance( // we cant use the canvas-space fingers here because it depends on zoom
            new Vector2(fingers[0].X, fingers[0].Y),
            new Vector2(fingers[1].X, fingers[1].Y)
        );

        if (isPinchZooming)
        {
            var f = pinchZoomCalibrateDistance / dist;
            var s = 1 - f * zoomAtPinchStart / zoom;
            zoom -= zoom * s;
            pan += (midpoint - pan) * (float)s;
            pan -= (finger1Delta + finger2Delta) * 4; // again no idea why the x4 multiplier
            return true;
        }
        else
        {
            isPinchZooming = true;
            zoomAtPinchStart = zoom;
            panAtPinchStart = pan;
            pinchZoomCalibrateDistance = dist;
        }

        return false;
    }
}
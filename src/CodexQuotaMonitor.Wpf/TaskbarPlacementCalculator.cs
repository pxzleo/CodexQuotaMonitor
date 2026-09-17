namespace CodexQuotaMonitor.Wpf;

public static class TaskbarPlacementCalculator
{
    private const uint EdgeLeft = 0;
    private const uint EdgeTop = 1;
    private const uint EdgeRight = 2;
    private const uint EdgeBottom = 3;

    public static TaskbarPlacement Compute(
        uint edge,
        NativeMethods.RECT taskbar,
        int preferredWidth,
        int extraHeight,
        int screenWidth,
        int screenHeight)
    {
        if (edge is EdgeTop or EdgeBottom && taskbar.Width > 0 && taskbar.Height > 0)
        {
            var width = Math.Min(Math.Max(1, preferredWidth), taskbar.Width);
            var height = taskbar.Height + extraHeight;
            var y = edge == EdgeBottom
                ? taskbar.Top - extraHeight
                : extraHeight > 0 ? taskbar.Bottom : taskbar.Top;
            return Clamp(new TaskbarPlacement(taskbar.Left, y, width, height), screenWidth, screenHeight);
        }

        // A vertical taskbar has no meaningful horizontal "taskbar height". Keep the
        // compact overlay at the lower-left of the primary screen in that layout.
        return Fallback(preferredWidth, Constants.DefaultHeight + extraHeight, screenWidth, screenHeight);
    }

    public static TaskbarPlacement Fallback(int preferredWidth, int height, int screenWidth, int screenHeight)
    {
        return Clamp(
            new TaskbarPlacement(0, Math.Max(0, screenHeight - height), preferredWidth, height),
            screenWidth,
            screenHeight);
    }

    public static TaskbarPlacement EdgePlacement(
        string edge,
        int offset,
        int offset2,
        int width,
        int height,
        System.Drawing.Rectangle bounds)
    {
        var w = Math.Max(1, width);
        var h = Math.Max(1, height);
        int x;
        int y;
        switch (edge)
        {
            case Constants.PlacementRight:
                x = Math.Max(0, bounds.Width - w);
                y = ClampOffset(offset2, 0, bounds.Height - h);
                break;
            case Constants.PlacementTop:
                x = ClampOffset(offset, 0, bounds.Width - w);
                y = 0;
                break;
            case Constants.PlacementBottom:
                x = ClampOffset(offset, 0, bounds.Width - w);
                y = Math.Max(0, bounds.Height - h);
                break;
            case Constants.PlacementFree:
                x = ClampOffset(offset, 0, bounds.Width - w);
                y = ClampOffset(offset2, 0, bounds.Height - h);
                break;
            default:
                x = 0;
                y = ClampOffset(offset2, 0, bounds.Height - h);
                break;
        }

        return new TaskbarPlacement(x, y, w, h);
    }

    public static string ResolveSnapEdge(int left, int top, int right, int bottom, int screenWidth, int screenHeight, int snapDistance)
    {
        var candidates = new (string Edge, int Distance)[]
        {
            (Constants.PlacementLeft, left),
            (Constants.PlacementRight, screenWidth - right),
            (Constants.PlacementTop, top),
            (Constants.PlacementBottom, screenHeight - bottom)
        };
        var nearest = candidates.OrderBy(candidate => candidate.Distance).First();
        return nearest.Distance > snapDistance ? Constants.PlacementFree : nearest.Edge;
    }

    private static int ClampOffset(int offset, int min, int max)
    {
        return max < min ? min : Math.Clamp(offset, min, max);
    }

    private static TaskbarPlacement Clamp(TaskbarPlacement placement, int screenWidth, int screenHeight)
    {
        var width = Math.Clamp(placement.Width, 1, Math.Max(1, screenWidth));
        var height = Math.Clamp(placement.Height, 1, Math.Max(1, screenHeight));
        var x = Math.Clamp(placement.X, 0, Math.Max(0, screenWidth - width));
        var y = Math.Clamp(placement.Y, 0, Math.Max(0, screenHeight - height));
        return new TaskbarPlacement(x, y, width, height);
    }
}

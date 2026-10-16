namespace Browser.Core;

public sealed record ChromeLayout(double Sidebar, double Header)
{
    public static ChromeLayout? From(Command command, double viewportWidth, double viewportHeight)
    {
        if (command.X is not { } x || command.Y is not { } y || command.Width is not { } width || command.Height is not { } height)
            return null;
        if (!new[] { x, y, width, height, viewportWidth, viewportHeight }.All(double.IsFinite)) return null;
        if (x < 0 || y < 20 || y > 128 || width < 80 || height < 60 || x > viewportWidth - 80) return null;
        if (Math.Abs(x + width - viewportWidth) > 3 || Math.Abs(y + height - viewportHeight) > 3) return null;
        return new(x, y);
    }
}

namespace zview;

internal class Program
{
    internal static void Main(string[] args)
    {
        using var p = new Presentation();

        if (args.Length == 1)
            p.SetTexture(args[0]);

        p.RunLoop();
    }
}
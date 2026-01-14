using System.Reflection;

namespace zview;

internal class Program
{
    internal static void Main(string[] args)
    {
        if (args is ["-v"])
        {
            var ass = Assembly.GetCallingAssembly();
            Console.WriteLine(ass.GetName()?.Version ?? new Version());
            return;
        }
        else if (args is ["-h"])
        {
            Console.WriteLine($"Usage: {nameof(zview)} [filename or folder path]");
            return;
        }else if (args.Length == 1 && args[0].StartsWith('-'))
        {
            Console.Error.WriteLine("Unknown flag ({0})", args[0]);
            return;
        }

        using var p = new Presentation();

        if (args.Length == 1)
            p.SetTexture(args[0]);

        p.RunLoop();
    }
}
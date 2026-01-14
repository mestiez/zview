using System.Reflection;

namespace zview;

internal class Program
{
    internal static void Main(string[] args)
    {
        switch (args)
        {
            case ["-v"]:
            {
                var ass = Assembly.GetCallingAssembly();
                Console.WriteLine(ass.GetName()?.Version ?? new Version());
                return;
            }
            case ["-h"]:
                Console.WriteLine($"Usage: {nameof(zview)} [filename or folder path]");
                return;
            default:
            {
                if (args.Length == 1 && args[0].StartsWith('-'))
                {
                    Console.Error.WriteLine("Unknown flag ({0})", args[0]);
                    return;
                }
                break;
            }
        }

        using var p = new Presentation();

        if (args.Length == 1)
            p.SetTexture(args[0]);

        p.RunLoop();
    }
}
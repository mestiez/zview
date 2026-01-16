using System.Reflection;

namespace zview;

internal class Program
{
    internal static void Main(string[] args)
    {
        string? path = null;
        if (args.Length > 0)
        {
            foreach (var arg in args)
            {
                if (arg.StartsWith('-'))
                {
                    switch (arg)
                    {
                        case "-v" or "--version":
                        {
                            var ass = Assembly.GetExecutingAssembly();
                            Console.WriteLine(ass.GetName()?.Version ?? throw new Exception("Assembly not found"));
                            return;
                        }
                        case "-h" or "--help":
                            Console.WriteLine("""
                                              zview - image viewer for x11 

                                              Usage:
                                                zview [options] [path]

                                              Arguments:
                                                path              Path to the image or directory (optional).

                                              Options:
                                                -v, --version     Display version information.
                                                -h, --help        Show help and usage information.
                                              """);
                            return;
                        default:
                            Console.Error.WriteLine("Unknown flag ({0}). Run {1} -h for usage info.", args[0],
                                nameof(zview));
                            return;
                    }
                }

                path = arg;
            }
        }

        using var p = new Presentation();
        if (!string.IsNullOrWhiteSpace(path))
        {
            if (!p.SetTexture(path))
                return;
        }
        p.RunLoop();
    }
}
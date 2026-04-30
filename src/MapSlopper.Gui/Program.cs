using Avalonia;
using System;
using System.IO;
using MapSlopper.Core.Export;
using MapSlopper.Core.Generation;
using MapSlopper.Core.Project;

namespace MapSlopper.Gui;

class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static int Main(string[] args)
    {
        // Optional CLI passthrough: if the first arg is a CLI verb, run headless and exit.
        // The dedicated MapSlopper.Cli.exe is still the recommended lightweight option.
        if (args.Length > 0 && args[0] is "build" or "validate" or "--help" or "-h" or "help")
        {
            return RunCli(args);
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        return 0;
    }

    private static int RunCli(string[] args)
    {
        try
        {
            switch (args[0])
            {
                case "--help":
                case "-h":
                case "help":
                    PrintCliUsage();
                    return 0;
                case "build":
                    return CliBuild(args);
                case "validate":
                    return CliValidate(args);
                default:
                    Console.Error.WriteLine($"Unknown command: {args[0]}");
                    return 64;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    private static int CliBuild(string[] args)
    {
        if (args.Length < 3)
        {
            Console.Error.WriteLine("Usage: build <project.json> <output.map>");
            return 64;
        }
        var project = ProjectJsonIo.Load(args[1]);
        var result = GeometryGenerator.Generate(project);
        foreach (var issue in result.Issues) Console.Error.WriteLine(issue);
        if (!result.Ok || result.Document is null)
        {
            Console.Error.WriteLine("Build failed.");
            return 1;
        }
        MapWriter.WriteToFile(result.Document, args[2]);
        Console.WriteLine($"Wrote {result.Document.Worldspawn.Brushes.Count} brushes to {args[2]}");
        return 0;
    }

    private static int CliValidate(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Usage: validate <project.json>");
            return 64;
        }
        var project = ProjectJsonIo.Load(args[1]);
        var result = GeometryGenerator.Generate(project);
        foreach (var issue in result.Issues) Console.Error.WriteLine(issue);
        Console.WriteLine(result.Ok ? "OK" : "INVALID");
        return result.Ok ? 0 : 1;
    }

    private static void PrintCliUsage()
    {
        Console.WriteLine("MapSlopper GUI (with CLI passthrough)");
        Console.WriteLine("Usage:");
        Console.WriteLine("  MapSlopper.Gui.exe                       Launch the GUI");
        Console.WriteLine("  MapSlopper.Gui.exe build <proj> <out>    Build a .map file");
        Console.WriteLine("  MapSlopper.Gui.exe validate <proj>       Validate a project.json");
        Console.WriteLine("  MapSlopper.Gui.exe --help                Show this help");
        Console.WriteLine();
        Console.WriteLine("Tip: prefer MapSlopper.Cli.exe for headless batch workflows (smaller, no Avalonia).");
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}

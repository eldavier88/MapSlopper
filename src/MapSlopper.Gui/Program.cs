using System;
using System.IO;
using Avalonia;
using MapSlopper.Core.Export;
using MapSlopper.Core.Generation;
using MapSlopper.Core.Project;

namespace MapSlopper.Gui;

class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // If any CLI args are passed, run in headless CLI mode and exit.
        if (args.Length > 0 && args[0] is not "--" )
        {
            return RunCli(args);
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        return 0;
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();

    // ─── CLI ────────────────────────────────────────────────────────────────

    private static int RunCli(string[] args)
    {
        if (args[0] is "-h" or "--help" or "help")
        {
            PrintUsage();
            return 0;
        }
        try
        {
            return args[0] switch
            {
                "build"    => Build(args),
                "validate" => Validate(args),
                _          => Fail($"Unknown command '{args[0]}'. Run with --help for usage."),
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("ERROR: " + ex.Message);
            return 1;
        }
    }

    private static int Build(string[] args)
    {
        string? input = null;
        string? output = null;
        for (var i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-o": case "--out":
                    if (++i >= args.Length) return Fail("Missing value for -o/--out.");
                    output = args[i];
                    break;
                default:
                    if (input is null) input = args[i];
                    else return Fail($"Unexpected argument: {args[i]}");
                    break;
            }
        }
        if (input is null) return Fail("Missing input project file.");
        output ??= Path.ChangeExtension(input, ".map");

        var project = ProjectJsonIo.Load(input);
        var result = GeometryGenerator.Generate(project);
        foreach (var issue in result.Issues) Console.Error.WriteLine(issue);
        if (!result.Ok)
        {
            Console.Error.WriteLine("Build failed.");
            return 1;
        }
        MapWriter.WriteToFile(result.Document!, output);
        Console.WriteLine(
            $"Wrote {output} ({result.Document!.Worldspawn.Brushes.Count} brushes, "
            + $"{result.Document!.Entities.Count} entities)");
        return 0;
    }

    private static int Validate(string[] args)
    {
        if (args.Length < 2) return Fail("Missing input project file.");
        var project = ProjectJsonIo.Load(args[1]);
        var result = GeometryGenerator.Generate(project);
        foreach (var issue in result.Issues) Console.Error.WriteLine(issue);
        Console.WriteLine(result.Ok ? "OK" : "INVALID");
        return result.Ok ? 0 : 1;
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine("ERROR: " + message);
        Console.Error.WriteLine();
        PrintUsage();
        return 64;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("MapSlopper — 2.5D Quake 3 level editor");
        Console.WriteLine();
        Console.WriteLine("GUI mode (no arguments):");
        Console.WriteLine("  MapSlopper.exe");
        Console.WriteLine();
        Console.WriteLine("CLI mode:");
        Console.WriteLine("  MapSlopper.exe build <project.mapsproj.json> [-o <out.map>]");
        Console.WriteLine("  MapSlopper.exe validate <project.mapsproj.json>");
    }
}

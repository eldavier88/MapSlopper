using System;
using System.IO;
using MapSlopper.Core.Export;
using MapSlopper.Core.Generation;
using MapSlopper.Core.Project;

namespace MapSlopper.Cli;

internal static class Program
{
    private const int ExitOk = 0;
    private const int ExitUsage = 64;
    private const int ExitFailure = 1;

    public static int Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            PrintUsage();
            return ExitOk;
        }
        try
        {
            return args[0] switch
            {
                "build" => Build(args),
                "validate" => Validate(args),
                _ => Fail($"Unknown command: {args[0]}"),
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("ERROR: " + ex.Message);
            return ExitFailure;
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
                case "-o":
                case "--out":
                    if (++i >= args.Length) return Fail("Missing value for --out.");
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
            return ExitFailure;
        }
        MapWriter.WriteToFile(result.Document!, output);
        Console.WriteLine(
            $"Wrote {output} ({result.Document!.Worldspawn.Brushes.Count} brushes, "
            + $"{result.Document!.Entities.Count} entities)");
        return ExitOk;
    }

    private static int Validate(string[] args)
    {
        if (args.Length < 2) return Fail("Missing input project file.");
        var project = ProjectJsonIo.Load(args[1]);
        var result = GeometryGenerator.Generate(project);
        var ok = result.Ok;
        foreach (var issue in result.Issues) Console.Error.WriteLine(issue);
        Console.WriteLine(ok ? "OK" : "INVALID");
        return ok ? ExitOk : ExitFailure;
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine("ERROR: " + message);
        Console.Error.WriteLine();
        PrintUsage();
        return ExitUsage;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("MapSlopper CLI");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  mapslopper build <project.mapsproj.json> [-o <out.map>]");
        Console.WriteLine("  mapslopper validate <project.mapsproj.json>");
        Console.WriteLine("  mapslopper help");
    }
}

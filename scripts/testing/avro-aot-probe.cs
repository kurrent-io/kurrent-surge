#!/usr/bin/env dotnet
#:package CliWrap@3.8.2
#:package Spectre.Console@0.55.2
#:package System.CommandLine@2.0.8

// avro-aot-probe.cs — Publish the Avro AOT probe natively, run it, and measure the binary.
//
// The probe is the only check that proves the generated Avro codec works under Native AOT: the
// trim/AOT analyzers can be satisfied by code that still fails at runtime. It also carries this
// feature's headline measurement — the number of Newtonsoft.Json symbols left in the native binary.
// Newtonsoft arrives transitively through Apache.Avro and is reachable only from Avro.Schema.Parse,
// so its symbol count is a direct read of whether anything still parses a schema at startup.
//
// Symbols are counted with `nm`, not inferred from IL. A type initializer can look unreachable in the
// IL and still be rooted by the compiler; only the linked binary settles it.

using System.CommandLine;
using System.Globalization;
using CliWrap;
using CliWrap.Buffered;
using Spectre.Console;

var repoRoot = FindRepoRoot(Environment.CurrentDirectory);

var projectPath = Path.Combine(repoRoot, "test", "Kurrent.Surge.Codecs.Avro.AotProbe", "Kurrent.Surge.Codecs.Avro.AotProbe.csproj");
var resultPath  = Path.Combine(repoRoot, ".artifacts", "avro-aot-probe.json");

var baselineOpt = new Option<string?>("--baseline") { Description = "Path to a previous result JSON to compare against" };
var outputOpt   = new Option<string?>("--output") { Description = $"Result JSON path (default: {resultPath})" };
var skipOpt     = new Option<bool>("--no-publish") { Description = "Reuse the existing published binary" };

var rootCmd = new RootCommand("Publish, run and measure the Avro Native AOT probe") { baselineOpt, outputOpt, skipOpt };

var exitCode = 0;

rootCmd.SetAction(async (result, _) => {
    exitCode = await Run(
        result.GetValue(baselineOpt),
        result.GetValue(outputOpt) ?? resultPath,
        result.GetValue(skipOpt));
});

var parseResult = await rootCmd.Parse(args).InvokeAsync();

// The probe's own verdict is the exit code. Parsing failures still win, since there was no run.
return parseResult != 0 ? parseResult : exitCode;

async Task<int> Run(string? baselinePath, string outputPath, bool skipPublish) {
    AnsiConsole.WriteLine();
    AnsiConsole.Write(new Rule("[bold blue]Avro Native AOT probe[/]").LeftJustified());
    AnsiConsole.WriteLine();

    var publishDir = Path.Combine(repoRoot, ".artifacts", "avro-aot-probe", "publish");

    if (!skipPublish) {
        AnsiConsole.MarkupLine("[grey]publishing[/] (native AOT link takes a minute)");

        var publish = await Cli.Wrap("dotnet")
            .WithArguments(["publish", projectPath, "-c", "Release", "-o", publishDir])
            .WithWorkingDirectory(repoRoot)
            .WithValidation(CommandResultValidation.None)
            .ExecuteBufferedAsync();

        if (publish.ExitCode != 0) {
            AnsiConsole.MarkupLine("[red]publish failed[/]");
            AnsiConsole.WriteLine(publish.StandardOutput);
            AnsiConsole.WriteLine(publish.StandardError);
            return 1;
        }
    }

    var binary = FindBinary(publishDir);

    if (binary is null) {
        AnsiConsole.MarkupLine($"[red]no native binary under {publishDir}[/]");
        return 1;
    }

    var run = await Cli.Wrap(binary)
        .WithValidation(CommandResultValidation.None)
        .ExecuteBufferedAsync();

    AnsiConsole.WriteLine(run.StandardOutput.TrimEnd());

    if (run.StandardError.Length > 0) AnsiConsole.WriteLine(run.StandardError.TrimEnd());

    var symbols = await LinkedSymbols(binary);

    var newtonsoft = symbols.Where(symbol => symbol.Contains("Newtonsoft", StringComparison.OrdinalIgnoreCase)).ToList();

    var measurement = new Measurement(
        Binary:            Path.GetRelativePath(repoRoot, binary),
        Bytes:             new FileInfo(binary).Length,
        NewtonsoftStrings: await NewtonsoftStrings(binary),
        NewtonsoftSymbols: newtonsoft.Count,
        SchemaParseLinked: symbols.Any(symbol => symbol.Contains("Avro_Schema__Parse", StringComparison.Ordinal)),
        Passed:            run.ExitCode == 0 && run.StandardOutput.Contains("RESULT: ALL PASSED"),
        SampleSymbols:     newtonsoft.Take(20).ToList());

    Report(measurement, baselinePath);

    Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
    File.WriteAllText(outputPath, ToJson(measurement));

    AnsiConsole.WriteLine();
    AnsiConsole.MarkupLine($"[grey]result written to {Path.GetRelativePath(repoRoot, outputPath)}[/]");

    return measurement.Passed ? 0 : 1;
}

void Report(Measurement measurement, string? baselinePath) {
    var baseline = baselinePath is not null && File.Exists(baselinePath) ? File.ReadAllText(baselinePath) : null;

    var table = new Table().AddColumn("measure").AddColumn("value");

    if (baseline is not null) table.AddColumn("baseline").AddColumn("delta");

    AddRow("native binary bytes", measurement.Bytes, Number(baseline, "bytes"));
    AddRow("newtonsoft strings", measurement.NewtonsoftStrings, Number(baseline, "newtonsoftStrings"));
    AddRow("newtonsoft symbols", measurement.NewtonsoftSymbols, Number(baseline, "newtonsoftSymbols"));

    table.AddRow("Avro.Schema.Parse linked", measurement.SchemaParseLinked ? "[red]yes[/]" : "[green]no[/]");
    table.AddRow("round trip", measurement.Passed ? "[green]ALL PASSED[/]" : "[red]FAILED[/]");

    AnsiConsole.WriteLine();
    AnsiConsole.Write(table);

    if (measurement.SampleSymbols.Count > 0) {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[yellow]Newtonsoft symbols still linked (first {measurement.SampleSymbols.Count}):[/]");

        foreach (var symbol in measurement.SampleSymbols) AnsiConsole.WriteLine($"  {symbol}");
    }

    void AddRow(string name, long value, long? previous) {
        if (previous is null) {
            table.AddRow(name, value.ToString("N0"));
            return;
        }

        var delta  = value - previous.Value;
        var colour = delta switch { < 0 => "green", > 0 => "red", _ => "grey" };

        table.AddRow(name, value.ToString("N0"), previous.Value.ToString("N0"), $"[{colour}]{delta:+#,0;-#,0;0}[/]");
    }
}

// The count the earlier measurements used, and the one the feature's baseline is stated in. It reads
// name blobs rather than the symbol table, so it also catches assembly metadata the linker kept.
async Task<int> NewtonsoftStrings(string binary) {
    var strings = await Cli.Wrap("strings")
        .WithArguments(["-a", binary])
        .WithValidation(CommandResultValidation.None)
        .ExecuteBufferedAsync();

    return strings.ExitCode != 0
        ? -1
        : strings.StandardOutput
            .Split('\n')
            .Count(line => line.Contains("Newtonsoft", StringComparison.OrdinalIgnoreCase));
}

// `nm` output is one symbol per line, address and type first:
//   0000000100a1b2c3 t Newtonsoft_Json_JsonTextReader__ParseValue
// Native AOT mangles managed names, so the assembly name survives as a prefix and a substring match is
// the reliable filter. Undefined symbols carry no address, which is why the name is taken as the last
// whitespace-separated field rather than a fixed column.
//
// On macOS the linked executable keeps only a stub symbol table and the real one lives in the .dSYM
// bundle beside it, so that is preferred when present. Elsewhere the binary carries its own.
async Task<List<string>> LinkedSymbols(string binary) {
    var dwarf = Path.Combine($"{binary}.dSYM", "Contents", "Resources", "DWARF", Path.GetFileName(binary));
    var from  = File.Exists(dwarf) ? dwarf : binary;

    var nm = await Cli.Wrap("nm")
        .WithArguments(["-a", from])
        .WithValidation(CommandResultValidation.None)
        .ExecuteBufferedAsync();

    if (nm.ExitCode != 0) {
        AnsiConsole.MarkupLine("[yellow]nm failed; symbol counts unavailable[/]");
        return [];
    }

    return nm.StandardOutput
        .Split('\n', StringSplitOptions.RemoveEmptyEntries)
        .Select(line => line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "")
        .Distinct(StringComparer.Ordinal)
        .OrderBy(symbol => symbol, StringComparer.Ordinal)
        .ToList();
}

// The assembly name is dotted, so Path.GetFileNameWithoutExtension would strip ".AotProbe" as an
// extension. Match the file name whole instead.
static string? FindBinary(string publishDir) =>
    Directory.Exists(publishDir)
        ? Directory.EnumerateFiles(publishDir)
            .FirstOrDefault(path => Path.GetFileName(path) is "Kurrent.Surge.Codecs.Avro.AotProbe"
                                                           or "Kurrent.Surge.Codecs.Avro.AotProbe.exe")
        : null;

static string FindRepoRoot(string start) {
    var directory = new DirectoryInfo(start);

    while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, ".git")))
        directory = directory.Parent;

    return directory?.FullName ?? throw new InvalidOperationException("not inside a git repository");
}

// Hand-written rather than System.Text.Json: this script is compiled under the repo's AOT analysis
// settings, and the reflection-based serializer overloads raise IL2026/IL3050 on every run.
static string ToJson(Measurement measurement) {
    var symbols = string.Join(",\n    ", measurement.SampleSymbols.Select(symbol => $"\"{symbol}\""));

    return $$"""
             {
               "binary": "{{measurement.Binary}}",
               "bytes": {{measurement.Bytes}},
               "newtonsoftStrings": {{measurement.NewtonsoftStrings}},
               "newtonsoftSymbols": {{measurement.NewtonsoftSymbols}},
               "schemaParseLinked": {{(measurement.SchemaParseLinked ? "true" : "false")}},
               "passed": {{(measurement.Passed ? "true" : "false")}},
               "sampleSymbols": [
                   {{symbols}}
               ]
             }
             """;
}

static long? Number(string? json, string property) {
    if (json is null) return null;

    var start = json.IndexOf($"\"{property}\":", StringComparison.Ordinal);

    if (start < 0) return null;

    var digits = json[(start + property.Length + 3)..].TrimStart();
    var end    = 0;

    while (end < digits.Length && char.IsDigit(digits[end])) end++;

    return end > 0 && long.TryParse(digits[..end], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
        ? value
        : null;
}

record Measurement(
    string       Binary,
    long         Bytes,
    int          NewtonsoftStrings,
    int          NewtonsoftSymbols,
    bool         SchemaParseLinked,
    bool         Passed,
    List<string> SampleSymbols);

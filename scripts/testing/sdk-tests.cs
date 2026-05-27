#!/usr/bin/env dotnet
#:package CliWrap@3.8.2
#:package Spectre.Console@0.55.2
#:package System.CommandLine@2.0.8

// sdk-tests.cs — Canonical test runner for the Kurrent SDK solution.
// C# file-based app using CliWrap, Spectre.Console, and System.CommandLine.

using System.CommandLine;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using CliWrap;
using CliWrap.Buffered;
using CliWrap.EventStream;
using Spectre.Console;
using SysCommand = System.CommandLine.Command;

var repoRoot = FindRepoRoot(Environment.CurrentDirectory);

// =============================================================================
// CLI
// =============================================================================

var categoryArg = new Argument<string>("category") { Description = "Test category: unit, integration, or all" };
var filterOpt = new Option<string?>("--treenode-filter") { Description = "TUnit treenode filter pattern" };
var extraArgs = new Argument<string[]>("extra") { Arity = ArgumentArity.ZeroOrMore, Description = "Extra TUnit arguments" };

var runCmd = new SysCommand("run", "Execute tests and produce results") { categoryArg, filterOpt, extraArgs };
var listCmd = new SysCommand("list", "Discover and list matching tests without running them") { categoryArg, filterOpt };
var rootCmd = new RootCommand("Canonical test runner for the Kurrent SDK solution") { runCmd, listCmd };

runCmd.SetAction(async (result, _) => {
    Environment.ExitCode = await RunTests(
        result.GetValue(categoryArg)!,
        result.GetValue(filterOpt),
        result.GetValue(extraArgs) ?? []);
});

listCmd.SetAction(async (result, _) => {
    Environment.ExitCode = await ListTests(
        result.GetValue(categoryArg)!,
        result.GetValue(filterOpt));
});

return await rootCmd.Parse(args).InvokeAsync();

// =============================================================================
// List
// =============================================================================

async Task<int> ListTests(string category, string? userFilter) {
    var filter = BuildFilter(userFilter, CategoryProp(category));

    AnsiConsole.Write(new Rule($"[blue]list {category} tests[/]").LeftJustified());
    AnsiConsole.MarkupLine($"  Filter: [dim]{Markup.Escape(filter)}[/]");
    AnsiConsole.WriteLine();

    var result = await Cli.Wrap("dotnet")
        .WithArguments(["test", "--list-tests", "--no-progress", "--ignore-exit-code", "8",
            "--", "--disable-logo", "--treenode-filter", filter])
        .WithWorkingDirectory(repoRoot)
        .WithValidation(CommandResultValidation.None)
        .ExecuteBufferedAsync();

    foreach (var line in result.StandardOutput.Split('\n'))
        if (!string.IsNullOrEmpty(line) && !line.StartsWith("Discovering"))
            Console.WriteLine(line);

    return result.ExitCode;
}

// =============================================================================
// Run
// =============================================================================

async Task<int> RunTests(string category, string? userFilter, string[] passthrough) {
    var runId = $"{DateTime.Now:yyyyMMdd-HHmmss}-{BitConverter.ToString(RandomNumberGenerator.GetBytes(2)).Replace("-", "").ToLowerInvariant()}";
    var filter = BuildFilter(userFilter, CategoryProp(category));
    var resultsDir = Path.Combine(repoRoot, ".artifacts", "test-results", runId);
    Directory.CreateDirectory(resultsDir);

    // --- Header ---
    AnsiConsole.WriteLine();
    AnsiConsole.Write(new Rule($"[bold blue]{category} tests[/]").LeftJustified());
    AnsiConsole.WriteLine();

    var headerTable = new Table()
        .Border(TableBorder.None)
        .HideHeaders()
        .AddColumn(new TableColumn("k").Width(10))
        .AddColumn("v");
    headerTable.AddRow("[dim]Run ID[/]", $"[bold]{Markup.Escape(runId)}[/]");
    headerTable.AddRow("[dim]Filter[/]", $"[dim]{Markup.Escape(filter)}[/]");
    headerTable.AddRow("[dim]Results[/]", $"[dim].artifacts/test-results/{Markup.Escape(runId)}[/]");
    AnsiConsole.Write(headerTable);
    AnsiConsole.WriteLine();

    // --- Build dotnet test args ---
    var dotnetArgs = new List<string> {
        "test", "-c", "Release",
        "--no-progress", "--ignore-exit-code", "8",
        "--results-directory", resultsDir,
        "--",
        "--disable-logo", "--treenode-filter", filter,
        "--report-trx", "--log-level", "Trace", "--detailed-stacktrace"
    };
    dotnetArgs.AddRange(passthrough);

    // --- Run dotnet test silently with live status ---
    var logBuilder = new StringBuilder();
    var exitCode = 0;
    var passed = 0;
    var failed = 0;
    var skipped = 0;
    var currentAssembly = "";
    var assemblyResults = new Dictionary<string, (string Status, string Duration)>();

    var testCmd = Cli.Wrap("dotnet")
        .WithArguments(dotnetArgs)
        .WithWorkingDirectory(repoRoot)
        .WithValidation(CommandResultValidation.None);

    await AnsiConsole.Status()
        .Spinner(Spinner.Known.Dots)
        .SpinnerStyle(new Style(Color.Blue))
        .StartAsync("[blue]Building...[/]", async ctx => {
            await foreach (var evt in testCmd.ListenAsync()) {
                switch (evt) {
                    case StandardOutputCommandEvent { Text: var line }:
                        logBuilder.AppendLine(line);

                        // Track assembly under test
                        var runMatch = Regex.Match(line, @"Running tests from .+[/\\]([^/\\]+)\.dll");
                        if (runMatch.Success) {
                            currentAssembly = runMatch.Groups[1].Value;
                            ctx.Status($"[blue]Running [bold]{Markup.Escape(currentAssembly)}[/]...[/]");
                        }

                        // Track assembly completion
                        // Matches both "passed (1s 022ms)" and "failed with 3 error(s) (45s 060ms)"
                        var doneMatch = Regex.Match(line, @"([^\s/\\]+)\.dll \([^)]+\) (passed|failed)[^(]*\(([^)]+)\)$");
                        if (doneMatch.Success) {
                            var asmName = doneMatch.Groups[1].Value;
                            var status = doneMatch.Groups[2].Value;
                            var duration = doneMatch.Groups[3].Value;
                            assemblyResults[asmName] = (status, duration);

                            var icon = status == "passed" ? "[green]✓[/]" : "[red]✗[/]";
                            ctx.Status($"{icon} {Markup.Escape(asmName)} [dim]({Markup.Escape(duration)})[/]");
                        }

                        // Track test counts from the summary
                        var totalMatch = Regex.Match(line, @"^\s+total: (\d+)");
                        var failedMatch = Regex.Match(line, @"^\s+failed: (\d+)");
                        var succeededMatch = Regex.Match(line, @"^\s+succeeded: (\d+)");
                        var skippedMatch = Regex.Match(line, @"^\s+skipped: (\d+)");
                        if (failedMatch.Success) failed = int.Parse(failedMatch.Groups[1].Value);
                        if (succeededMatch.Success) passed = int.Parse(succeededMatch.Groups[1].Value);
                        if (skippedMatch.Success) skipped = int.Parse(skippedMatch.Groups[1].Value);
                        break;

                    case StandardErrorCommandEvent { Text: var err }:
                        logBuilder.AppendLine(err);
                        break;

                    case ExitedCommandEvent exited:
                        exitCode = exited.ExitCode;
                        break;
                }
            }
        });

    await File.WriteAllTextAsync(Path.Combine(resultsDir, "output.log"), logBuilder.ToString());

    // --- Assembly results table ---
    var asmTable = new Table()
        .Border(TableBorder.Rounded)
        .BorderColor(Color.Grey)
        .AddColumn(new TableColumn("[dim]Assembly[/]"))
        .AddColumn(new TableColumn("[dim]Status[/]").Centered())
        .AddColumn(new TableColumn("[dim]Duration[/]").RightAligned());

    foreach (var (name, (status, duration)) in assemblyResults) {
        var statusMarkup = status switch {
            "passed" => "[green]✓ passed[/]",
            _ => $"[red]✗ failed[/]"
        };
        asmTable.AddRow(new Markup(Markup.Escape(name)), new Markup(statusMarkup), new Markup($"[dim]{Markup.Escape(duration)}[/]"));
    }

    AnsiConsole.Write(asmTable);
    AnsiConsole.WriteLine();

    // --- Summary bar ---
    var total = passed + failed + skipped;
    var passRate = total > 0 ? (int)Math.Round(passed * 100.0 / total) : 100;
    var statusColor = failed > 0 ? "red" : "green";
    var statusText = failed > 0 ? "FAILED" : "PASSED";
    var statusIcon = failed > 0 ? "✗" : "✓";

    var summaryParts = new List<string> {
        $"[{statusColor} bold]{statusIcon} {statusText}[/]",
        $"[bold]{passRate}%[/] pass rate",
        $"[green]✓ {passed}[/] passed"
    };
    if (failed > 0) summaryParts.Add($"[red]✗ {failed}[/] failed");
    if (skipped > 0) summaryParts.Add($"[yellow]△ {skipped}[/] skipped");
    summaryParts.Add($"[dim]{total} total[/]");

    AnsiConsole.MarkupLine(string.Join(" [dim]·[/] ", summaryParts));
    AnsiConsole.WriteLine();

    // --- Post-processing ---
    PostProcess(resultsDir);

    // --- Generate markdown reports ---
    var trxFiles = Directory.GetFiles(resultsDir, "*.trx").OrderBy(f => f).ToArray();
    var trx2md = Path.Combine(repoRoot, "scripts", "testing", "trx2md.py");
    var subtitle = $"{category} tests";

    if (trxFiles.Length > 0) {
        // Generate persistent .md reports (default preset, silent).
        // ExecuteBufferedAsync ensures the process fully completes before continuing —
        // ExecuteAsync can return before file writes are flushed.
        var trx2mdResult = await Cli.Wrap(trx2md)
            .WithArguments(b => { b.Add("--subtitle").Add(subtitle); foreach (var f in trxFiles) b.Add(f); })
            .WithValidation(CommandResultValidation.None)
            .ExecuteBufferedAsync();

        if (trx2mdResult.ExitCode != 0) {
            AnsiConsole.MarkupLine($"[red]trx2md failed (exit {trx2mdResult.ExitCode})[/]");
            if (!string.IsNullOrEmpty(trx2mdResult.StandardError))
                AnsiConsole.MarkupLine($"[red]{Markup.Escape(trx2mdResult.StandardError)}[/]");
            if (!string.IsNullOrEmpty(trx2mdResult.StandardOutput))
                AnsiConsole.MarkupLine($"[dim]{Markup.Escape(trx2mdResult.StandardOutput)}[/]");
        }

        // Get the ai summary for terminal display
        var mdReport = await Cli.Wrap(trx2md)
            .WithArguments(b => {
                b.Add("--stdout").Add("--preset").Add("ai").Add("--subtitle").Add(subtitle);
                foreach (var f in trxFiles) b.Add(f);
            })
            .WithValidation(CommandResultValidation.None)
            .ExecuteBufferedAsync();

        var reportText = mdReport.StandardOutput.Trim();
        if (!string.IsNullOrEmpty(reportText)) {
            AnsiConsole.Write(new Rule("[blue]report[/]").LeftJustified());
            AnsiConsole.WriteLine();
            Console.WriteLine(reportText);
        }
    }

    // --- Command reproduction block ---
    var relResults = $".artifacts/test-results/{runId}";
    var extra = passthrough.Length > 0 ? $" \\\n    {string.Join(' ', passthrough)}" : "";
    var cmdText = $"""
        dotnet test \
          -c Release --no-progress --ignore-exit-code 8 \
          --results-directory {relResults} \
          -- \
          --disable-logo --log-level Trace --detailed-stacktrace \
          --treenode-filter "{filter}" \
          --report-trx{extra}
        """;

    AnsiConsole.WriteLine();
    AnsiConsole.Write(new Rule("[dim]command[/]").LeftJustified());
    AnsiConsole.Write(new Panel(Markup.Escape(cmdText.Trim()))
        .Border(BoxBorder.Rounded)
        .BorderColor(Color.Grey)
        .Padding(1, 0));

    return exitCode;
}

// =============================================================================
// Post-Processing
// =============================================================================

void PostProcess(string resultsDir) {
    foreach (var dir in Directory.GetDirectories(resultsDir))
        Directory.Delete(dir, recursive: true);

    foreach (var html in Directory.GetFiles(resultsDir, "*-report.html")) {
        var lower = Path.Combine(Path.GetDirectoryName(html)!, Path.GetFileName(html).ToLowerInvariant());
        if (html != lower) File.Move(html, lower);
    }

    var osName = GetShortOsName();
    XNamespace ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";

    foreach (var trx in Directory.GetFiles(resultsDir, "*.trx").OrderBy(f => f)) {
        try {
            var doc = XDocument.Load(trx);
            var unitTest = doc.Descendants(ns + "UnitTest").FirstOrDefault();
            if (unitTest is null) { File.Delete(trx); continue; }

            var storage = unitTest.Attribute("storage")?.Value ?? "";
            if (string.IsNullOrEmpty(storage)) continue;

            var assembly = Path.GetFileNameWithoutExtension(storage);
            var framework = Path.GetFileName(Path.GetDirectoryName(storage));
            var newName = Path.Combine(resultsDir, $"{assembly}-{osName}-{framework}-report.trx");

            if (trx != newName && !File.Exists(newName))
                File.Move(trx, newName);
        } catch {
            // Skip unparseable files
        }
    }
}

// =============================================================================
// Helpers
// =============================================================================

static string FindRepoRoot(string startDir) {
    var dir = startDir;
    while (dir is not null) {
        if (Directory.Exists(Path.Combine(dir, ".git"))) return dir;
        dir = Path.GetDirectoryName(dir);
    }
    return startDir;
}

static string CategoryProp(string category) => category switch {
    "unit" => "Category!=Integration",
    "integration" => "Category=Integration",
    "all" => "",
    _ => throw new ArgumentException($"Unknown category: {category}")
};

static string BuildFilter(string? userFilter, string categoryProp) {
    if (string.IsNullOrEmpty(categoryProp))
        return userFilter ?? "/*/*/*/*";

    if (!string.IsNullOrEmpty(userFilter)) {
        if (userFilter.EndsWith(']')) {
            var bracketIdx = userFilter.IndexOf('[');
            var path = userFilter[..bracketIdx];
            var props = userFilter[(bracketIdx + 1)..^1];
            return $"{path}[({props})&({categoryProp})]";
        }
        return $"{userFilter}[{categoryProp}]";
    }

    return $"/*/*/*/*[{categoryProp}]";
}

static string GetShortOsName() {
    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return "windows";
    if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return "linux";
    if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return "macos";
    return "unknown";
}

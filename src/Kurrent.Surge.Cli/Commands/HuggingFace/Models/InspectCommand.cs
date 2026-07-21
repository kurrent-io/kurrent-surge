using System.Text.Json;
using Kurrent.Surge.Cli.Infrastructure;

namespace Kurrent.Surge.Cli;

static class InspectCommand {
    public static Command Build(
        IAnsiConsole console, IAnsiConsole errorConsole, TextWriter output,
        Option<string?> tokenOption, Option<string?> cacheDirOption
    ) {
        var urlArgument = new Argument<string>("url") {
            Description = "A Hugging Face model-file URL (…/blob/ or …/resolve/)."
        };

        var jsonOption = new Option<bool>("--json") {
            Description = "Emit machine-readable JSON instead of a table."
        };

        var command = new Command("inspect", "Lists a model's planned bundle without downloading it.") {
            urlArgument, jsonOption, tokenOption, cacheDirOption
        };

        command.SetAction((result, ct) => errorConsole.ExecuteAsync(async () => {
            var hub = new HuggingFaceHub(
                new HubOptions { Token = result.GetValue(tokenOption), CacheDirectory = result.GetValue(cacheDirOption) });

            var bundle = await hub.InspectModelAsync(ModelRef.From(result.GetValue(urlArgument)!), ct).ConfigureAwait(false);

            if (result.GetValue(jsonOption))
                WriteJson(output, bundle);
            else
                WriteTable(console, bundle);

            return 0;
        }));

        return command;
    }

    static void WriteJson(TextWriter output, ModelBundle bundle) {
        var payload = new InspectJsonResult(
            bundle.Reference.Repo, bundle.Reference.Revision, bundle.Reference.Kind.ToString(), bundle.Reference.Url,
            bundle.Cached, bundle.TotalBytes,
            bundle.Files.Select(f => new InspectJsonFile(f.RepoPath, f.LocalPath, f.Size, f.Sha256)).ToList());

        // Bypasses IAnsiConsole entirely — Spectre wraps at Profile.Width even for non-markup text, which
        // would corrupt this machine-readable payload in a narrow terminal. Raw TextWriter, never wrapped.
        output.WriteLine(JsonSerializer.Serialize(payload, CliJsonContext.Default.InspectJsonResult));
    }

    static void WriteTable(IAnsiConsole console, ModelBundle bundle) {
        console.MarkupLineInterpolated(
            $"[bold]{bundle.Reference.Repo}@{bundle.Reference.Revision}[/] — {bundle.Reference.Kind}, {bundle.Files.Count} files, {bundle.TotalBytes.Humanize()}, cached: {(bundle.Cached ? "yes" : "no")}");

        var table = new Table().AddColumn("File").AddColumn("Size").AddColumn("SHA-256").AddColumn("Cached");

        foreach (var file in bundle.Files) {
            var cached = File.Exists(file.LocalPath) && new FileInfo(file.LocalPath).Length == file.Size;
            var sha    = file.Sha256 is { Length: >= 8 } s ? s[..8] : "—";

            table.AddRow(Markup.Escape(file.RepoPath), file.Size.Humanize(), sha, cached ? "[green]✓[/]" : "[red]✗[/]");
        }

        console.Write(table);
    }
}

// The --json shape for `inspect`: { repo, revision, kind, url, cached, totalBytes, files: [{ repoPath, localPath, size, sha256 }] }
sealed record InspectJsonResult(
    string Repo,
    string Revision,
    string Kind,
    string Url,
    bool Cached,
    long TotalBytes,
    IReadOnlyList<InspectJsonFile> Files
);

sealed record InspectJsonFile(string RepoPath, string LocalPath, long Size, string? Sha256);

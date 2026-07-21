using Kurrent.Surge.Cli.Infrastructure;

namespace Kurrent.Surge.Cli;

static class ModelsCommand {
    public static Command Build(
        IAnsiConsole console, IAnsiConsole errorConsole, TextWriter output,
        Option<string?> tokenOption, Option<string?> cacheDirOption
    ) {
        var repoArgument = new Argument<string>("repo") {
            Description = "The exact repository id as printed by `models list` (e.g. Xenova/all-MiniLM-L6-v2)."
        };

        var clearCommand = new Command(
            "clear", "Clears one cached model from the local cache — every revision. Never touches the Hugging Face Hub."
        ) { repoArgument, cacheDirOption };

        // No confirmation prompt and no --yes here (unlike `cache clear`): naming the exact model IS the
        // confirmation (the docker-rm convention) — `cache clear` keeps its prompt because it is indiscriminate.
        clearCommand.SetAction((result, _) => errorConsole.ExecuteAsync(() => {
            var repo   = result.GetValue(repoArgument)!;
            var hub    = new HuggingFaceHub(new HubOptions { CacheDirectory = result.GetValue(cacheDirOption) });
            var models = hub.ListCachedModels().Where(m => string.Equals(m.Repo, repo, StringComparison.Ordinal)).ToList();

            if (models.Count == 0) {
                errorConsole.MarkupLineInterpolated(
                    $"[red]Error:[/] No cached model matches [bold]{Markup.Escape(repo)}[/] under [bold]{Markup.Escape(hub.CacheDirectory)}[/].");
                return Task.FromResult(1);
            }

            // Same walk-up as `cache clear` — the one models--* directory every matching revision lives under.
            var byDirectory = models
                .GroupBy(m => Directory.GetParent(Directory.GetParent(m.Path)!.FullName)!.FullName)
                .ToList();

            var (_, freedBytes, failures) = CacheCommand.DeleteModelDirectories(byDirectory);

            if (failures.Count > 0) {
                CacheCommand.ReportFailures(errorConsole, failures);
                return Task.FromResult(1);
            }

            console.MarkupLineInterpolated($"Removed [bold]{Markup.Escape(repo)}[/] — {models.Count} snapshot(s), freed {freedBytes.Humanize()}.");

            return Task.FromResult(0);
        }));

        return new Command("models", "Model acquisition operations.") {
            InspectCommand.Build(console, errorConsole, output, tokenOption, cacheDirOption),
            DownloadCommand.Build(console, errorConsole, tokenOption, cacheDirOption),
            clearCommand
        };
    }
}

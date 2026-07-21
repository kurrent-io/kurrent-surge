using Kurrent.Surge.Cli.Infrastructure;

namespace Kurrent.Surge.Cli;

static class CacheCommand {
    public static Command Build(IAnsiConsole console, IAnsiConsole errorConsole, TextWriter output, Option<string?> cacheDirOption) {
        var listCommand = new Command("list", "Lists models cached locally.") { cacheDirOption };

        listCommand.SetAction((result, _) => errorConsole.ExecuteAsync(() => {
            var hub    = new HuggingFaceHub(new HubOptions { CacheDirectory = result.GetValue(cacheDirOption) });
            var models = hub.ListCachedModels();

            if (models.Count == 0) {
                console.MarkupLineInterpolated($"No cached models under [bold]{Markup.Escape(hub.CacheDirectory)}[/].");
                return Task.FromResult(0);
            }

            console.MarkupLineInterpolated(
                $"[bold]{Markup.Escape(hub.CacheDirectory)}[/] — {models.Count} snapshot(s), {models.Sum(m => m.TotalBytes).Humanize()}");

            var table = new Table().AddColumn("Model").AddColumn("Revision").AddColumn("Files").AddColumn("Size");

            foreach (var model in models) {
                var revision = model.Revision.Length > 8 ? model.Revision[..8] : model.Revision;
                table.AddRow(Markup.Escape(model.Repo), Markup.Escape(revision), model.FileCount.ToString(), model.TotalBytes.Humanize());
            }

            console.Write(table);

            return Task.FromResult(0);
        }));

        var yesOption = new Option<bool>("--yes") {
            Description = "Skip the confirmation prompt — required in non-interactive contexts (CI)."
        };

        var clearCommand = new Command("clear", "Deletes every cached model.") { cacheDirOption, yesOption };

        clearCommand.SetAction((result, _) => errorConsole.ExecuteAsync(() => {
            var hub    = new HuggingFaceHub(new HubOptions { CacheDirectory = result.GetValue(cacheDirOption) });
            var models = hub.ListCachedModels();

            if (models.Count == 0) {
                console.MarkupLineInterpolated($"No cached models under [bold]{Markup.Escape(hub.CacheDirectory)}[/]. Nothing to do.");
                return Task.FromResult(0);
            }

            // One group per distinct model — clear removes every cached revision of a model in one shot (the
            // models--* directory each CachedModel.Path was found under), never a bare snapshot subdirectory
            // and never anything else under the cache root.
            var byDirectory = models
                .GroupBy(m => Directory.GetParent(Directory.GetParent(m.Path)!.FullName)!.FullName)
                .ToList();

            var totalBytes = models.Sum(m => m.TotalBytes);

            if (!result.GetValue(yesOption)) {
                if (!console.Profile.Capabilities.Interactive) {
                    errorConsole.MarkupLineInterpolated(
                        $"[red]Error:[/] Refusing to delete {byDirectory.Count} model(s) ({totalBytes.Humanize()}) from [bold]{Markup.Escape(hub.CacheDirectory)}[/] without confirmation. Pass --yes.");
                    return Task.FromResult(1);
                }

                var confirmed = console.Confirm(
                    $"Delete {byDirectory.Count} model(s) ({totalBytes.Humanize()}) from [bold]{Markup.Escape(hub.CacheDirectory)}[/]?",
                    defaultValue: false);

                if (!confirmed) {
                    console.MarkupLine("Cancelled — nothing deleted.");
                    return Task.FromResult(0);
                }
            }

            var (removedCount, freedBytes, failures) = DeleteModelDirectories(byDirectory);

            if (failures.Count > 0) {
                ReportFailures(errorConsole, failures);

                console.MarkupLineInterpolated($"Removed {removedCount} of {byDirectory.Count} model(s), freed {freedBytes.Humanize()}.");

                return Task.FromResult(1);
            }

            console.MarkupLineInterpolated($"Removed {removedCount} model(s), freed {freedBytes.Humanize()}.");

            return Task.FromResult(0);
        }));

        var cacheCommand = new Command("cache", "Cache-related commands. Run with no subcommand to print the resolved cache root.") {
            cacheDirOption, listCommand, clearCommand
        };

        // Bare `cache` invocation prints the resolved root — same behavior the old `cache path` subcommand
        // had. Bypasses IAnsiConsole entirely — see InspectCommand.WriteJson for why.
        cacheCommand.SetAction((result, _) => errorConsole.ExecuteAsync(() => {
            var hub = new HuggingFaceHub(new HubOptions { CacheDirectory = result.GetValue(cacheDirOption) });
            output.WriteLine(hub.CacheDirectory);
            return Task.FromResult(0);
        }));

        return cacheCommand;
    }

    // Shared by `cache clear` and `models clear <repo>` (ModelsCommand): deletes each grouped model directory
    // independently so one locked model never blocks freeing the rest, recording (not throwing)
    // IOException/UnauthorizedAccessException. Callers own their own success/failure messages — this returns
    // only the raw counts. Deletion of cache model directories is cache-domain machinery, so it lives here.
    internal static (int RemovedCount, long FreedBytes, List<(string Directory, string Message)> Failures) DeleteModelDirectories(
        IEnumerable<IGrouping<string, CachedModel>> byDirectory
    ) {
        List<(string Directory, string Message)> failures = [];
        var  removedCount = 0;
        long freedBytes   = 0;

        foreach (var group in byDirectory) {
            try {
                Directory.Delete(group.Key, recursive: true);
                removedCount++;
                freedBytes += group.Sum(m => m.TotalBytes);
            } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
                failures.Add((group.Key, ex.Message));
            }
        }

        return (removedCount, freedBytes, failures);
    }

    // Renders each DeleteModelDirectories failure as a red error line. Shared with ModelsCommand for the
    // same reason as DeleteModelDirectories above.
    internal static void ReportFailures(IAnsiConsole errorConsole, List<(string Directory, string Message)> failures) {
        foreach (var (directory, message) in failures)
            errorConsole.MarkupLineInterpolated($"[red]Error:[/] Could not delete [bold]{Markup.Escape(directory)}[/]: {Markup.Escape(message)}");
    }
}

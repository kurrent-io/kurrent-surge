namespace Kurrent.Surge.Cli;

static class CacheCommand {
    public static Command Build(IAnsiConsole console, IAnsiConsole errorConsole, TextWriter output, Option<string?> cacheDirOption) {
        var listCommand = new Command("list", "Lists models cached locally.") { cacheDirOption };

        listCommand.SetAction((result, _) => CliHelpers.ExecuteAsync(errorConsole, () => {
            var hub    = new HuggingFaceHub(new HubOptions { CacheDirectory = result.GetValue(cacheDirOption) });
            var models = hub.ListCachedModels();

            if (models.Count == 0) {
                console.MarkupLineInterpolated($"No cached models under [bold]{Markup.Escape(hub.CacheDirectory)}[/].");
                return Task.FromResult(0);
            }

            console.MarkupLineInterpolated(
                $"[bold]{Markup.Escape(hub.CacheDirectory)}[/] — {models.Count} snapshot(s), {CliHelpers.Humanize(models.Sum(m => m.TotalBytes))}");

            var table = new Table().AddColumn("Model").AddColumn("Revision").AddColumn("Files").AddColumn("Size");

            foreach (var model in models) {
                var revision = model.Revision.Length > 8 ? model.Revision[..8] : model.Revision;
                table.AddRow(Markup.Escape(model.Repo), Markup.Escape(revision), model.FileCount.ToString(), CliHelpers.Humanize(model.TotalBytes));
            }

            console.Write(table);

            return Task.FromResult(0);
        }));

        var yesOption = new Option<bool>("--yes") {
            Description = "Skip the confirmation prompt — required in non-interactive contexts (CI)."
        };

        var clearCommand = new Command("clear", "Deletes every cached model.") { cacheDirOption, yesOption };

        clearCommand.SetAction((result, _) => CliHelpers.ExecuteAsync(errorConsole, () => {
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
                        $"[red]Error:[/] Refusing to delete {byDirectory.Count} model(s) ({CliHelpers.Humanize(totalBytes)}) from [bold]{Markup.Escape(hub.CacheDirectory)}[/] without confirmation. Pass --yes.");
                    return Task.FromResult(1);
                }

                var confirmed = console.Confirm(
                    $"Delete {byDirectory.Count} model(s) ({CliHelpers.Humanize(totalBytes)}) from [bold]{Markup.Escape(hub.CacheDirectory)}[/]?",
                    defaultValue: false);

                if (!confirmed) {
                    console.MarkupLine("Cancelled — nothing deleted.");
                    return Task.FromResult(0);
                }
            }

            var (removedCount, freedBytes, failures) = CliHelpers.DeleteModelDirectories(byDirectory);

            if (failures.Count > 0) {
                CliHelpers.ReportFailures(errorConsole, failures);

                console.MarkupLineInterpolated($"Removed {removedCount} of {byDirectory.Count} model(s), freed {CliHelpers.Humanize(freedBytes)}.");

                return Task.FromResult(1);
            }

            console.MarkupLineInterpolated($"Removed {removedCount} model(s), freed {CliHelpers.Humanize(freedBytes)}.");

            return Task.FromResult(0);
        }));

        var cacheCommand = new Command("cache", "Cache-related commands. Run with no subcommand to print the resolved cache root.") {
            cacheDirOption, listCommand, clearCommand
        };

        // Bare `cache` invocation prints the resolved root — same behavior the old `cache path` subcommand
        // had. Bypasses IAnsiConsole entirely — see InspectCommand.WriteJson for why.
        cacheCommand.SetAction((result, _) => CliHelpers.ExecuteAsync(errorConsole, () => {
            var hub = new HuggingFaceHub(new HubOptions { CacheDirectory = result.GetValue(cacheDirOption) });
            output.WriteLine(hub.CacheDirectory);
            return Task.FromResult(0);
        }));

        return cacheCommand;
    }
}

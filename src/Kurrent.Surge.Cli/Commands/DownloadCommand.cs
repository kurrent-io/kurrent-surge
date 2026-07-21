using System.Diagnostics;

namespace Kurrent.Surge.Cli;

static class DownloadCommand {
    public static Command Build(IAnsiConsole console, IAnsiConsole errorConsole, Option<string?> tokenOption, Option<string?> cacheDirOption) {
        var urlArgument = new Argument<string>("url") {
            Description = "A Hugging Face model-file URL (…/blob/ or …/resolve/)."
        };

        var forceOption = new Option<bool>("--force") {
            Description = "Re-download every file even when a valid cached copy exists."
        };

        var quietOption = new Option<bool>("--quiet") {
            Description = "Suppress the progress display; print only a final summary."
        };

        var command = new Command("download", "Inspects a model and downloads its planned bundle.") {
            urlArgument, forceOption, quietOption, tokenOption, cacheDirOption
        };

        command.SetAction((result, ct) => CliHelpers.ExecuteAsync(errorConsole, async () => {
            var force = result.GetValue(forceOption);
            var quiet = result.GetValue(quietOption);

            var hub = new HuggingFaceHub(
                new HubOptions { Token = result.GetValue(tokenOption), CacheDirectory = result.GetValue(cacheDirOption) });

            var bundle = await hub.InspectModelAsync(ModelRef.From(result.GetValue(urlArgument)!), ct).ConfigureAwait(false);

            var stopwatch = Stopwatch.StartNew();

            if (quiet) {
                await bundle.DownloadAsync(force: force, ct: ct).ConfigureAwait(false);
            } else {
                await console.Progress().StartAsync(async progressContext => {
                    var tracker = new SpectreProgressTracker(progressContext);
                    await bundle.DownloadAsync(tracker, force, ct).ConfigureAwait(false);
                }).ConfigureAwait(false);
            }

            stopwatch.Stop();

            console.MarkupLineInterpolated(
                $"Downloaded {bundle.Files.Count} files, {CliHelpers.Humanize(bundle.TotalBytes)}, in {stopwatch.Elapsed.TotalSeconds:0.#}s");

            return 0;
        }));

        return command;
    }
}

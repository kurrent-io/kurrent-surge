namespace Kurrent.Surge.Cli;

/// <summary>
/// Builds the <c>surge</c> root command tree. Each vertical registers as one group <see cref="Command"/> —
/// today just <c>huggingface</c> — assembled from plain, composable <see cref="Command"/> objects, so the
/// command implementations themselves never change when a vertical is lifted into (or added to) the umbrella.
/// </summary>
public static class CliApp {
    /// <summary>Builds a fresh command tree, wired against the given consoles (real by default, or a test double).</summary>
    /// <param name="console">Sink for decorative output (tables, progress); defaults to the real <see cref="AnsiConsole.Console"/>.</param>
    /// <param name="errorConsole">Sink for error output; defaults to a console bound to standard error.</param>
    /// <param name="output">
    /// Sink for machine-consumable output (<c>huggingface models inspect --json</c>, bare <c>huggingface cache</c>) —
    /// a raw <see cref="TextWriter"/>, deliberately bypassing <see cref="IAnsiConsole"/>: Spectre wraps text at
    /// the console's <c>Profile.Width</c> even for non-markup content, which corrupts a single-value payload
    /// in a narrow terminal. Defaults to the real <see cref="Console.Out"/>.
    /// </param>
    public static RootCommand Build(IAnsiConsole? console = null, IAnsiConsole? errorConsole = null, TextWriter? output = null) {
        console      ??= AnsiConsole.Console;
        errorConsole ??= AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(Console.Error) });
        output       ??= Console.Out;

        var tokenOption = new Option<string?>("--token") {
            Description = "Hugging Face access token; defaults to the HF_TOKEN environment variable."
        };

        var cacheDirOption = new Option<string?>("--cache-dir") {
            Description = "Local cache root; defaults to HF_HUB_CACHE, else ~/.cache/huggingface/hub."
        };

        var huggingFaceCommand = new Command("huggingface", "Hugging Face model acquisition.") {
            ModelsCommand.Build(console, errorConsole, output, tokenOption, cacheDirOption),
            CacheCommand.Build(console, errorConsole, output, cacheDirOption),
            ProbeCommand.Build(console, errorConsole)
        };

        return new RootCommand("surge — Kurrent Surge command-line") {
            huggingFaceCommand
        };
    }
}

using Kurrent.Surge.Cli;

return await CliApp.Build().Parse(args).InvokeAsync();

static class CliApp {
    public static RootCommand Build(IAnsiConsole? console = null, IAnsiConsole? errorConsole = null, TextWriter? output = null) {
        console      ??= AnsiConsole.Console;
        errorConsole ??= AnsiConsole.Create(new() { Out = new AnsiConsoleOutput(Console.Error) });
        output       ??= Console.Out;

        return new("Kurrent Surge tooling. Acquire Hugging Face models, manage the local model cache, and probe host hardware.") {
            HuggingFaceCommand.Build(console, errorConsole, output)
        };
    }
}

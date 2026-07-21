namespace Kurrent.Surge.Cli;

static class HuggingFaceCommand {
    public static Command Build(IAnsiConsole console, IAnsiConsole errorConsole, TextWriter output) {
        var tokenOption = new Option<string?>("--token") {
            Description = "Hugging Face access token; defaults to the HF_TOKEN environment variable."
        };

        var cacheDirOption = new Option<string?>("--cache-dir") {
            Description = "Local cache root; defaults to HF_HUB_CACHE, else ~/.cache/huggingface/hub."
        };

        return new Command("huggingface", "Hugging Face model acquisition.") {
            ModelsCommand.Build(console, errorConsole, output, tokenOption, cacheDirOption),
            CacheCommand.Build(console, errorConsole, output, cacheDirOption),
            ProbeCommand.Build(console, errorConsole)
        };
    }
}

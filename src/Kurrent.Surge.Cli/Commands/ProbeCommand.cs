namespace Kurrent.Surge.Cli;

static class ProbeCommand {
    public static Command Build(IAnsiConsole console, IAnsiConsole errorConsole) {
        var command = new Command("probe", "Prints the CPU arch tags this host supports, best-first.");

        command.SetAction((_, _) => CliHelpers.ExecuteAsync(errorConsole, () => {
            var tags = HardwareProbe.PreferredArchTags();

            if (tags.Count == 0) {
                console.MarkupLine("[yellow]No supported arch tags detected for this host.[/]");
                return Task.FromResult(0);
            }

            var table = new Table().AddColumn("Arch tag (best-first)");

            foreach (var tag in tags)
                table.AddRow(Markup.Escape(tag));

            console.Write(table);

            return Task.FromResult(0);
        }));

        return command;
    }
}

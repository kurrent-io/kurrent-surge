namespace Kurrent.Surge.Cli.Infrastructure;

static class AnsiConsoleExtensions {
    extension(IAnsiConsole errorConsole) {
        /// <summary>Runs <paramref name="action"/>, turning a <see cref="ModelException"/> or
        /// <see cref="HttpRequestException"/> into a red error line on this console and exit code 1 instead
        /// of letting it propagate.</summary>
        public async Task<int> ExecuteAsync(Func<Task<int>> action) {
            try {
                return await action().ConfigureAwait(false);
            } catch (Exception ex) when (ex is ModelException or HttpRequestException) {
                errorConsole.MarkupLineInterpolated($"[red]Error:[/] {ex.Message}");
                return 1;
            }
        }
    }
}

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Kurrent.Surge.Generators.Validation;

readonly record struct DiagnosticModel(
    string Id, string Title, string Message, bool IsError,
    string FilePath, int Start, int Length, LinePositionSpan Lines
) {
    public Diagnostic ToDiagnostic() {
        var descriptor = new DiagnosticDescriptor(
            Id, Title, Message, "Kurrent.Validation",
            IsError ? DiagnosticSeverity.Error : DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        // The line span must be carried alongside the character span: Location.Create builds no
        // line map of its own, so a default LinePositionSpan puts every squiggle on line 1.
        var location = FilePath.Length == 0
            ? Location.None
            : Location.Create(FilePath, new TextSpan(Start, Length), Lines);

        return Diagnostic.Create(descriptor, location);
    }
}

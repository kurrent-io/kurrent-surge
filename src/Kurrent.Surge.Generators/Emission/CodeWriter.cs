using System;
using System.Text;

namespace Kurrent.Surge.Codecs.Avro.Generators.Emission;

sealed class CodeWriter {
    readonly StringBuilder _builder = new();

    int _indent;

    public void Line(string text = "") {
        if (text.Length > 0) _builder.Append(' ', _indent * 4).Append(text);

        _builder.AppendLine();
    }

    /// <summary>Writes a multi-line template, re-indenting every line to the current depth.</summary>
    public void Lines(string text) {
        foreach (var line in text.Split('\n')) Line(line.TrimEnd('\r'));
    }

    public IDisposable Block(string header) => Block(header, "}");

    /// <param name="closing">
    /// What follows the block, for the cases where a brace alone does not close the statement — a switch
    /// expression body needs <c>};</c>.
    /// </param>
    public IDisposable Block(string header, string closing) {
        Line(header);
        Line("{");
        _indent++;

        return new Scope(this, closing);
    }

    public override string ToString() => _builder.ToString();

    sealed class Scope(CodeWriter writer, string closing) : IDisposable {
        public void Dispose() {
            writer._indent--;
            writer.Line(closing);
        }
    }
}

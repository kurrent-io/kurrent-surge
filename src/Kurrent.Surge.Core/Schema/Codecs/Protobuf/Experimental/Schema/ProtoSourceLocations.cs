using Google.Protobuf.Reflection;

namespace Kurrent.Surge.Codecs.Protobuf.Experimental.Schema;

/// <summary>
/// Builds the <see cref="SourceCodeInfo"/> a parsed file carries, which is the only place a
/// descriptor can hold a comment.
/// </summary>
/// <remarks>
/// A location is recorded only where a declaration is commented. <c>protoc</c> emits one for every
/// element; the descriptor builder throws on a location whose span is malformed, so each span emitted
/// is another way to fail a legal schema, and a span on an uncommented field carries nothing a reader
/// consumes. See https://protobuf.dev/reference/protobuf/google.protobuf/#source-code-info.
/// </remarks>
sealed class ProtoSourceLocations {
    readonly SourceCodeInfo _info = new();
    readonly List<int>      _path = [];

    /// <summary>Descends into entry <paramref name="index"/> of the descriptor field numbered <paramref name="field"/>.</summary>
    /// <remarks>
    /// The numbers are the descriptor's own field numbers — 4 is <c>FileDescriptorProto.message_type</c>,
    /// 2 is <c>DescriptorProto.field</c> — so a path names an element without naming a type.
    /// </remarks>
    public void Enter(int field, int index) {
        _path.Add(field);
        _path.Add(index);
    }

    public void Exit() => _path.RemoveRange(_path.Count - 2, 2);

    /// <summary>Records the comments on the declaration currently entered.</summary>
    public void Record(Token start, Token end, string? trailing) {
        if (start.Leading is null && trailing is null) return;

        var location = new SourceCodeInfo.Types.Location();

        location.Path.AddRange(_path);

        // A span counts from zero where the lexer counts from one, and the end column already sits
        // one past the last character, so it needs the same shift as every other coordinate.
        location.Span.Add(start.Line - 1);
        location.Span.Add(start.Column - 1);
        location.Span.Add(end.EndLine - 1);
        location.Span.Add(end.EndColumn - 1);

        if (start.Leading is not null)
            foreach (var comment in start.Leading) {
                if (comment.Detached) location.LeadingDetachedComments.Add(comment.Text);
                else location.LeadingComments = comment.Text;
            }

        if (trailing is not null) location.TrailingComments = trailing;

        _info.Location.Add(location);
    }

    public SourceCodeInfo? ToSourceCodeInfo() => _info.Location.Count > 0 ? _info : null;
}

using System.Text;

using Kurrent.Surge.Client.Exceptions;

namespace Kurrent.Projectors.Transactional;

/// <summary>
/// Storage for the documents a <see cref="DocumentProjection{TClient,TDocument,TKey}"/> folds. One store
/// serves every document type — the type travels on the call, not on the interface — so a backend
/// implements this once and every projection over it shares the implementation.
/// </summary>
/// <remarks>
/// <para>
/// An implementation must write through the client of the transaction its projection currently has open.
/// A store that opens a connection of its own writes outside the transaction: a rollback leaves those
/// writes behind and the checkpoint commits as if they belonged there.
/// </para>
/// <para>
/// Every write carries the version it expects to find and the version it becomes. A write whose expected
/// version is not the version in storage must fail rather than overwrite, so two writers over one
/// collection cannot silently lose each other's work. Version <c>0</c> means "no row yet".
/// </para>
/// <para>
/// Every operation takes a batch. One key is a batch of one, so a store implements three statements and
/// never a per-key round trip.
/// </para>
/// </remarks>
[PublicAPI]
public interface IDocumentStore {
    /// <summary>
    /// Reads the requested documents.
    /// </summary>
    /// <typeparam name="TDocument">The document type, which selects the collection to read from.</typeparam>
    /// <typeparam name="TKey">The document's key.</typeparam>
    /// <param name="request">The keys to read.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>
    /// Every requested document that exists, streamed as storage yields it. A key with no row yields
    /// nothing, so a caller that needs to know which keys were absent compares what arrived against what
    /// it asked for.
    /// </returns>
    IAsyncEnumerable<StoredDocument<TDocument, TKey>> Load<TDocument, TKey>(
        LoadRequest<TKey> request, CancellationToken cancellationToken)
        where TDocument : class
        where TKey : notnull;

    /// <summary>
    /// Applies every change in one round trip, each guarded on its own expected version. An entry whose
    /// document is <see langword="null"/> removes the row.
    /// </summary>
    /// <typeparam name="TDocument">The document type, which selects the collection to write to.</typeparam>
    /// <typeparam name="TKey">The document's key.</typeparam>
    /// <param name="request">The changes to apply.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <exception cref="DocumentVersionMismatchException">
    /// An entry's expected version is not the version in storage.
    /// </exception>
    /// <remarks>
    /// All or nothing: one entry whose expected version does not match must abort the whole request, or a
    /// caller cannot tell which half of its changeset landed.
    /// </remarks>
    ValueTask Save<TDocument, TKey>(SaveRequest<TDocument, TKey> request, CancellationToken cancellationToken)
        where TDocument : class
        where TKey : notnull;
}

/// <summary>A document as storage holds it.</summary>
/// <typeparam name="TDocument">The document type.</typeparam>
/// <typeparam name="TKey">The document's key.</typeparam>
/// <param name="Key">Identifies the row. Carried because a streamed read cannot be matched back by position.</param>
/// <param name="Document">The document.</param>
/// <param name="Version">How many times it has changed. Guards the next write.</param>
/// <param name="Timestamp">When storage last wrote it.</param>
[PublicAPI]
public readonly record struct StoredDocument<TDocument, TKey>(
    TKey           Key,
    TDocument      Document,
    long           Version,
    DateTimeOffset Timestamp
) where TDocument : class
  where TKey : notnull;

/// <summary>The documents to read.</summary>
/// <typeparam name="TKey">The document's key.</typeparam>
/// <param name="Keys">The keys to read. One key is a batch of one.</param>
[PublicAPI]
public readonly record struct LoadRequest<TKey>(TKey[] Keys)
    where TKey : notnull;

/// <summary>Every change to apply, in one round trip.</summary>
/// <typeparam name="TDocument">The document type.</typeparam>
/// <typeparam name="TKey">The document's key.</typeparam>
/// <param name="Tracked">
/// One entry per key. <c>ExpectedVersion</c> is what storage must currently hold, <c>0</c> expecting no row.
/// A null <c>Document</c> removes the row, and <c>Version</c> is then unused — the row is going away.
/// </param>
[PublicAPI]
public readonly record struct SaveRequest<TDocument, TKey>(
    (
        TKey Key,
        long ExpectedVersion,
        long Version,
        TDocument? Document
        )[] Tracked
)
    where TDocument : class
    where TKey : notnull;

/// <summary>
/// Where a store keeps documents of a given type. Derived from the type name so a projection never has to
/// name it: <c>SchemaVersion</c> becomes <c>schema_version</c>.
/// </summary>
[PublicAPI]
public static class DocumentCollection {
    /// <summary>Gets the collection name for <typeparamref name="TDocument"/>.</summary>
    /// <typeparam name="TDocument">The document type.</typeparam>
    /// <returns>The type name in lower snake case — <c>SchemaVersion</c> becomes <c>schema_version</c>.</returns>
    public static string For<TDocument>() => Cache<TDocument>.Name;

    // A static field on a generic type is the per-type cache: computed once per TDocument by the runtime,
    // with no dictionary and no lock.
    static class Cache<TDocument> {
        public static readonly string Name = SnakeCase(typeof(TDocument).Name);
    }

    static string SnakeCase(string value) {
        // A generic type's name carries its arity — List`1. Documents are not generic, but a name that
        // reaches SQL must not depend on that.
        var backtick = value.IndexOf('`');
        var name     = backtick < 0 ? value : value[..backtick];
        var builder  = new StringBuilder(name.Length + 8);

        for (var index = 0; index < name.Length; index++) {
            var character = name[index];

            // Break before a capital that starts a word: either the previous character was lower, or this
            // is the last capital of a run — so HTTPRequest reads http_request, not h_t_t_p_request.
            if (index > 0 && char.IsUpper(character)
             && (!char.IsUpper(name[index - 1]) || (index + 1 < name.Length && char.IsLower(name[index + 1]))))
                builder.Append('_');

            builder.Append(char.ToLowerInvariant(character));
        }

        return builder.ToString();
    }
}

/// <summary>
/// Thrown when a guarded write met a version other than the one it expected — a concurrent writer, or a
/// replay over rows that have already moved on.
/// </summary>
[PublicAPI]
public sealed class DocumentVersionMismatchException(string reason, Exception? innerException = null)
    : KurrentException(ErrorCode.From(nameof(DocumentVersionMismatchException)), reason, innerException);
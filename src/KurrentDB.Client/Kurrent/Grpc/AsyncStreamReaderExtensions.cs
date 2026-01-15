using System.Runtime.CompilerServices;
using Grpc.Core;

namespace Kurrent.Grpc;

public static class AsyncStreamReaderExtensions {
	extension<T>(IAsyncStreamReader<T> reader) {
        public async IAsyncEnumerable<T> ReadAllAsync([EnumeratorCancellation] CancellationToken cancellationToken = default) {
            while (await reader.MoveNext(cancellationToken).ConfigureAwait(false))
                yield return reader.Current;
        }

        public ValueTask<T?> FirstOrDefaultAsync(CancellationToken cancellationToken = default) =>
            reader.ReadAllAsync(cancellationToken).FirstOrDefaultAsync(cancellationToken);

        public ValueTask<List<T>> ToListAsync(CancellationToken cancellationToken = default) =>
            reader.ReadAllAsync(cancellationToken).ToListAsync(cancellationToken);

        public ValueTask<int> CountAsync(CancellationToken cancellationToken = default) =>
            reader.ReadAllAsync(cancellationToken).CountAsync(cancellationToken);

        public ValueTask<bool> AnyAsync(CancellationToken cancellationToken = default) =>
            reader.ReadAllAsync(cancellationToken).AnyAsync(cancellationToken);
    }
}

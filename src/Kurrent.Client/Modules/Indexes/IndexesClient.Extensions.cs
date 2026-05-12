namespace Kurrent.Client.Indexes;

public static partial class IndexesClientExtensions {
    extension(IndexesClient client) {
        #region ->> Create <<-

        public ValueTask Create(
            string name, string filter, IEnumerable<IndexField> fields,
            CancellationToken cancellationToken = default
        ) => client.Create(
            CreateIndexRequest.For(name)
                .WithFilter(filter)
                .WithFields(fields)
                .Build(),
            cancellationToken);

        public ValueTask Create(
            string name, string filter, IEnumerable<IndexField> fields, bool start,
            CancellationToken cancellationToken = default
        ) => client.Create(
            CreateIndexRequest.For(name)
                .WithFilter(filter)
                .WithFields(fields)
                .WithStart(start)
                .Build(),
            cancellationToken);

        #endregion
    }
}

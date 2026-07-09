namespace Kurrent.Surge.Client.Indexes;

public static partial class IndexesClientExtensions {
    extension(IIndexesClient client) {
        #region ->> Create <<-

        public ValueTask Create(
            string name, string filter, IEnumerable<IndexField> fields,
            CancellationToken cancellationToken = default
        ) => client.Create(
            name,
            r => {
                r.Filter = filter;
                r.Fields = fields.ToList();
            },
            cancellationToken);

        #endregion
    }
}

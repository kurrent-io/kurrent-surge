namespace Kurrent.Client.Streams;

static class RecordFilterExtensions {
    extension(RecordFilter source) {
        /// <summary>
        /// Unwraps an <see cref="ExpressionFilter"/> to its server-resolvable inner filter.
        /// Non-Expression filters return themselves. Used by the V1 wire mapper to route
        /// requests (stream vs $all) and to dispatch <c>FilterOptions</c> (Prefix vs Regex)
        /// against the concrete server filter type rather than the expression wrapper.
        /// </summary>
        public RecordFilter UnwrapFilter() =>
            source is ExpressionFilter ef ? ef.ServerFilter : source;
    }
}

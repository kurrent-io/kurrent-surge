namespace Kurrent.Client;

static class ArrayExtensions {
    /// <param name="array">The source array to copy from.</param>
    /// <typeparam name="T">The type of elements in the array.</typeparam>
    extension<T>(T[] array) {
        /// <summary>
        /// Copies the first <paramref name="count"/> elements to a new array.
        /// Uses Span.CopyTo which leverages Buffer.Memmove for optimal performance.
        /// </summary>
        /// <param name="count">The number of elements to copy from the start of the array.</param>
        /// <returns>A new array containing the copied elements.</returns>
        public T[] CopyToArray(int count) {
            var result = new T[count];
            array.AsSpan(0, count).CopyTo(result);
            return result;
        }
    }
}

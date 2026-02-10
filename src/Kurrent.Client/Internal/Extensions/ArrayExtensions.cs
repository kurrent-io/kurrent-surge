namespace Kurrent.Client;

static class ArrayExtensions {
    /// <param name="source">The source array to copy from.</param>
    /// <typeparam name="T">The type of elements in the array.</typeparam>
    extension<T>(T[] source) {
        /// <summary>
        /// Copies the first <paramref name="count"/> elements to a new array.
        /// Uses Span.CopyTo which leverages Buffer.Memmove for optimal performance.
        /// </summary>
        /// <param name="count">The number of elements to copy from the start of the array.</param>
        /// <returns>A new array containing the copied elements.</returns>
        public T[] CopyToArray(int count) {
            //TODO SS: does this slow us down significantly? do we need an unsafe version without checks?
            if (count < 0 || count > source.Length)
                throw new ArgumentOutOfRangeException(nameof(count), "Count must be non-negative and less than or equal to the length of the array.");

            if (count == 0) return [];

            var result = new T[count];
            source.AsSpan(0, count).CopyTo(result);
            return result;
        }
    }
}

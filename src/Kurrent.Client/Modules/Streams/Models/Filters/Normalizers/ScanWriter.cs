namespace Kurrent.Client.Streams;

#if NET9_0_OR_GREATER

/// <summary>
/// Stack-only output cursor that writes normalized query text into a caller-supplied buffer.
/// Tracks a monotonically advancing write position so successive <see cref="Copy"/> and
/// <see cref="CopySlice"/> calls append contiguously. No bounds checking is performed;
/// the caller must ensure the buffer is large enough (at minimum, the length of the input query).
/// </summary>
ref struct ScanWriter(Span<char> buffer) {
    readonly Span<char> _buffer = buffer;

    /// <summary>
    /// Number of characters written so far.
    /// </summary>
    public int Written { get; private set; } = 0;

    /// <summary>
    /// Appends a single character and advances the write position.
    /// </summary>
    public void Copy(char c) => _buffer[Written++] = c;

    /// <summary>
    /// Appends a contiguous span of characters and advances the write position.
    /// </summary>
    public void CopySlice(ReadOnlySpan<char> slice) {
        slice.CopyTo(_buffer[Written..]);
        Written += slice.Length;
    }

    /// <summary>
    /// Returns the portion of the buffer that has been written to.
    /// </summary>
    public ReadOnlySpan<char> Result => _buffer[..Written];
}

#endif

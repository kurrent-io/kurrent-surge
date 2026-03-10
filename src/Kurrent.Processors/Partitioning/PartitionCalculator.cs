using System.Buffers;
using System.Text;

namespace Kurrent.Client.Processors.Partitioning;

/// <summary>
/// Calculates which partition a key belongs to using a deterministic hash function.
/// </summary>
/// <remarks>
/// <para>
/// Partitions provide a fixed intermediate layer between keys and workers. Keys are mapped
/// to partitions deterministically, ensuring the same key always routes to the same partition.
/// </para>
/// <para>
/// <b>Thread Safety:</b> This class is thread-safe. All methods are pure functions with no shared state.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var calculator = PartitionCalculator.Default;
///
/// // Calculate partition for keys (0 to maxParallelism-1)
/// int partition1 = calculator.For("customer-123", maxParallelism: 128);
/// int partition2 = calculator.For("order-456", maxParallelism: 128);
///
/// // Same key always maps to same partition
/// Debug.Assert(calculator.For("customer-123", 128) == partition1);
/// </code>
/// </example>
[PublicAPI]
public sealed class PartitionCalculator(GenerateHashFromBytes hashGenerator) {
    /// <summary>
    /// Stack allocation threshold - keys shorter than this use stackalloc (zero allocation).
    /// </summary>
    const int StackAllocThreshold = 256;

    /// <summary>
    /// Default instance using XXHash (fastest with excellent distribution).
    /// </summary>
    public static readonly PartitionCalculator XXHash = new(HashGenerators.FromBytes.XXHash);

    /// <summary>
    /// Instance using MurmurHash3 (industry standard used by Kafka, Flink, Cassandra).
    /// </summary>
    public static readonly PartitionCalculator MurmurHash3 = new(HashGenerators.FromBytes.MurmurHash3);

    /// <summary>
    /// Calculates the partition number for a given key.
    /// </summary>
    /// <param name="key">The partition key (e.g., stream ID, customer ID).</param>
    /// <param name="maxParallelism">The total number of partitions (1 to 32768).</param>
    /// <returns>A partition number in the range [0, maxParallelism).</returns>
    /// <remarks>
    /// <para>
    /// The same key always maps to the same partition for a given maxParallelism.
    /// This ensures state locality: all records with the same key are processed by the same worker.
    /// </para>
    /// </remarks>
    public int Calculate(string key, int maxParallelism) {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxParallelism, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxParallelism, 32768);

        var hash = ComputeHash(key, hashGenerator);

        return (int)(hash % maxParallelism);

        static uint ComputeHash(string key, GenerateHashFromBytes hashGenerator) {
            var maxByteCount = Encoding.UTF8.GetMaxByteCount(key.Length);

            // Fast path: stackalloc for small keys (zero allocation)
            if (maxByteCount <= StackAllocThreshold) {
                Span<byte> buffer = stackalloc byte[maxByteCount];
                var byteCount = Encoding.UTF8.GetBytes(key, buffer);
                return hashGenerator(buffer[..byteCount]);
            }

            // Slow path: rent from pool for large keys
            var rentedBuffer = ArrayPool<byte>.Shared.Rent(maxByteCount);
            try {
                var byteCount = Encoding.UTF8.GetBytes(key, rentedBuffer);
                return hashGenerator(rentedBuffer.AsSpan(0, byteCount));
            }
            finally {
                ArrayPool<byte>.Shared.Return(rentedBuffer);
            }
        }
    }

    /// <summary>
    /// Calculates the partition number for a given key (UTF-8 bytes).
    /// </summary>
    /// <param name="keyBytes">The partition key as UTF-8 bytes.</param>
    /// <param name="maxParallelism">The total number of partitions (1 to 32768).</param>
    /// <returns>A partition number in the range [0, maxParallelism).</returns>
    public int Calculate(ReadOnlySpan<byte> keyBytes, int maxParallelism) {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxParallelism, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxParallelism, 32768);

        var hash = hashGenerator(keyBytes);
        return (int)(hash % maxParallelism);
    }
}

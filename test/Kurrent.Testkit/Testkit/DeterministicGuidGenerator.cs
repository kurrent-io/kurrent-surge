
using System.Security.Cryptography;

namespace Kurrent.Testkit;

/// <summary>
/// <para>
/// Generates deterministic GUIDs based on a namespace and input.
///</para><para>
/// This is useful for generating GUIDs from a source identifier whereby if the identifier is regenerated
/// from the same input, the same GUID will be generated. This is essential to support idempotent handling.
/// </para>
/// <para>This object lifecycle should be singleton with respect to lifetime of the hosting application.</para>
/// </summary>
class DeterministicGuidGenerator {
	/// <summary>
	/// Creates a new instance of <see cref="DeterministicGuidGenerator"/>.
	/// </summary>
	/// <param name="guidNamespace">
	/// A namespace that ensure uniqueness across applications / services. An application
	/// should supply a namespace value that is long term stable.
	/// </param>
	public DeterministicGuidGenerator(Guid guidNamespace) {
        NamespaceBytes = guidNamespace.ToByteArray();
        // NamespaceBytes = new byte[16];
        // guidNamespace.TryWriteBytes(NamespaceBytes);
        SwapByteOrder(NamespaceBytes);
    }

    byte[] NamespaceBytes { get; }

	/// <summary>
	/// Generates a deterministic GUID based on the namespace and input.
	/// </summary>
	/// <param name="input">
	/// The input to generate the GUID from.
	/// </param>
	/// <returns>
	/// A deterministic GUID.
	/// </returns>
	public Guid Generate(byte[] input) {
        Span<byte> newGuid;

		using (var algorithm = SHA1.Create()) {
			_ = algorithm.TransformBlock(NamespaceBytes, 0, NamespaceBytes.Length, null, 0);
			_ = algorithm.TransformFinalBlock(input, 0, input.Length);
            newGuid = algorithm.Hash!.AsSpan(0, 16);
		}

        // Set the version to 5 (UUIDv5)
		newGuid[6] = (byte)((newGuid[6] & 0x0F) | 0x50);

        // Set the variant to RFC 4122
		newGuid[8] = (byte)((newGuid[8] & 0x3F) | 0x80);

        // This converts little-endian to big-endian for specific UUID fields.
        SwapByteOrder(newGuid);

		return new(newGuid);
	}

    static void SwapByteOrder(Span<byte> guid) {
        SwapBytes(guid, 0, 3); // Swap bytes in time_low
        SwapBytes(guid, 1, 2);
        SwapBytes(guid, 4, 5); // Swap bytes in time_mid
        SwapBytes(guid, 6, 7); // Swap bytes in time_hi_and_version
    }

    static void SwapBytes(Span<byte> guid, int a, int b) =>
        (guid[a], guid[b]) = (guid[b], guid[a]);

    static void SwapBytesXor(Span<byte> guid, int a, int b) {
        guid[a] ^= guid[b];
        guid[b] ^= guid[a];
        guid[a] ^= guid[b];
    }
}

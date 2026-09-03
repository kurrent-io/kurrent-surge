using Kurrent.Surge.Client.Streams;
using Kurrent.Surge.Client.Legacy;

namespace Kurrent.Surge.Tests.Legacy;

[Category("Legacy")]
public class UuidTests {
	[Test]
	public async ValueTask to_guid_returns_expected_result() {
		var guid = Guid.NewGuid();
		var sut  = Uuid.FromGuid(guid);

		await Assert.That(sut.ToGuid()).IsEqualTo(guid);
	}

	[Test]
	public async ValueTask to_string_produces_expected_result() {
		var sut = Uuid.NewUuid();

		await Assert.That(sut.ToString()).IsEqualTo(sut.ToGuid().ToString());
	}

	[Test]
	public async ValueTask to_formatted_string_produces_expected_result() {
		var sut = Uuid.NewUuid();

		await Assert.That(sut.ToString("n")).IsEqualTo(sut.ToGuid().ToString("n"));
	}

	[Test]
	public async ValueTask to_dto_returns_expected_result() {
		var msb = GetRandomInt64();
		var lsb = GetRandomInt64();

		var sut = Uuid.FromInt64(msb, lsb);

		var result = sut.ToDto();

		await Assert.That(result.Structured).IsNotNull();
		await Assert.That(result.Structured.LeastSignificantBits).IsEqualTo(lsb);
		await Assert.That(result.Structured.MostSignificantBits).IsEqualTo(msb);
	}

	[Test]
	public async ValueTask parse_returns_expected_result() {
		var guid = Guid.NewGuid();

		var sut = Uuid.Parse(guid.ToString());

		await Assert.That(sut).IsEqualTo(Uuid.FromGuid(guid));
	}

	[Test]
	public async ValueTask from_int64_returns_expected_result() {
		var guid     = Guid.Parse("65678f9b-d139-4786-8305-b9166922b378");
		var sut      = Uuid.FromInt64(7306966819824813958L, -9005588373953137800L);
		var expected = Uuid.FromGuid(guid);

		await Assert.That(sut).IsEqualTo(expected);
	}

	static long GetRandomInt64() {
		var buffer = new byte[sizeof(long)];

		new Random().NextBytes(buffer);

		return BitConverter.ToInt64(buffer, 0);
	}
}

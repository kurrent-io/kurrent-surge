using System.Text;
using Microsoft.Extensions.Configuration;

namespace Kurrent.Streaming.Tests;

/// <summary>
/// Verifies the module-initializer-registered TimeSpan converter: numeric ms and TimeSpan
/// format strings both bind into a plain <see cref="TimeSpan"/> property
/// </summary>
[Category("Processors")]
public class ConfigTimeSpanConverterTests {
    sealed class Probe {
        public TimeSpan Interval { get; set; }
    }

    [Test]
    public async ValueTask binds_numeric_milliseconds_as_timespan() {
        var section = BuildSection("""{ "Interval": 30000 }""");
        var probe   = new Probe();

        section.Bind(probe);

        await Assert.That(probe.Interval).IsEqualTo(TimeSpan.FromSeconds(30));
    }

    [Test]
    public async ValueTask binds_timespan_format_string() {
        var section = BuildSection("""{ "Interval": "00:00:45" }""");
        var probe   = new Probe();

        section.Bind(probe);

        await Assert.That(probe.Interval).IsEqualTo(TimeSpan.FromSeconds(45));
    }

    [Test]
    public async ValueTask binds_days_in_timespan_format() {
        var section = BuildSection("""{ "Interval": "1.00:00:00" }""");
        var probe   = new Probe();

        section.Bind(probe);

        await Assert.That(probe.Interval).IsEqualTo(TimeSpan.FromDays(1));
    }

    [Test]
    public async ValueTask invalid_string_throws() {
        var section = BuildSection("""{ "Interval": "not-a-duration" }""");
        var probe   = new Probe();

        await Assert.That(() => section.Bind(probe)).ThrowsException();
    }

    static IConfiguration BuildSection(string json) =>
        new ConfigurationBuilder()
            .AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(json)))
            .Build();
}

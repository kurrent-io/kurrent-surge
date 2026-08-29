namespace Kurrent.Surge.Tests.Options;

public class KurrentClientGossipOptionsTests {
    [Test]
    public async ValueTask default_gossip_options_have_expected_values() {
        // Create default options
        var options = KurrentClientGossipOptions.Default;

        // Verify default values
        await Assert.That(options.MaxDiscoverAttempts).IsEqualTo(10);
        await Assert.That(options.DiscoveryInterval).IsEqualTo(TimeSpan.FromMilliseconds(100));
        await Assert.That(options.Timeout).IsEqualTo(TimeSpan.FromSeconds(5));
        await Assert.That(options.ReadPreference).IsEqualTo(NodeReadPreference.Leader);
    }

    [Test]
    public async ValueTask creating_custom_gossip_options_stores_expected_values() {
        // Create custom gossip options
        var options = new KurrentClientGossipOptions {
            MaxDiscoverAttempts = 15,
            DiscoveryInterval   = TimeSpan.FromMilliseconds(200),
            Timeout             = TimeSpan.FromSeconds(10),
            ReadPreference      = NodeReadPreference.Leader
        };

        // Verify custom values were stored correctly
        await Assert.That(options.MaxDiscoverAttempts).IsEqualTo(15);
        await Assert.That(options.DiscoveryInterval).IsEqualTo(TimeSpan.FromMilliseconds(200));
        await Assert.That(options.Timeout).IsEqualTo(TimeSpan.FromSeconds(10));
        await Assert.That(options.ReadPreference).IsEqualTo(NodeReadPreference.Leader);
    }

    [Test]
    public async ValueTask gossip_options_with_small_timeout_are_valid() {
        // Create options with minimal timeouts
        var options = new KurrentClientGossipOptions {
            DiscoveryInterval = TimeSpan.FromMilliseconds(10),
            Timeout           = TimeSpan.FromMilliseconds(50)
        };

        // Verify these extreme but valid values are stored correctly
        await Assert.That(options.DiscoveryInterval).IsEqualTo(TimeSpan.FromMilliseconds(10));
        await Assert.That(options.Timeout).IsEqualTo(TimeSpan.FromMilliseconds(50));
    }

    [Test]
    public async ValueTask default_constructor_creates_same_values_as_default_property() {
        // Create with default constructor
        var constructedOptions = new KurrentClientGossipOptions();

        // Get the static Default instance
        var defaultOptions = KurrentClientGossipOptions.Default;

        // Verify they have the same values
        await Assert.That(constructedOptions.MaxDiscoverAttempts).IsEqualTo(defaultOptions.MaxDiscoverAttempts);
        await Assert.That(constructedOptions.DiscoveryInterval).IsEqualTo(defaultOptions.DiscoveryInterval);
        await Assert.That(constructedOptions.Timeout).IsEqualTo(defaultOptions.Timeout);
        await Assert.That(constructedOptions.ReadPreference).IsEqualTo(defaultOptions.ReadPreference);
    }
}

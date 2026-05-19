// ReSharper disable UseObjectOrCollectionInitializer

using Kurrent.Testkit.TUnit;

namespace Aspire.Hosting.KurrentDB.Tests;

[Category("Hosting")]
public class KurrentDBSettingsTests {
    [ClassDataSource<BogusFaker>(Shared = SharedType.PerTestSession)]
    public required BogusFaker Faker { get; [UsedImplicitly] init; }

    #region ->> Test Case Generators <<-

    public class EmptyStringCases : TestCaseDataGenerator<string> {
        protected override IEnumerable<TestCase<string>> Data() {
            yield return new("", DisplayName: "empty");
            yield return new(" ", DisplayName: "whitespace");
        }
    }

    public class EvenClusterSizeCases : TestCaseDataGenerator<int> {
        protected override IEnumerable<TestCase<int>> Data() {
            yield return new(2, DisplayName: "2 nodes");
            yield return new(4, DisplayName: "4 nodes");
            yield return new(6, DisplayName: "6 nodes");
        }
    }

    public class OddClusterSizeCases : TestCaseDataGenerator<int> {
        protected override IEnumerable<TestCase<int>> Data() {
            yield return new(3, DisplayName: "3 nodes");
            yield return new(5, DisplayName: "5 nodes");
            yield return new(7, DisplayName: "7 nodes");
        }
    }

    public class InvalidRegistryCases : TestCaseDataGenerator<string> {
        protected override IEnumerable<TestCase<string>> Data() {
            yield return new("",               DisplayName: "empty");
            yield return new(" ",              DisplayName: "whitespace");
            yield return new("not-a-hostname", DisplayName: "hyphenated word");
            yield return new("justaword",      DisplayName: "plain word");
        }
    }

    public class ValidRegistryCases : TestCaseDataGenerator<string> {
        protected override IEnumerable<TestCase<string>> Data() {
            yield return new("docker.io",          DisplayName: "dot separator");
            yield return new("registry:5000",      DisplayName: "port separator");
            yield return new("localhost",          DisplayName: "localhost");
            yield return new("docker.kurrent.io",  DisplayName: "multi-dot hostname");
        }
    }

    public class ImageRefParsingCases : TestCaseDataGenerator<(string Input, string Registry, string Image, string Tag, string Expected)> {
        protected override IEnumerable<TestCase<(string Input, string Registry, string Image, string Tag, string Expected)>> Data() {
            yield return new(("docker.kurrent.io/kurrent-latest/kurrentdb:latest", "docker.kurrent.io", "kurrent-latest/kurrentdb", "latest", "docker.kurrent.io/kurrent-latest/kurrentdb:latest"), DisplayName: "full ref with registry and tag");
            yield return new(("docker.kurrent.io/kurrent-staging/kurrentdb",       "docker.kurrent.io", "kurrent-staging/kurrentdb", "latest", "docker.kurrent.io/kurrent-staging/kurrentdb:latest"), DisplayName: "no tag defaults to latest");
            yield return new(("registry:5000/myimage:v2",                          "registry:5000",     "myimage",                    "v2",    "registry:5000/myimage:v2"),                           DisplayName: "port in registry");
            yield return new(("localhost/myimage:dev",                              "localhost",         "myimage",                    "dev",   "localhost/myimage:dev"),                               DisplayName: "localhost registry");
            yield return new(("myimage:v1",                                        "",                  "myimage",                    "v1",    "myimage:v1"),                                          DisplayName: "no registry");
        }
    }

    #endregion // Test Case Generators

    #region ->> EnsureValid <<-

    [Test]
    public async ValueTask default_settings_are_valid() {
        // Arrange
        var settings = new KurrentDBSettings();

        // Act & Assert
        await Assert.That(settings.EnsureValid).ThrowsNothing();
    }

    [Test]
    [EmptyStringCases]
    public async ValueTask rejects_empty_username(string userName) {
        // Arrange
        var settings = new KurrentDBSettings { Credentials = { UserName = userName } };

        // Act & Assert
        await Assert.That(settings.EnsureValid).ThrowsExactly<ArgumentException>();
    }

    [Test]
    public async ValueTask rejects_zero_node_count() {
        // Arrange
        var settings = new KurrentDBSettings { Topology = { Nodes = 0 } };

        // Act & Assert
        await Assert.That(settings.EnsureValid).ThrowsExactly<ArgumentOutOfRangeException>();
    }

    [Test]
    public async ValueTask rejects_negative_node_count() {
        // Arrange
        var settings = new KurrentDBSettings();

        // Act & Assert — setter throws before EnsureValid is reached
        await Assert.That(() => settings.Topology.Nodes = -1).ThrowsExactly<ArgumentOutOfRangeException>();
    }

    [Test]
    public async ValueTask rejects_negative_replicas() {
        // Arrange
        var settings = new KurrentDBSettings {
            Topology = { Replicas = -1 }
        };

        // Act & Assert
        await Assert.That(settings.EnsureValid).ThrowsExactly<ArgumentOutOfRangeException>();
    }

    [Test]
    public async ValueTask rejects_replicas_with_single_node() {
        // Arrange
        var settings = new KurrentDBSettings {
            Topology = { Nodes = 1, Replicas = 1 }
        };

        // Act & Assert
        await Assert.That(settings.EnsureValid).ThrowsExactly<ArgumentException>();
    }

    [Test]
    [EvenClusterSizeCases]
    public async ValueTask rejects_even_cluster_size(int nodes) {
        // Arrange
        var settings = new KurrentDBSettings { Topology = { Nodes = nodes } };

        // Act & Assert
        await Assert.That(settings.EnsureValid).ThrowsExactly<ArgumentException>();
    }

    [Test]
    [OddClusterSizeCases]
    public async ValueTask accepts_odd_cluster_size(int nodes) {
        // Arrange
        var settings = new KurrentDBSettings { Topology = { Nodes = nodes } };

        // Act & Assert
        await Assert.That(settings.EnsureValid).ThrowsNothing();
    }

    [Test]
    [OddClusterSizeCases]
    public async ValueTask accepts_cluster_with_replicas(int nodes) {
        // Arrange
        var replicas = Faker.Random.Int(1, 3);
        var settings = new KurrentDBSettings {
            Topology = { Nodes = nodes, Replicas = replicas }
        };

        // Act & Assert
        await Assert.That(settings.EnsureValid).ThrowsNothing();
    }

    [Test]
    [EmptyStringCases]
    public async ValueTask rejects_empty_container_image(string image) {
        // Arrange
        var settings = new KurrentDBSettings { Container = { Image = image } };

        // Act & Assert
        await Assert.That(settings.EnsureValid).ThrowsExactly<ArgumentException>();
    }

    [Test]
    [EmptyStringCases]
    public async ValueTask rejects_empty_container_tag(string tag) {
        // Arrange
        var settings = new KurrentDBSettings { Container = { Tag = tag } };

        // Act & Assert
        await Assert.That(settings.EnsureValid).ThrowsExactly<ArgumentException>();
    }

    [Test]
    [InvalidRegistryCases]
    public async ValueTask rejects_invalid_container_registry(string registry) {
        // Arrange
        var settings = new KurrentDBSettings { Container = { Registry = registry } };

        // Act & Assert
        await Assert.That(settings.EnsureValid).ThrowsExactly<ArgumentException>();
    }

    [Test]
    [ValidRegistryCases]
    public async ValueTask accepts_valid_container_registry(string registry) {
        // Arrange
        var settings = new KurrentDBSettings { Container = { Registry = registry } };

        // Act & Assert
        await Assert.That(settings.EnsureValid).ThrowsNothing();
    }

    #endregion // EnsureValid

    #region ->> ImageRef Parsing <<-

    [Test]
    [ImageRefParsingCases]
    public async ValueTask parses_image_ref(string input, string expectedRegistry, string expectedImage, string expectedTag, string expected) {
        // Arrange
        var container = new ContainerSettings();

        // Act
        container.ImageRef = input;

        // Assert
        await Assert.That(container.Registry).IsEqualTo(expectedRegistry);
        await Assert.That(container.Image).IsEqualTo(expectedImage);
        await Assert.That(container.Tag).IsEqualTo(expectedTag);
        await Assert.That(container.ImageRef).IsEqualTo(expected);
    }

    [Test]
    public async ValueTask getter_reflects_individual_property_changes() {
        // Arrange
        var registry = Faker.Internet.DomainName();
        var image    = $"{Faker.Lorem.Word()}/{Faker.Lorem.Word()}";
        var tag      = Faker.Lorem.Word();
        var expected = $"{registry}/{image}:{tag}";

        var container = new ContainerSettings();

        // Act
        container.Registry = registry;
        container.Image    = image;
        container.Tag      = tag;

        // Assert
        await Assert.That(container.ImageRef).IsEqualTo(expected);
    }

    #endregion // ImageRef Parsing
}

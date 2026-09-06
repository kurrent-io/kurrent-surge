using Kurrent.Toolkit;
using System.ComponentModel;

namespace Kurrent.Surge.Tests.Internal;

public class EnumExtensionsTests {
    [Test]
    public async ValueTask returns_description_when_attribute_exists() {
        await Assert.That(TestEnum.First.Description).IsEqualTo("First Value");
        await Assert.That(TestEnum.Third.Description).IsEqualTo("Third Value");
    }

    [Test]
    public async ValueTask returns_enum_name_when_description_attribute_missing() {
        await Assert.That(TestEnum.Second.Description).IsEqualTo("Second");
    }

    [Test]
    public async ValueTask returns_cached_result_for_repeated_calls() {
        var firstCall  = TestEnum.First.Description;
        var secondCall = TestEnum.First.Description;

        await Assert.That(firstCall).IsEqualTo(secondCall);
        await Assert.That(firstCall).IsEqualTo("First Value");
    }

    [Test]
    public async ValueTask handles_different_enum_types_correctly() {
        await Assert.That(TestEnum.First.Description).IsEqualTo("First Value");
        await Assert.That(KurrentConnectionScheme.Direct.Description).IsEqualTo("kurrentdb");
    }

    [Test]
    public async ValueTask returns_correct_descriptions_for_connection_scheme_type() {
        await Assert.That(KurrentConnectionScheme.Direct.Description).IsEqualTo("kurrentdb");
        await Assert.That(KurrentConnectionScheme.Discover.Description).IsEqualTo("kurrentdb+discover");
    }

    // Test enums for verification
    enum TestEnum {
        [Description("First Value")] First,

        // No description attribute
        Second,
        [Description("Third Value")] Third
    }

}

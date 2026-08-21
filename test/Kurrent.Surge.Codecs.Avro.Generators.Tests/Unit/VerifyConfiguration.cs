using System.Runtime.CompilerServices;
using VerifyTests;

namespace Kurrent.Surge.Codecs.Avro.Generators.Tests.Unit;

/// <summary>
/// Sets up Verify for source generator snapshots.
/// </summary>
/// <remarks>
/// <c>VerifySourceGenerators</c> teaches Verify how to serialise a
/// <see cref="Microsoft.CodeAnalysis.GeneratorDriver"/> result: one section per generated file, with
/// its hint name, so a snapshot reads like the output rather than like an object dump.
/// <para>
/// Snapshots live beside the test as <c>.verified.txt</c> and are committed. A run that differs writes
/// a <c>.received.txt</c> and fails, so the change is reviewed as a diff instead of being invisible
/// until someone thinks to assert the right substring.
/// </para>
/// </remarks>
static class VerifyConfiguration {
    [ModuleInitializer]
    public static void Initialize() {
        VerifySourceGenerators.Initialize();

        // Snapshots are committed, so a machine-specific path in one would break every other machine.
        VerifierSettings.ScrubLinesContaining("Kurrent.Surge.Codecs.Avro.Generators", "1.0.0.0");
    }
}

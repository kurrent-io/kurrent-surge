using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.KurrentDB;

namespace Kurrent.Client.Arrow.Tests;

public sealed class ArrowFixture : KurrentDBFixture {
    protected override string ResourceName => "ArrowKurrentDB";

    protected override void ConfigureSettings(KurrentDBSettings settings) {
        settings.TlsEnabled = false;
        settings.Lifetime   = ContainerLifetime.Persistent;
    }
    
}
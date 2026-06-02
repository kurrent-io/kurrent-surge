[assembly: TUnit.Core.Executors.TestExecutor<Kurrent.Testkit.TestkitTestExecutor>]

[assembly: SkipAll("Kurrent.Client.Tests.Admin", "Flaky tests. Investigate.")]
[assembly: SkipAll("Kurrent.Client.Tests.Connectors", "Flaky tests. Investigate.")]
[assembly: SkipAll("Kurrent.Client.Tests.Indexes", "Flaky tests. Investigate.")]
[assembly: SkipAll("Kurrent.Client.Tests.Registry", "Flaky tests. Investigate.")]
[assembly: SkipAll("Kurrent.Client.Tests.Secure", "Flaky tests. Investigate.")]

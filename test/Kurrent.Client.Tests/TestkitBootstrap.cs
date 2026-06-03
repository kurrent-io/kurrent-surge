using Kurrent.Testkit.TUnit;

[assembly: TUnit.Core.Executors.TestExecutor<Kurrent.Testkit.TestkitTestExecutor>]

[assembly: SkipAll("Kurrent.Client.Tests.Admin", "Flaky tests. Investigate.")]
[assembly: SkipAll("Kurrent.Client.Tests.Secure", "Flaky tests. Investigate.")]

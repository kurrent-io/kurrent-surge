[assembly: TUnit.Core.Executors.TestExecutor<Kurrent.Testkit.TestkitTestExecutor>]

[assembly: SkipAll("Kurrent.Surge.Tests.Admin", "Flaky tests. Investigate.")]
[assembly: SkipAll("Kurrent.Surge.Tests.Secure", "Flaky tests. Investigate.")]
[assembly: SkipAll(
    typeof(Kurrent.Surge.Tests.Client.LoadBalancing.LargeClusterConvergenceTests),
    "Cold multi-node clusters fail catalog auto-registration: CreateSchema returns Internal wrapping StreamNotFound for a varying subset of schemas. Kept as the reproduction — unskip when the server classifies that as NotFound.")]

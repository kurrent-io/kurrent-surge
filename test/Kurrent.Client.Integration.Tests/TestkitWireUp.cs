using Kurrent.Testing.TUnit;
using Microsoft.Extensions.Logging;
using Serilog;

[assembly: TestkitTestConfigurator]
[assembly: TUnit.Core.Executors.TestExecutor<TestkitTestExecutor>]

namespace Kurrent.Client.Tests;

public class TestkitWireUpTests {
    [Test]
    [Repeat(10)]
    public void logging_configured() {
        TestContext.CurrrentLogger
            .LogInformation("Logger() is effective. TestContext TestUid: {CurrentTestUid}", TestContext.Current?.Id);

        TestContext.CurrentLoggerFactory.CreateLogger<TestkitWireUpTests>()
            .LogInformation("CreateLogger<T>() is effective. TestContext TestUid: {CurrentTestUid}", TestContext.Current?.Id);

        TestContext.CurrentLoggerFactory.CreateLogger("TestEnvironmentWireUpTestsFromString")
            .LogInformation("CreateLogger(string categoryName) is effective. TestContext TestUid: {CurrentTestUid}", TestContext.Current?.Id);

        Log.Information("Logging from Serilog Static Log. Current TestUid: {CurrentTestUid}", TestContext.Current?.Id);
    }

    [Test]
    [Repeat(10)]
    public async ValueTask logs_are_scoped_to_test() {
        TestContext.CurrrentLogger
            .LogInformation("Logger() is effective. Current TestUid: {CurrentTestUid}", TestContext.Current?.Id);

        TestContext.CurrentLoggerFactory.CreateLogger<TestkitWireUpTests>()
            .LogInformation("CreateLogger<T>() is effective. Current TestUid: {CurrentTestUid}", TestContext.Current?.Id);

        TestContext.CurrentLoggerFactory.CreateLogger("TestEnvironmentWireUpTestsFromString")
            .LogInformation("CreateLogger(string categoryName) is effective. Current TestUid: {CurrentTestUid}", TestContext.Current?.Id);

        Log.Information("Logging from Serilog Static Log. Current TestUid: {CurrentTestUid}", TestContext.Current?.Id);

        await Task.Run(async () => {
            TestContext.CurrrentLogger
                .LogInformation("Logger() is effective in another thread. Current TestUid: {CurrentTestUid}", TestContext.Current?.Id);

            TestContext.CurrentLoggerFactory.CreateLogger<TestkitWireUpTests>()
                .LogInformation("CreateLogger<T>() is effective in another thread. Current TestUid: {CurrentTestUid}", TestContext.Current?.Id);

            TestContext.CurrentLoggerFactory.CreateLogger("TestEnvironmentWireUpTestsFromString")
                .LogInformation("CreateLogger(string categoryName) is effective in another thread. Current TestUid: {CurrentTestUid}", TestContext.Current?.Id);

            Log.Information("Logging from Serilog Static Log in another thread. Current TestUid: {CurrentTestUid}", TestContext.Current?.Id);

            await Task.Run(() => {
                TestContext.CurrrentLogger
                    .LogInformation("Logger() is effective in sub thread. Current TestUid: {CurrentTestUid}", TestContext.Current?.Id);

                TestContext.CurrentLoggerFactory.CreateLogger<TestkitWireUpTests>()
                    .LogInformation("CreateLogger<T>() is effective in sub thread. Current TestUid: {CurrentTestUid}", TestContext.Current?.Id);

                TestContext.CurrentLoggerFactory.CreateLogger("TestEnvironmentWireUpTestsFromString")
                    .LogInformation("CreateLogger(string categoryName) is effective in sub thread. Current TestUid: {CurrentTestUid}", TestContext.Current?.Id);

                Log.Information("Logging from Serilog Static Log in sub thread. Current TestUid: {CurrentTestUid}", TestContext.Current?.Id);
            });
        });
    }
}

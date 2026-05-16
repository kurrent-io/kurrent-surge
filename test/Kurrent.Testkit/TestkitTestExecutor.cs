// ReSharper disable CheckNamespace

using Grpc.Core;
using Kurrent.Testkit.TUnit;
using Serilog;
using Serilog.Context;
using TUnit.Core.Interfaces;

namespace Kurrent.Testkit;

/// <summary>
/// Custom test executor for the Kurrent test suite.
/// <remarks>
/// This executor not only injects the test identifier into the log context,
/// but also handles gRPC exceptions in a more user-friendly format.
/// </remarks>
/// </summary>
public class TestkitTestExecutor : ITestExecutor {
    public async ValueTask ExecuteTest(TestContext context, Func<ValueTask> action) {
        using var _ = LogContext.PushProperty("TestUid", context.TestUid);

        try {
            await action().ConfigureAwait(false);
        } catch (RpcException ex) {
            var status = ex.GetRpcStatus();

            if (status is null) {
                Log.Error(
                    ex, "{TestClass} {TestName} failed: {ErrorMessage}",
                    context.Metadata.TestDetails.ClassType.Name,
                    context.Metadata.TestDetails.TestName,
                    ex.Message);

                throw;
            }

            Log.Error(
                ex.Status.DebugException,
                "{TestClass} {TestName} failed: {ErrorMessage}",
                context.Metadata.TestDetails.ClassType.Name,
                context.Metadata.TestDetails.TestName,
                ex.Status.Detail);

            var details = string.Join($"{Environment.NewLine}", status.Details.Select(d => $"  - {d.TypeUrl}"));

            var errorMessage = $"*** gRPC Request Failed ***{Environment.NewLine}"
                             + $"Status:  {ex.StatusCode} ({ex.StatusCode.GetHashCode()}){Environment.NewLine}"
                             + $"Error:   {ex.Status.Detail}{Environment.NewLine}"
                             + $"Details:{Environment.NewLine}{details}{Environment.NewLine}";

            throw new(errorMessage, ex.Status.DebugException);
        } catch (Exception ex) {
            Log.Error(
                ex, "{TestClass} {TestName} failed: {ErrorMessage}",
                context.Metadata.TestDetails.ClassType.Name,
                context.Metadata.TestDetails.TestName,
                ex.Message);

            throw;
        }
    }
}
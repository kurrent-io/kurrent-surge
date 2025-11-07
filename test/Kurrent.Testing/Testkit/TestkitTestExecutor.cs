using System.Diagnostics;
using Grpc.Core;
using Kurrent.Testing.TUnit;
using TUnit.Core.Interfaces;

namespace Kurrent.Testing;

public class TestkitTestExecutor : ITestExecutor {
    public async ValueTask ExecuteTest(TestContext context, Func<ValueTask> action) {
        var logger = context.AddLogging(
            TestkitExecutionContext.GetTestLogger(context.Id));

        try {
            await action();
        }
        catch (RpcException ex) {
            var status = ex.GetRpcStatus();

            if (status is null) {
                logger.Error(
                    ex, "{TestClass} {TestName} {State} {ErrorMessage}", context.Metadata.TestDetails.ClassType.Name,
                    context.Metadata.TestDetails.TestName, TestState.Failed, ex.Message
                );

                throw ex.Demystify();
            }

            logger.Error(
                ex.Status.DebugException,
                "{TestClass} {TestName} {State} {ErrorMessage}",
                context.Metadata.TestDetails.ClassType.Name, context.Metadata.TestDetails.TestName, TestState.Failed,
                ex.Status.Detail
            );

            var errorMessage =
                $"*** gRPC Request Failed ***{Environment.NewLine}"
              + $"Status:  {ex.StatusCode} ({ex.StatusCode.GetHashCode()}){Environment.NewLine}"
              + $"Error:   {ex.Status.Detail}{Environment.NewLine}"
              + $"Details:{Environment.NewLine}{string.Join($"{Environment.NewLine}", status.Details.Select(d => $"  - {d.TypeUrl}"))}{Environment.NewLine}";

            throw new Exception(errorMessage, ex.Status.DebugException).Demystify();
        }
        catch (Exception ex) {
            logger.Error(
                ex, "{TestClass} {TestName} {State} {ErrorMessage}", context.Metadata.TestDetails.ClassType.Name,
                context.Metadata.TestDetails.TestName, TestState.Failed, ex.Message
            );

            throw ex.Demystify();
        }
        finally {
            if (logger is IAsyncDisposable disposable)
                await disposable.DisposeAsync();
        }
    }
}

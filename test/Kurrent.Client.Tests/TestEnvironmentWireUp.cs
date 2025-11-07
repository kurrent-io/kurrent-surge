[assembly: TestkitTestConfigurator]
[assembly: TUnit.Core.Executors.TestExecutor<TestkitTestExecutor>]

// namespace Kurrent.Client.Tests;

// public class TestEnvironmentWireUp {
//     [Before(Assembly)]
//     public static ValueTask BeforeAssembly(AssemblyHookContext context) =>
//         TestkitExecutionContext.Initialize(context.Assembly);
//
//     [After(Assembly)]
//     public static ValueTask AfterAssembly(AssemblyHookContext context) =>
//         TestkitExecutionContext.Reset(context.Assembly);
// }

// using Microsoft.Extensions.Logging;
// using Serilog.Extensions.Logging;
//
// namespace Kurrent.Testing.Testkit;
//
// public class TestkitSystemLoggerFactory : ILoggerFactory {
//     readonly ILoggerFactory _inner = new SerilogLoggerFactory(Serilog.Log.Logger);
//
//     public ILogger CreateLogger(string categoryName) => _inner.CreateLogger(categoryName);
//
//     public void AddProvider(ILoggerProvider provider) => _inner.AddProvider(provider);
//
//     public void Dispose() { }
// }
//
// public static class TestkitSystemLogging {
//     public static readonly ILoggerFactory SystemLoggerFactory = new TestkitSystemLoggerFactory();
//     public static readonly ILogger        SystemLogger         = SystemLoggerFactory.CreateLogger("");
// }

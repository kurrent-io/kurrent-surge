using System.Diagnostics;

namespace Kurrent.Client;

static class EnumerableTaskExtensions {
	extension(IEnumerable<Task> source) {
        [DebuggerStepThrough]
        public Task WhenAll() => Task.WhenAll(source);
    }

	extension<T>(IEnumerable<Task<T>> source) {
        [DebuggerStepThrough]
        public Task<T[]> WhenAll() => Task.WhenAll(source);
    }

    extension(IEnumerable<ValueTask> source) {
        [DebuggerStepThrough]
        public Task WhenAll() => Task.WhenAll(source.Select(vt => vt.AsTask()));
    }

    extension<T>(IEnumerable<ValueTask<T>> source) {
        [DebuggerStepThrough]
        public Task<T[]> WhenAll() => Task.WhenAll(source.Select(vt => vt.AsTask()));
    }
}

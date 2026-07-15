namespace Kurrent.Surge.Client;

static class ObjectExtensions {
    extension<T>(T source) {
        public ValueTask<T> AsValueTask() => new(source);
    }

    extension(object source) {
        public T As<T>() => (T)source;
    }
}

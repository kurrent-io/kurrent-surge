namespace Kurrent.Toolkit;

static class ObjectExtensions {
    extension<T>(T source) {
        public ValueTask<T> AsValueTask() => new(source);
    }

    extension(object source) {
        public T As<T>() => (T)source;
        
        // Qualified: an unqualified call binds to this member (innermost scope wins over the
        // more specific Type overload in the parent namespace) and recurses without end.
        public bool IsBytes() => TypeExtensions.IsBytes(source.GetType());
    }
}

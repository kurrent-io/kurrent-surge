namespace Kurrent.Client;

static class ObjectExtensions {
    public static T As<T>(this object source) => (T)source;
}

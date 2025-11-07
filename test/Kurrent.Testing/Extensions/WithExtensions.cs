// using System.Diagnostics;
//
// namespace Kurrent.Testing;
//
// public static class WithExtensions {
//     [DebuggerStepThrough]
//     public static T With<T>(this T instance, Action<T> update) {
//         update(instance);
//         return instance;
//     }
//
//     [DebuggerStepThrough]
//     public static T With<T>(this T instance, Func<T, T> update) {
//         return update(instance);
//     }
// }

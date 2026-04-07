using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace Kurrent.Client;

public interface IThrowableException<out TSelf> where TSelf : Exception, IThrowableException<TSelf> {
    static abstract TSelf Create(string message, Exception? innerException = null);
}

public static class ThrowableExceptionExtensions {
    extension<T>(IThrowableException<T>) where T : Exception, IThrowableException<T> {
        [DoesNotReturn, StackTraceHidden]
        public static void Throw(string message, Exception? innerException = null) =>
            throw T.Create(message, innerException);
    }
}
//
// public interface IThrowableException<out TSelf> where TSelf : Exception, IThrowableException<TSelf> {
//     static abstract TSelf Create(string message, Exception? innerException = null);
// }
//
// public static class ThrowableExceptionExtensions {
//     extension<T>(IThrowableException<T>) where T : Exception, IThrowableException<T> {
//         [DoesNotReturn, StackTraceHidden]
//         public static void Throw(string message, Exception? innerException = null) =>
//             throw T.Create(message, innerException);
//     }
// }

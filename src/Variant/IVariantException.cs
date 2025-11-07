// ReSharper disable CheckNamespace

using System.Collections.Immutable;

namespace Kurrent.Variant;

/// <summary>
/// Specialized variant interface for discriminated union error types where all variants are exceptions.
/// Implements IResultError to enable use as error type in Result&lt;TValue, TError&gt;.
/// </summary>
public interface IVariantException : IVariant, IResultError {
    ErrorCode IResultError.ErrorCode => ErrorCode.FromType(Value.GetType());

    string IResultError.ErrorMessage => ((Exception)Value).Message;

    ImmutableDictionary<string, object?> IResultError.Metadata => ImmutableDictionary<string, object?>.Empty
        .Add(ResultError.ReservedErrorCodeKey, ErrorCode.FromType(Value.GetType()));

    Exception IResultError.GetOrCreateException() => (Exception)Value;
}

/// <summary>
/// Specialized variant interface for discriminated union error types where all variants implement Exception.
/// This interface combines variant capabilities with automatic Exception passthrough functionality.
/// When accessed as Exception, the methods delegate to the currently contained error type.
/// </summary>
[PublicAPI]
public interface IVariantException<T0> : IVariant<T0>, IVariantException
    where T0 : Exception;

/// <summary>
/// Specialized variant interface for discriminated union error types where all variants implement Exception.
/// This interface combines variant capabilities with automatic Exception passthrough functionality.
/// When accessed as Exception, the methods delegate to the currently contained error type.
/// </summary>
[PublicAPI]
public interface IVariantException<T0, T1> : IVariant<T0, T1>, IVariantException
    where T0 : Exception
    where T1 : Exception;

/// <summary>
/// Specialized variant interface for discriminated union error types where all variants implement Exception.
/// This interface combines variant capabilities with automatic Exception passthrough functionality.
/// When accessed as Exception, the methods delegate to the currently contained error type.
/// </summary>
[PublicAPI]
public interface IVariantException<T0, T1, T2> : IVariant<T0, T1, T2>, IVariantException
    where T0 : Exception
    where T1 : Exception
    where T2 : Exception;

/// <summary>
/// Specialized variant interface for discriminated union error types where all variants implement Exception.
/// This interface combines variant capabilities with automatic Exception passthrough functionality.
/// When accessed as Exception, the methods delegate to the currently contained error type.
/// </summary>
[PublicAPI]
public interface IVariantException<T0, T1, T2, T3> : IVariant<T0, T1, T2, T3>, IVariantException
    where T0 : Exception
    where T1 : Exception
    where T2 : Exception
    where T3 : Exception;

/// <summary>
/// Specialized variant interface for discriminated union error types where all variants implement Exception.
/// This interface combines variant capabilities with automatic Exception passthrough functionality.
/// When accessed as Exception, the methods delegate to the currently contained error type.
/// </summary>
[PublicAPI]
public interface IVariantException<T0, T1, T2, T3, T4> : IVariant<T0, T1, T2, T3, T4>, IVariantException
    where T0 : Exception
    where T1 : Exception
    where T2 : Exception
    where T3 : Exception
    where T4 : Exception;

/// <summary>
/// Specialized variant interface for discriminated union error types where all variants implement Exception.
/// This interface combines variant capabilities with automatic Exception passthrough functionality.
/// When accessed as Exception, the methods delegate to the currently contained error type.
/// </summary>
[PublicAPI]
public interface IVariantException<T0, T1, T2, T3, T4, T5> : IVariant<T0, T1, T2, T3, T4, T5>, IVariantException
    where T0 : Exception
    where T1 : Exception
    where T2 : Exception
    where T3 : Exception
    where T4 : Exception
    where T5 : Exception;

/// <summary>
/// Specialized variant interface for discriminated union error types where all variants implement Exception.
/// This interface combines variant capabilities with automatic Exception passthrough functionality.
/// When accessed as Exception, the methods delegate to the currently contained error type.
/// </summary>
[PublicAPI]
public interface IVariantException<T0, T1, T2, T3, T4, T5, T6> : IVariant<T0, T1, T2, T3, T4, T5, T6>, IVariantException
    where T0 : Exception
    where T1 : Exception
    where T2 : Exception
    where T3 : Exception
    where T4 : Exception
    where T5 : Exception
    where T6 : Exception;

/// <summary>
/// Specialized variant interface for discriminated union error types where all variants implement Exception.
/// This interface combines variant capabilities with automatic Exception passthrough functionality.
/// When accessed as Exception, the methods delegate to the currently contained error type.
/// </summary>
[PublicAPI]
public interface IVariantException<T0, T1, T2, T3, T4, T5, T6, T7> : IVariant<T0, T1, T2, T3, T4, T5, T6, T7>, IVariantException
    where T0 : Exception
    where T1 : Exception
    where T2 : Exception
    where T3 : Exception
    where T4 : Exception
    where T5 : Exception
    where T6 : Exception
    where T7 : Exception;

/// <summary>
/// Specialized variant interface for discriminated union error types where all variants implement Exception.
/// This interface combines variant capabilities with automatic Exception passthrough functionality.
/// When accessed as Exception, the methods delegate to the currently contained error type.
/// </summary>
[PublicAPI]
public interface IVariantException<T0, T1, T2, T3, T4, T5, T6, T7, T8> : IVariant<T0, T1, T2, T3, T4, T5, T6, T7, T8>, IVariantException
    where T0 : Exception
    where T1 : Exception
    where T2 : Exception
    where T3 : Exception
    where T4 : Exception
    where T5 : Exception
    where T6 : Exception
    where T7 : Exception
    where T8 : Exception;

/// <summary>
/// Specialized variant interface for discriminated union error types where all variants implement Exception.
/// This interface combines variant capabilities with automatic Exception passthrough functionality.
/// When accessed as Exception, the methods delegate to the currently contained error type.
/// </summary>
[PublicAPI]
public interface IVariantException<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9> : IVariant<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9>, IVariantException
    where T0 : Exception
    where T1 : Exception
    where T2 : Exception
    where T3 : Exception
    where T4 : Exception
    where T5 : Exception
    where T6 : Exception
    where T7 : Exception
    where T8 : Exception
    where T9 : Exception;

/// <summary>
/// Specialized variant interface for discriminated union error types where all variants implement Exception.
/// This interface combines variant capabilities with automatic Exception passthrough functionality.
/// When accessed as Exception, the methods delegate to the currently contained error type.
/// </summary>
[PublicAPI]
public interface IVariantException<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10> : IVariant<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>, IVariantException
    where T0 : Exception
    where T1 : Exception
    where T2 : Exception
    where T3 : Exception
    where T4 : Exception
    where T5 : Exception
    where T6 : Exception
    where T7 : Exception
    where T8 : Exception
    where T9 : Exception
    where T10 : Exception;

/// <summary>
/// Specialized variant interface for discriminated union error types where all variants implement Exception.
/// This interface combines variant capabilities with automatic Exception passthrough functionality.
/// When accessed as Exception, the methods delegate to the currently contained error type.
/// </summary>
[PublicAPI]
public interface IVariantException<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11> : IVariant<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>, IVariantException
    where T0 : Exception
    where T1 : Exception
    where T2 : Exception
    where T3 : Exception
    where T4 : Exception
    where T5 : Exception
    where T6 : Exception
    where T7 : Exception
    where T8 : Exception
    where T9 : Exception
    where T10 : Exception
    where T11 : Exception;

/// <summary>
/// Specialized variant interface for discriminated union error types where all variants implement Exception.
/// This interface combines variant capabilities with automatic Exception passthrough functionality.
/// When accessed as Exception, the methods delegate to the currently contained error type.
/// </summary>
[PublicAPI]
public interface IVariantException<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12> : IVariant<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>, IVariantException
    where T0 : Exception
    where T1 : Exception
    where T2 : Exception
    where T3 : Exception
    where T4 : Exception
    where T5 : Exception
    where T6 : Exception
    where T7 : Exception
    where T8 : Exception
    where T9 : Exception
    where T10 : Exception
    where T11 : Exception
    where T12 : Exception;

/// <summary>
/// Specialized variant interface for discriminated union error types where all variants implement Exception.
/// This interface combines variant capabilities with automatic Exception passthrough functionality.
/// When accessed as Exception, the methods delegate to the currently contained error type.
/// </summary>
[PublicAPI]
public interface IVariantException<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13> : IVariant<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>, IVariantException
    where T0 : Exception
    where T1 : Exception
    where T2 : Exception
    where T3 : Exception
    where T4 : Exception
    where T5 : Exception
    where T6 : Exception
    where T7 : Exception
    where T8 : Exception
    where T9 : Exception
    where T10 : Exception
    where T11 : Exception
    where T12 : Exception
    where T13 : Exception;

/// <summary>
/// Specialized variant interface for discriminated union error types where all variants implement Exception.
/// This interface combines variant capabilities with automatic Exception passthrough functionality.
/// When accessed as Exception, the methods delegate to the currently contained error type.
/// </summary>
[PublicAPI]
public interface IVariantException<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14> : IVariant<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>, IVariantException
    where T0 : Exception
    where T1 : Exception
    where T2 : Exception
    where T3 : Exception
    where T4 : Exception
    where T5 : Exception
    where T6 : Exception
    where T7 : Exception
    where T8 : Exception
    where T9 : Exception
    where T10 : Exception
    where T11 : Exception
    where T12 : Exception
    where T13 : Exception
    where T14 : Exception;

/// <summary>
/// Specialized variant interface for discriminated union error types where all variants implement Exception.
/// This interface combines variant capabilities with automatic Exception passthrough functionality.
/// When accessed as Exception, the methods delegate to the currently contained error type.
/// </summary>
[PublicAPI]
public interface IVariantException<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15> : IVariant<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15>, IVariantException
    where T0 : Exception
    where T1 : Exception
    where T2 : Exception
    where T3 : Exception
    where T4 : Exception
    where T5 : Exception
    where T6 : Exception
    where T7 : Exception
    where T8 : Exception
    where T9 : Exception
    where T10 : Exception
    where T11 : Exception
    where T12 : Exception
    where T13 : Exception
    where T14 : Exception
    where T15 : Exception;

/// <summary>
/// Specialized variant interface for discriminated union error types where all variants implement Exception.
/// This interface combines variant capabilities with automatic Exception passthrough functionality.
/// When accessed as Exception, the methods delegate to the currently contained error type.
/// </summary>
[PublicAPI]
public interface IVariantException<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16> : IVariant<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16>, IVariantException
    where T0 : Exception
    where T1 : Exception
    where T2 : Exception
    where T3 : Exception
    where T4 : Exception
    where T5 : Exception
    where T6 : Exception
    where T7 : Exception
    where T8 : Exception
    where T9 : Exception
    where T10 : Exception
    where T11 : Exception
    where T12 : Exception
    where T13 : Exception
    where T14 : Exception
    where T15 : Exception
    where T16 : Exception;

/// <summary>
/// Specialized variant interface for discriminated union error types where all variants implement Exception.
/// This interface combines variant capabilities with automatic Exception passthrough functionality.
/// When accessed as Exception, the methods delegate to the currently contained error type.
/// </summary>
[PublicAPI]
public interface IVariantException<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17> : IVariant<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17>, IVariantException
    where T0 : Exception
    where T1 : Exception
    where T2 : Exception
    where T3 : Exception
    where T4 : Exception
    where T5 : Exception
    where T6 : Exception
    where T7 : Exception
    where T8 : Exception
    where T9 : Exception
    where T10 : Exception
    where T11 : Exception
    where T12 : Exception
    where T13 : Exception
    where T14 : Exception
    where T15 : Exception
    where T16 : Exception
    where T17 : Exception;

/// <summary>
/// Specialized variant interface for discriminated union error types where all variants implement Exception.
/// This interface combines variant capabilities with automatic Exception passthrough functionality.
/// When accessed as Exception, the methods delegate to the currently contained error type.
/// </summary>
[PublicAPI]
public interface IVariantException<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17, T18> : IVariant<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17, T18>, IVariantException
    where T0 : Exception
    where T1 : Exception
    where T2 : Exception
    where T3 : Exception
    where T4 : Exception
    where T5 : Exception
    where T6 : Exception
    where T7 : Exception
    where T8 : Exception
    where T9 : Exception
    where T10 : Exception
    where T11 : Exception
    where T12 : Exception
    where T13 : Exception
    where T14 : Exception
    where T15 : Exception
    where T16 : Exception
    where T17 : Exception
    where T18 : Exception;

/// <summary>
/// Specialized variant interface for discriminated union error types where all variants implement Exception.
/// This interface combines variant capabilities with automatic Exception passthrough functionality.
/// When accessed as Exception, the methods delegate to the currently contained error type.
/// </summary>
[PublicAPI]
public interface IVariantException<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17, T18, T19> : IVariant<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17, T18, T19>, IVariantException
    where T0 : Exception
    where T1 : Exception
    where T2 : Exception
    where T3 : Exception
    where T4 : Exception
    where T5 : Exception
    where T6 : Exception
    where T7 : Exception
    where T8 : Exception
    where T9 : Exception
    where T10 : Exception
    where T11 : Exception
    where T12 : Exception
    where T13 : Exception
    where T14 : Exception
    where T15 : Exception
    where T16 : Exception
    where T17 : Exception
    where T18 : Exception
    where T19 : Exception;

/// <summary>
/// Specialized variant interface for discriminated union error types where all variants implement Exception.
/// This interface combines variant capabilities with automatic Exception passthrough functionality.
/// When accessed as Exception, the methods delegate to the currently contained error type.
/// </summary>
[PublicAPI]
public interface IVariantException<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17, T18, T19, T20> : IVariant<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17, T18, T19, T20>, IVariantException
    where T0 : Exception
    where T1 : Exception
    where T2 : Exception
    where T3 : Exception
    where T4 : Exception
    where T5 : Exception
    where T6 : Exception
    where T7 : Exception
    where T8 : Exception
    where T9 : Exception
    where T10 : Exception
    where T11 : Exception
    where T12 : Exception
    where T13 : Exception
    where T14 : Exception
    where T15 : Exception
    where T16 : Exception
    where T17 : Exception
    where T18 : Exception
    where T19 : Exception
    where T20 : Exception;

/// <summary>
/// Specialized variant interface for discriminated union error types where all variants implement Exception.
/// This interface combines variant capabilities with automatic Exception passthrough functionality.
/// When accessed as Exception, the methods delegate to the currently contained error type.
/// </summary>
[PublicAPI]
public interface IVariantException<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17, T18, T19, T20, T21> : IVariant<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17, T18, T19, T20, T21>, IVariantException
    where T0 : Exception
    where T1 : Exception
    where T2 : Exception
    where T3 : Exception
    where T4 : Exception
    where T5 : Exception
    where T6 : Exception
    where T7 : Exception
    where T8 : Exception
    where T9 : Exception
    where T10 : Exception
    where T11 : Exception
    where T12 : Exception
    where T13 : Exception
    where T14 : Exception
    where T15 : Exception
    where T16 : Exception
    where T17 : Exception
    where T18 : Exception
    where T19 : Exception
    where T20 : Exception
    where T21 : Exception;

/// <summary>
/// Specialized variant interface for discriminated union error types where all variants implement Exception.
/// This interface combines variant capabilities with automatic Exception passthrough functionality.
/// When accessed as Exception, the methods delegate to the currently contained error type.
/// </summary>
[PublicAPI]
public interface IVariantException<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17, T18, T19, T20, T21, T22> : IVariant<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17, T18, T19, T20, T21, T22>, IVariantException
    where T0 : Exception
    where T1 : Exception
    where T2 : Exception
    where T3 : Exception
    where T4 : Exception
    where T5 : Exception
    where T6 : Exception
    where T7 : Exception
    where T8 : Exception
    where T9 : Exception
    where T10 : Exception
    where T11 : Exception
    where T12 : Exception
    where T13 : Exception
    where T14 : Exception
    where T15 : Exception
    where T16 : Exception
    where T17 : Exception
    where T18 : Exception
    where T19 : Exception
    where T20 : Exception
    where T21 : Exception
    where T22 : Exception;

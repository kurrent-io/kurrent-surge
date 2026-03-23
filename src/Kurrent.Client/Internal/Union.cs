// using System.Diagnostics;
// using System.Drawing;
// using System.Text;

// // ReSharper disable UnusedType.Global

// namespace Kurrent.Client;

// public abstract record Union<TSelf, T1, T2>
//     where TSelf : Union<TSelf, T1, T2>
//     where T1 : class, TSelf
//     where T2 : class, TSelf {
//     public TResult Match<TResult>(
//         Func<T1, TResult> case1,
//         Func<T2, TResult> case2
//     ) =>
//         this switch {
//             T1 v => case1(v),
//             T2 v => case2(v),
//             _    => throw new UnreachableException()
//         };

//     public void Switch(
//         Action<T1> case1,
//         Action<T2> case2
//     ) {
//         switch (this) {
//             case T1 v: case1(v); break;

//             case T2 v: case2(v); break;

//             default: throw new UnreachableException();
//         }
//     }

//     public sealed override string ToString() {
//         var sb = new StringBuilder();
//         sb.Append(UnionName.Of(typeof(TSelf)));
//         sb.Append('.');
//         sb.Append(GetType().Name);
//         sb.Append(" { ");
//         if (PrintMembers(sb)) sb.Append(' ');
//         sb.Append('}');
//         return sb.ToString();
//     }
// }

// public abstract record Union<TSelf, T1, T2, T3>
//     where TSelf : Union<TSelf, T1, T2, T3>
//     where T1 : class, TSelf
//     where T2 : class, TSelf
//     where T3 : class, TSelf {
//     public TResult Match<TResult>(
//         Func<T1, TResult> case1,
//         Func<T2, TResult> case2,
//         Func<T3, TResult> case3
//     ) =>
//         this switch {
//             T1 v => case1(v),
//             T2 v => case2(v),
//             T3 v => case3(v),
//             _    => throw new UnreachableException()
//         };

//     public void Switch(
//         Action<T1> case1,
//         Action<T2> case2,
//         Action<T3> case3
//     ) {
//         switch (this) {
//             case T1 v: case1(v); break;

//             case T2 v: case2(v); break;

//             case T3 v: case3(v); break;

//             default: throw new UnreachableException();
//         }
//     }

//     public sealed override string ToString() {
//         var sb = new StringBuilder();
//         sb.Append(UnionName.Of(typeof(TSelf)));
//         sb.Append('.');
//         sb.Append(GetType().Name);
//         sb.Append(" { ");
//         if (PrintMembers(sb)) sb.Append(' ');
//         sb.Append('}');
//         return sb.ToString();
//     }
// }

// public abstract record Union<TSelf, T1, T2, T3, T4>
//     where TSelf : Union<TSelf, T1, T2, T3, T4>
//     where T1 : class, TSelf
//     where T2 : class, TSelf
//     where T3 : class, TSelf
//     where T4 : class, TSelf {
//     public TResult Match<TResult>(
//         Func<T1, TResult> case1,
//         Func<T2, TResult> case2,
//         Func<T3, TResult> case3,
//         Func<T4, TResult> case4
//     ) =>
//         this switch {
//             T1 v => case1(v),
//             T2 v => case2(v),
//             T3 v => case3(v),
//             T4 v => case4(v),
//             _    => throw new UnreachableException()
//         };

//     public void Switch(
//         Action<T1> case1,
//         Action<T2> case2,
//         Action<T3> case3,
//         Action<T4> case4
//     ) {
//         switch (this) {
//             case T1 v: case1(v); break;

//             case T2 v: case2(v); break;

//             case T3 v: case3(v); break;

//             case T4 v: case4(v); break;

//             default: throw new UnreachableException();
//         }
//     }

//     public sealed override string ToString() {
//         var sb = new StringBuilder();
//         sb.Append(UnionName.Of(typeof(TSelf)));
//         sb.Append('.');
//         sb.Append(GetType().Name);
//         sb.Append(" { ");
//         if (PrintMembers(sb)) sb.Append(' ');
//         sb.Append('}');
//         return sb.ToString();
//     }
// }

// public abstract record Union<TSelf, T1, T2, T3, T4, T5>
//     where TSelf : Union<TSelf, T1, T2, T3, T4, T5>
//     where T1 : class, TSelf
//     where T2 : class, TSelf
//     where T3 : class, TSelf
//     where T4 : class, TSelf
//     where T5 : class, TSelf {
//     public TResult Match<TResult>(
//         Func<T1, TResult> case1,
//         Func<T2, TResult> case2,
//         Func<T3, TResult> case3,
//         Func<T4, TResult> case4,
//         Func<T5, TResult> case5
//     ) =>
//         this switch {
//             T1 v => case1(v),
//             T2 v => case2(v),
//             T3 v => case3(v),
//             T4 v => case4(v),
//             T5 v => case5(v),
//             _    => throw new UnreachableException()
//         };

//     public void Switch(
//         Action<T1> case1,
//         Action<T2> case2,
//         Action<T3> case3,
//         Action<T4> case4,
//         Action<T5> case5
//     ) {
//         switch (this) {
//             case T1 v: case1(v); break;

//             case T2 v: case2(v); break;

//             case T3 v: case3(v); break;

//             case T4 v: case4(v); break;

//             case T5 v: case5(v); break;

//             default: throw new UnreachableException();
//         }
//     }

//     public sealed override string ToString() {
//         var sb = new StringBuilder();
//         sb.Append(UnionName.Of(typeof(TSelf)));
//         sb.Append('.');
//         sb.Append(GetType().Name);
//         sb.Append(" { ");
//         if (PrintMembers(sb)) sb.Append(' ');
//         sb.Append('}');
//         return sb.ToString();
//     }
// }

// file static class UnionName {
//     internal static string Of(Type type) {
//         if (!type.IsGenericType)
//             return type.Name;

//         var name     = type.Name;
//         var backtick = name.IndexOf('`');

//         if (backtick > 0)
//             name = name[..backtick];

//         return $"{name}<{string.Join(", ", type.GetGenericArguments().Select(Of))}>";
//     }
// }


// // public partial record Shape
// // {
// //     public sealed record Circle(double    Radius) : Shape;
// //     public sealed record Rectangle(double Width, double Height) : Shape;
// //     public sealed record Triangle(double  Base,  double Height) : Shape;
// // }
// //
// // public class kebas {
// //     public kebas() {
// //         Shape s = new Shape.Circle(1.0);
// //
// //
// //
// //     }
// //
// //
// // }

// // public partial record Shape : IUnion<Circle, Rectangle, Triangle>;
// //
// // public sealed record Circle(double Radius);
// // public sealed record Rectangle(double Width, double Height);
// // public sealed record Triangle(double  Base,  double Height);

// // public class UnionAttribute<T1, T2, T3> : Attribute {
// //     public UnionAttribute() { }
// // }
// //
// //
// // [Union<Circle, Rectangle, Triangle>]
// // public partial record Shape;
// //
// // public readonly record struct Circle(double Radius);
// // public readonly record struct Rectangle(double Width, double Height);
// // public readonly record struct Triangle(double Base, double Height);

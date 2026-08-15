using MessagePack;
using MessagePack.Resolvers;

namespace Kurrent.Surge.Client.Schema.Serialization.MessagePack;

/// <summary>
/// Options for <see cref="MessagePackSchemaCodec"/>.
/// </summary>
/// <remarks>
/// <para>
/// This SDK enforces a zero-reflection, zero-dynamic-codegen policy for Native AOT and trimming.
/// MessagePack-CSharp's <see cref="StandardResolver"/> and <see cref="ContractlessStandardResolver"/> silently
/// fall back to <c>System.Reflection.Emit</c> dynamic formatter generation for any <c>[MessagePackObject]</c>
/// type that has no source-generated or hand-written formatter. Neither is ever used here.
/// </para>
/// <para>
/// The SDK's message catalog lets consumers register arbitrary POCOs at runtime
/// (<c>MessageCatalog.Map&lt;T&gt;()</c>) — types this package has never seen at its own compile time — so it
/// cannot bake in a fixed resolver for them. Run the MessagePack source generator
/// (the <c>MessagePackAnalyzer</c>/<c>mpc</c> tool) in your own project: it emits a partial class decorated with
/// <c>[GeneratedMessagePackResolver]</c> covering every <c>[MessagePackObject]</c> type it sees there, and
/// supply that resolver's <c>Instance</c> through <see cref="UserResolvers"/>. A type with no formatter
/// anywhere in the resulting chain throws <see cref="FormatterNotRegisteredException"/> — it is never silently
/// reflected.
/// </para>
/// </remarks>
[PublicAPI]
public record MessagePackSchemaSerializerOptions : SchemaSerializerOptions {
    public static MessagePackSchemaSerializerOptions Default => new();

    static readonly MessagePackSerializerOptions DefaultMessagePackSerializerOptions = BuildOptions([]);

    /// <summary>
    /// Consumer-supplied formatter resolvers — typically the <c>Instance</c> of the partial class your own
    /// project generates via <c>[GeneratedMessagePackResolver]</c>. Checked in order, ahead of the built-in
    /// resolvers for primitives and common BCL types (<see cref="DateTime"/>, <see cref="Guid"/>,
    /// <see cref="decimal"/>). Never add <see cref="StandardResolver"/> or
    /// <see cref="ContractlessStandardResolver"/> here — both fall back to runtime codegen for uncovered types,
    /// breaking Native AOT.
    /// </summary>
    public IReadOnlyList<IFormatterResolver> UserResolvers { get; set; } = [];

    public MessagePackSerializerOptions MessagePackSerializerOptions =>
        UserResolvers.Count == 0
            ? DefaultMessagePackSerializerOptions
            : BuildOptions(UserResolvers);

    static MessagePackSerializerOptions BuildOptions(IReadOnlyList<IFormatterResolver> userResolvers) {
        IFormatterResolver[] resolvers = [
            ..userResolvers,
            BuiltinResolver.Instance,
            NativeDateTimeResolver.Instance,
            NativeGuidResolver.Instance,
            NativeDecimalResolver.Instance
        ];

        return MessagePackSerializerOptions.Standard.WithResolver(CompositeResolver.Create(resolvers));
    }
}

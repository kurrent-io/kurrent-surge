using Kurrent.Client.Schema.NameStrategies;

namespace Kurrent.Client.Schema;

/// <summary>
/// Declares a message catalog built by scanning a namespace at compile time. The message
/// catalog source generator collects every matching type into a <see cref="MessageCatalog"/>
/// exposed via the generated <c>MessageCatalogs</c> class, a <c>Use*Catalog()</c> extension
/// method, and the assembly's catalog provider for automatic discovery.
/// </summary>
/// <param name="namespacePrefix">
/// Namespace prefix to scan. Every non-abstract class, record, or struct whose full name
/// starts with this prefix joins the catalog.
/// </param>
/// <example>
/// <code>
/// [assembly: MessageCatalog("Acme.Orders", Key = "orders")]
/// [assembly: MessageCatalog("Acme.Billing", Key = "billing", Strategy = typeof(AcmeNames))]
/// [assembly: MessageCatalog&lt;IDomainEvent&gt;("Acme.Audit", Key = "audit")]
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public class MessageCatalogAttribute(string namespacePrefix) : Attribute {
    public string NamespacePrefix { get; } = namespacePrefix;

    /// <summary>
    /// Catalog key; also names the generated catalog property. Omitted → the "Default" catalog.
    /// </summary>
    public string? Key { get; init; }

    /// <summary>
    /// Naming strategy applied to every type in this scan. Must implement
    /// <see cref="ISchemaNameStrategy"/> with a parameterless constructor.
    /// Omitted → the default <see cref="MessageSchemaNameStrategy"/> at resolution.
    /// </summary>
    public Type? Strategy { get; init; }

    /// <summary>
    /// When true, the generated catalog registers into every client's resolver by default,
    /// instead of being opt-in via <see cref="KurrentClientMessageOptions.UseCatalog"/>.
    /// Surfaces as <see cref="MessageCatalog.AlwaysActive"/> on the generated catalog.
    /// </summary>
    public bool AlwaysActive { get; init; }

    /// <summary>
    /// Constrains the scan to types implementing or deriving from this type — the non-generic
    /// equivalent of <see cref="MessageCatalogAttribute{TMustImplement}"/>. Omitted → no constraint.
    /// </summary>
    public Type? MustImplement { get; init; }
}

/// <summary>
/// Namespace scan constrained to types implementing or deriving from
/// <typeparamref name="TMustImplement"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class MessageCatalogAttribute<TMustImplement>(string namespacePrefix)
    : MessageCatalogAttribute(namespacePrefix);

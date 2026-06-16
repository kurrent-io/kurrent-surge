namespace Kurrent.Schema.Catalogs;

/// <summary>
/// Marks a concrete <see cref="MessageCatalog"/> subclass for automatic discovery — the
/// hand-authored counterpart to <see cref="MessageCatalogAttribute"/>'s namespace scan. The
/// message catalog source generator exposes a single instance through the generated
/// <c>MessageCatalogs</c> class, a <c>Use*Catalog()</c> extension method, and the assembly's
/// catalog provider, so the catalog is picked up at client construction with zero manual
/// registration.
/// </summary>
/// <remarks>
/// The target must be a non-abstract class deriving from <see cref="MessageCatalog"/> with an
/// accessible parameterless constructor — the primary-constructor form
/// <c>class Orders() : MessageCatalog("orders", c => c.Map&lt;OrderPlaced&gt;("order-placed"))</c>
/// satisfies this. A misuse is reported as a compile-time diagnostic (KURRENT521).
/// To make the catalog always-active, pass <c>alwaysActive: true</c> to the base constructor —
/// the catalog instance carries the flag, so the generator just instantiates it.
/// The generated member names derive from the type name minus a trailing <c>Catalog</c> /
/// <c>MessageCatalog</c> suffix: <c>OrdersCatalog</c> yields <c>MessageCatalogs.Orders</c>
/// and <c>UseOrdersCatalog()</c>.
/// </remarks>
/// <example>
/// <code>
/// [CustomMessageCatalog]
/// sealed class OrdersCatalog() : MessageCatalog("orders", c => c
///     .Map&lt;OrderPlaced&gt;("order-placed")
///     .Map&lt;OrderShipped&gt;("order-shipped"));
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class CustomMessageCatalogAttribute : Attribute;

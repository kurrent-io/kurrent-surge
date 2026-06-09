namespace Kurrent.Client.Schema;

/// <summary>
/// Marks a concrete <see cref="MessageCatalog"/> subclass for automatic discovery — the
/// hand-authored counterpart to <see cref="MessageCatalogAttribute"/>'s namespace scan. The
/// message catalog source generator emits a <c>new T()</c> into this assembly's catalog provider,
/// so the catalog is picked up at client construction with zero manual registration.
/// </summary>
/// <remarks>
/// The target must be a non-abstract class deriving from <see cref="MessageCatalog"/> with an
/// accessible parameterless constructor — the primary-constructor form
/// <c>class Orders() : MessageCatalog("orders", c => c.Map&lt;OrderPlaced&gt;("order-placed"))</c>
/// satisfies this. A misuse is reported as a compile-time diagnostic (KURRENT521).
/// To make the catalog always-active, pass <c>alwaysActive: true</c> to the base constructor —
/// the catalog instance carries the flag, so the generator just instantiates it.
/// </remarks>
/// <example>
/// <code>
/// [CustomMessageCatalog]
/// sealed class OrdersCatalog() : MessageCatalog("orders", c => c
///     .Map&lt;OrderPlaced&gt;("order-placed")
///     .Map&lt;OrderShipped&gt;("order-shipped"));
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class CustomMessageCatalogAttribute : Attribute;

namespace Kurrent.Client.Schema.NameStrategies;

/// <summary>
/// Provides factory methods for creating schema name strategies.
/// </summary>
/// <example>
/// <code>
/// new MessageCatalog("orders")
///     .Map&lt;UserCreated&gt;(SchemaNameStrategies.Namespace("acme"))
///     .Map&lt;OrderPlaced&gt;(SchemaNameStrategies.Namespace("acme"));
/// </code>
/// </example>
public static class SchemaNameStrategies {
    /// <summary>
    /// Creates a strategy that generates schema names from the message type's full name.
    /// </summary>
    /// <param name="format">The output format for the schema name.</param>
    /// <returns>A schema name strategy based on the type name.</returns>
    /// <example>
    /// <code>
    /// // For type "Acme.Orders.OrderPlaced" generates "Acme.Orders.OrderPlaced"
    /// SchemaNameStrategies.TypeName()
    /// </code>
    /// </example>
    public static ISchemaNameStrategy TypeName(SchemaNameOutputFormat format = SchemaNameOutputFormat.None) =>
        new MessageSchemaNameStrategy(format);

    /// <summary>
    /// Creates a strategy that generates schema names from a custom namespace and message type name.
    /// </summary>
    /// <param name="namespaceIdentifier">The namespace identifier to use.</param>
    /// <param name="format">The output format for the schema name.</param>
    /// <returns>A schema name strategy based on the custom namespace.</returns>
    /// <example>
    /// <code>
    /// // For namespace "acme" and type "OrderPlaced" generates "acme.OrderPlaced"
    /// SchemaNameStrategies.Namespace("acme")
    /// </code>
    /// </example>
    public static ISchemaNameStrategy Namespace(string namespaceIdentifier, SchemaNameOutputFormat format = SchemaNameOutputFormat.None) =>
        new NamespaceSchemaNameStrategy(namespaceIdentifier, format);
}

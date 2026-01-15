namespace Kurrent.Client.Schema.NameStrategies;

/// <summary>
/// Provides factory methods for creating schema name strategies.
/// </summary>
/// <example>
/// <code>
/// builder.WithMessages(mapper => mapper
///     .WithStrategy(SchemaNameStrategies.Category())
///     .Map&lt;UserCreated&gt;()
///     .Map&lt;OrderPlaced&gt;());
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
    /// Creates a strategy that generates schema names from the stream category and message type name.
    /// </summary>
    /// <param name="format">The output format for the schema name.</param>
    /// <returns>A schema name strategy based on the stream category.</returns>
    /// <example>
    /// <code>
    /// // For stream "order-123" and type "OrderPlaced" generates "order.OrderPlaced"
    /// SchemaNameStrategies.Category()
    /// </code>
    /// </example>
    public static ISchemaNameStrategy Category(SchemaNameOutputFormat format = SchemaNameOutputFormat.None) =>
        new CategorySchemaNameStrategy(format);

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

    /// <summary>
    /// Creates a strategy that generates schema names from a custom namespace, stream category, and message type name.
    /// </summary>
    /// <param name="namespaceIdentifier">The namespace identifier to use.</param>
    /// <param name="format">The output format for the schema name.</param>
    /// <returns>A schema name strategy based on namespace and category.</returns>
    /// <example>
    /// <code>
    /// // For namespace "acme", stream "order-123", and type "OrderPlaced" generates "acme.order.OrderPlaced"
    /// SchemaNameStrategies.NamespaceCategory("acme")
    /// </code>
    /// </example>
    public static ISchemaNameStrategy NamespaceCategory(string namespaceIdentifier, SchemaNameOutputFormat format = SchemaNameOutputFormat.None) =>
        new NamespaceCategorySchemaNameStrategy(namespaceIdentifier, format);
}

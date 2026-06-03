using Kurrent.Client.Registry;
using Kurrent.Client.Schema.NameStrategies;
using Kurrent.Variant;

namespace Kurrent.Client.Schema;

public partial record MapMessageTypeResult : IResultBase<Success, MapMessageTypeError>;

[PublicAPI]
public partial class MapMessageTypeError : IVariantException<
	MessageTypeConflictException>;

/// <summary>
/// Responsible for mapping message types to schema names and managing the bidirectional mapping
/// between schemas and message types. Provides methods for mapping, retrieving, and verifying
/// mappings between schema names and message types.
/// </summary>
public class MessageTypeMapper {
	static readonly Type Missing = Type.Missing.GetType();

	ConcurrentBidirectionalDictionary<string, Type> TypeMap { get; } = new();

    /// <summary>
    /// Attempts to add a mapping between a schema name and a message type.
    /// </summary>
    /// <param name="schemaName">The schema name to be mapped.</param>
    /// <param name="messageType">The message type to associate with the schema name.</param>
    public MapMessageTypeResult Map(SchemaName schemaName, Type messageType) {
        if (TypeMap.TryAdd(schemaName, messageType))
            return Results.Success;

        if (TypeMap.TryGetValue(schemaName, out var mappedType))
            return mappedType != messageType
	            ? new MessageTypeConflictException(schemaName, mappedType, messageType)
	            : Results.Success;

        TypeMap.TryGetKey(messageType, out var existingName);
        return new MessageTypeConflictException(existingName, messageType, messageType);
    }

    /// <summary>
    /// Attempts to add a mapping between a schema name and a message type.
    /// </summary>
    /// <param name="schemaName">The schema name to be mapped.</param>
    /// <typeparam name="T">
    /// The type to associate with the schema name. The <see cref="Type"/> of <typeparamref name="T"/>
    /// </typeparam>
    public MapMessageTypeResult Map<T>(SchemaName schemaName) => Map(schemaName, typeof(T));

    public MapMessageTypeResult Map<T>() => Map(SchemaName.From(typeof(T).FullName!), typeof(T));


    /// <summary>
    /// Attempts to add a mapping between a schema name and a message type.
	/// </summary>
	/// <param name="schemaName">The schema name to map.</param>
	/// <param name="messageType">The message type to map to the schema name.</param>
	/// <returns>
	/// <c>true</c> if the mapping was added successfully; <c>false</c> if the
	/// schema name already has a mapping.
	/// </returns>
	public bool TryMap(SchemaName schemaName, Type messageType) {
		if (TypeMap.TryAdd(schemaName, messageType)) return true;

		if (TypeMap.TryGetValue(schemaName, out var mappedType))
			return mappedType != messageType
				? throw new MessageTypeConflictException(schemaName, mappedType, messageType)
				: false;

		TypeMap.TryGetKey(messageType, out var existingName);
		throw new MessageTypeConflictException(existingName, messageType, messageType);
    }

	/// <summary>
	/// Attempts to add a mapping between a schema name and a message type.
	/// </summary>
	/// <typeparam name="T">The type to map to the schema name if no mapping exists.</typeparam>
	/// <param name="schemaName">The schema name to map.</param>
	/// <returns>
	/// <c>true</c> if the mapping was added successfully; <c>false</c> if the
	/// schema name already has a mapping.
	/// </returns>
	public bool TryMap<T>(SchemaName schemaName) =>
		TryMap(schemaName, typeof(T));

	/// <summary>
	/// Attempts to retrieve the message type associated with the specified schema name.
	/// </summary>
	/// <param name="schemaName">The schema name to look up.</param>
	/// <param name="messageType">
	/// When this method returns, contains the message type associated with the specified schema name,
	/// or <see cref="Missing"/> if the schema name is not found.
	/// </param>
	/// <returns>
	/// <c>true</c> if the schema name was found in the type map; otherwise, <c>false</c>.
	/// </returns>
	public bool TryGetMessageType(SchemaName schemaName, out Type messageType) {
		if (TypeMap.TryGetValue(schemaName, out var mappedType)) {
			messageType = mappedType;
			return true;
		}

		messageType = Missing;
		return false;
	}

	/// <summary>
	/// Attempts to retrieve the schema name associated with the specified message type.
	/// </summary>
	/// <param name="messageType">The message type to look up.</param>
	/// <param name="schemaName">
	/// When this method returns, contains the schema name associated with the specified message type,
	/// or <see cref="SchemaName.Unset"/> if the message type is not found.
	/// </param>
	/// <returns>
	/// <c>true</c> if the message type was found in the type map; otherwise, <c>false</c>.
	/// </returns>
	public bool TryGetSchemaName(Type messageType, out SchemaName schemaName) {
		if (TypeMap.TryGetKey(messageType, out var mappedName)) {
			schemaName = mappedName;
			return true;
		}

		schemaName = SchemaName.Unset;
		return false;
	}

	/// <summary>
	/// Retrieves the message type associated with the specified schema name.
	/// </summary>
	/// <param name="schemaName">The schema name to look up.</param>
	/// <param name="throwWhenMissing">
	/// Determines whether to throw an exception when the schema name is not found.
	/// If <c>false</c>, returns <see cref="Missing"/> instead.
	/// </param>
	/// <returns>
	/// The message type associated with the specified schema name, or <see cref="Missing"/> if
	/// the schema name is not found and <paramref name="throwWhenMissing"/> is <c>false</c>.
	/// </returns>
	/// <exception cref="SchemaNameMapNotFoundException">
	/// Thrown when the schema name is not found and <paramref name="throwWhenMissing"/> is <c>true</c>.
	/// </exception>
	public Type GetMessageType(string schemaName, bool throwWhenMissing = true) =>
        TypeMap.TryGetValue(schemaName, out var messageType)
            ? messageType
            : throwWhenMissing
                ? throw new SchemaNameMapNotFoundException(schemaName)
                : Missing;

    /// <summary>
	/// Retrieves the schema name associated with the specified message type.
	/// </summary>
	/// <param name="messageType">The message type to look up.</param>
	/// <param name="throwWhenMissing">
	/// Determines whether to throw an exception when the message type is not found.
	/// If <c>false</c>, returns <see cref="SchemaName.Unset"/> instead.
	/// </param>
	/// <returns>
	/// The schema name associated with the specified message type, or <see cref="SchemaName.Unset"/> if
	/// the message type is not found and <paramref name="throwWhenMissing"/> is <c>false</c>.
	/// </returns>
	/// <exception cref="MessageTypeMapNotFoundException">
	/// Thrown when the message type is not found and <paramref name="throwWhenMissing"/> is <c>true</c>.
	/// </exception>
	public SchemaName GetSchemaName(Type messageType, bool throwWhenMissing = true) =>
        TypeMap.TryGetKey(messageType, out var schemaName)
            ? schemaName
            : throwWhenMissing
                ? throw new MessageTypeMapNotFoundException(messageType)
                : SchemaName.Unset;

    /// <summary>
	/// Retrieves the schema name associated with the specified message type,
	/// or returns the provided default schema name if the message type is not found.
	/// </summary>
	/// <param name="messageType">The message type to look up.</param>
	/// <param name="defaultSchemaName">The default schema name to return if the message type is not found.</param>
	/// <returns>
	/// The schema name associated with the specified message type, or the specified default schema name
	/// if the message type is not found.
	/// </returns>
	public SchemaName GetSchemaNameOrDefault(Type messageType, SchemaName defaultSchemaName) =>
		TypeMap.TryGetKey(messageType, out var schemaName) ? schemaName : defaultSchemaName;

	/// <summary>
	/// Determines whether the specified message type is mapped to any schema name.
	/// </summary>
	/// <param name="messageType">The message type to check.</param>
	/// <returns>
	/// <c>true</c> if the message type is mapped to a schema name; otherwise, <c>false</c>.
	/// </returns>
	public bool ContainsMapTo(Type messageType) =>
		TypeMap.ContainsValue(messageType);

	/// <summary>
	/// Determines whether the specified schema name is mapped to any message type.
	/// </summary>
	/// <param name="schemaName">The schema name to check.</param>
	/// <returns>
	/// <c>true</c> if the schema name is mapped to a message type; otherwise, <c>false</c>.
	/// </returns>
	public bool ContainsMapTo(SchemaName schemaName) =>
		TypeMap.ContainsKey(schemaName);

	/// <summary>
	/// Resolves the message type associated with the specified schema name.
	/// Types must be explicitly registered via <see cref="Map"/> or auto-mapped during serialization.
	/// </summary>
	/// <param name="schemaName">The schema name for which the message type should be resolved.</param>
	/// <returns>The resolved message type associated with the specified schema name.</returns>
	/// <exception cref="MessageTypeResolutionException">
	/// Thrown if the schema name is not mapped to any known type.
	/// </exception>
	public Type GetOrResolveMessageType(SchemaName schemaName) =>
		TryGetMessageType(schemaName, out var messageType)
			? messageType
			: throw new MessageTypeResolutionException(schemaName);

    /// <summary>
    /// Creates a new <see cref="MessageTypeMapper"/> via a fluent builder action.
    /// When <paramref name="configure"/> is null, returns an empty mapper.
    /// </summary>
    /// <example>
    /// <code>
    /// var mapper = MessageTypeMapper.Create(m => m
    ///     .Map&lt;OrderPlaced&gt;()
    ///     .Map&lt;OrderShipped&gt;("order.shipped.v1"));
    /// </code>
    /// </example>
    public static MessageTypeMapper Create(Action<Builder>? configure = null) {
        var builder = new Builder();
        configure?.Invoke(builder);
        return builder.Build();
    }

    /// <summary>
    /// A builder for configuring message type mappings.
    /// </summary>
    public class Builder {
        MessageTypeMapper   Mapper       { get; }      = new();
        ISchemaNameStrategy NameStrategy { get; set; } = new MessageSchemaNameStrategy();

        /// <summary>
        /// Sets the naming strategy to use for automatic schema name generation.
        /// </summary>
        public Builder WithStrategy(ISchemaNameStrategy strategy) {
            NameStrategy = strategy ?? throw new ArgumentNullException(nameof(strategy));
            return this;
        }

        /// <summary>
        /// Maps a message type to a schema name.
        /// </summary>
        public Builder Map<T>(string? schemaName = null) {
            var type = typeof(T);
            var name = schemaName ?? NameStrategy.GenerateSchemaName(type);
            Mapper.TryMap(name, type);
            return this;
        }

        /// <summary>
        /// Attempts to map a message type to a schema name.
        /// </summary>
        public Builder TryMap<T>(string? schemaName = null) {
            var type = typeof(T);
            var name = schemaName ?? NameStrategy.GenerateSchemaName(type);
            Mapper.TryMap(name, type);
            return this;
        }

        public MessageTypeMapper Build() => Mapper;
    }
}

using Kurrent.Schema;

#pragma warning disable CS8524 // The switch expression does not handle some values of its input type (it is not exhaustive) involving an unnamed enum value.

namespace Kurrent.Schema.Naming;

/// <summary>
/// Defines the format options for schema name generation, which determine
/// how schema names are composed and represented.
/// </summary>
public enum SchemaNameOutputFormat {
	None      = 0,
	KebabCase = 1,
	SnakeCase = 2,
	Urn       = 3
}

public interface ISchemaNameStrategy {
	/// <summary>
	/// Generates a schema name based on the specified message type,
	/// formatted according to the schema naming strategy.
	/// </summary>
	/// <param name="messageType">The type of the message for which the schema name is being generated. Must not be null.</param>
	/// <returns>The generated schema name as a string, formatted according to the specified schema name format.</returns>
	/// <exception cref="ArgumentNullException">Thrown when the provided message type is null.</exception>
	/// <exception cref="ArgumentOutOfRangeException">Thrown when the schema name format is unsupported.</exception>
	SchemaName GenerateSchemaName(Type messageType);

    SchemaName GenerateSchemaName<T>() =>
        GenerateSchemaName(typeof(T));
}

/// <summary>
/// Base class for schema naming strategies
/// </summary>
public abstract class SchemaNameStrategyBase(SchemaNameOutputFormat format) : ISchemaNameStrategy {
	// Runtime type of System.Reflection.Missing.Value — the sentinel a "no type" reflection result
	// yields. Reproduced locally (was Type.MissingType, a Kurrent.Client internal extension) so the
	// naming layer stays BCL-only after the extraction into Kurrent.Schema.Catalogs.
	static readonly Type MissingType = Type.Missing.GetType();

	/// <summary>
	/// Generates a schema name based on the specified message type,
	/// formatted according to the schema naming strategy.
	/// </summary>
	/// <param name="messageType">The type of the message for which the schema name is being generated. Must not be null.</param>
	/// <returns>The generated schema name as a string, formatted according to the specified schema name format.</returns>
	/// <exception cref="ArgumentNullException">Thrown when the provided message type is null.</exception>
	/// <exception cref="ArgumentOutOfRangeException">Thrown when the schema name format is unsupported.</exception>
	public SchemaName GenerateSchemaName(Type messageType) {
		if (messageType is null || messageType == MissingType)
			throw new ArgumentNullException(nameof(messageType));

		var (ns, msg) = Generate(messageType);

		if (string.IsNullOrEmpty(ns)) {
			return format switch {
				SchemaNameOutputFormat.None      => msg,
				SchemaNameOutputFormat.KebabCase => msg.ToKebabCase(),
				SchemaNameOutputFormat.SnakeCase => msg.ToSnakeCase(),
				SchemaNameOutputFormat.Urn       => SchemaUrn.Create(msg.ToKebabCase())
			};
		}

		return format switch {
			SchemaNameOutputFormat.None      => $"{ns}.{msg}",
			SchemaNameOutputFormat.KebabCase => $"{ns.ToKebabCase()}.{msg.ToKebabCase()}",
			SchemaNameOutputFormat.SnakeCase => $"{ns.ToSnakeCase()}.{msg.ToSnakeCase()}",
			SchemaNameOutputFormat.Urn       => SchemaUrn.Create(ns.ToKebabCase(), msg.ToKebabCase())
		};
	}

	protected abstract (string Namespace, string MessageName) Generate(Type messageType);
}

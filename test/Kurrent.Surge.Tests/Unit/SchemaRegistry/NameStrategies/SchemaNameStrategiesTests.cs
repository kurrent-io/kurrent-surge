using Kurrent.Schema.Naming;
using static Kurrent.Schema.Naming.SchemaNameOutputFormat;

namespace Kurrent.Surge.Tests.SchemaRegistry.NameStrategies;

public class SchemaNameStrategiesTests {
	# region . happy path .

	[Test]
	[Arguments(SchemaNameOutputFormat.None, "Company.Logistics.Events.OrderShipped")]
	[Arguments(KebabCase, "company.logistics.events.order-shipped")]
	[Arguments(SnakeCase, "company.logistics.events.order_shipped")]
	[Arguments(Urn, "urn:schemas-kurrent:company.logistics.events:order-shipped")]
	public async Task message_schema_name_strategy_generates_correct_name(SchemaNameOutputFormat format, string streamName) {
		var strategy    = new MessageSchemaNameStrategy(format);
		var messageType = typeof(Company.Logistics.Events.OrderShipped);
		var result      = strategy.GenerateSchemaName(messageType);
		await Assert.That(result.Value).IsEqualTo(streamName);
	}

	[Test]
	[Arguments(SchemaNameOutputFormat.None, "com.company.logistics.OrderShipped")]
	[Arguments(KebabCase, "com.company.logistics.order-shipped")]
	[Arguments(SnakeCase, "com.company.logistics.order_shipped")]
	[Arguments(Urn, "urn:schemas-kurrent:com.company.logistics:order-shipped")]
	public async Task namespace_schema_name_strategy_generates_correct_name(SchemaNameOutputFormat format, string expectedName) {
		var strategy    = new NamespaceSchemaNameStrategy("com.company.logistics", format);
		var messageType = typeof(Company.Logistics.Events.OrderShipped);
		var result      = strategy.GenerateSchemaName(messageType);
		await Assert.That(result.Value).IsEqualTo(expectedName);
	}

	# endregion

	#region . edge cases .

	[Test]
	public async Task message_schema_name_strategy_generates_correct_name_with_nested_types() {
		var strategy    = new MessageSchemaNameStrategy();
		var messageType = typeof(Company.Logistics.Events.OrderShipped.Address);
		var result      = strategy.GenerateSchemaName(messageType);
		await Assert.That(result.Value).IsEqualTo("Company.Logistics.Events.Address");
	}

	[Test]
	public async Task namespace_schema_name_strategy_throws_on_empty_namespace() {
		await Assert.That(() => new NamespaceSchemaNameStrategy(" ")).ThrowsExactly<ArgumentException>();
	}

	[Test]
	[Arguments(SchemaNameOutputFormat.None, "GlobalItemAdded")]
	[Arguments(KebabCase, "global-item-added")]
	[Arguments(SnakeCase, "global_item_added")]
	[Arguments(Urn, "urn:schemas-kurrent:global:global-item-added")]
	public async Task message_schema_name_strategy_handles_types_without_namespace(SchemaNameOutputFormat format, string expectedName) {
		var strategy    = new MessageSchemaNameStrategy(format);
		var messageType = typeof(GlobalItemAdded);
		var result      = strategy.GenerateSchemaName(messageType);
		await Assert.That(result.Value).IsEqualTo(expectedName);
	}

	#endregion
}

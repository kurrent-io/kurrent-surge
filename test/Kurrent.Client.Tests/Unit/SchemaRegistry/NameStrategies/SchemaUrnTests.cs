using SchemaUrn = Kurrent.Client.Schema.NameStrategies.SchemaUrn;

namespace Kurrent.Client.Tests.SchemaRegistry.NameStrategies;

public class SchemaUrnTests {
	const string ValidNamespace   = "company.department.service";
	const string ValidMessageName = "resource.event";

	static readonly Guid ValidGuid = Guid.Parse("12345678-1234-1234-1234-123456789012");

	[Test]
	public async Task creates_valid_urn_with_all_components() {
		var urn = SchemaUrn.Create(ValidNamespace, ValidMessageName, ValidGuid);

		await Assert.That(urn.Namespace).IsEqualTo(ValidNamespace);
		await Assert.That(urn.MessageName).IsEqualTo(ValidMessageName);
		await Assert.That(urn.SchemaGuid).IsEqualTo(ValidGuid);
		await Assert.That(urn.Value).IsEqualTo($"urn:schemas-kurrent:{ValidNamespace}:{ValidMessageName}:{ValidGuid}");
	}

	[Test]
	public async Task creates_valid_urn_without_guid() {
		var urn = SchemaUrn.Create(ValidNamespace, ValidMessageName);

		await Assert.That(urn.Namespace).IsEqualTo(ValidNamespace);
		await Assert.That(urn.MessageName).IsEqualTo(ValidMessageName);
		await Assert.That(urn.SchemaGuid).IsNull();
		await Assert.That(urn.Value).IsEqualTo($"urn:schemas-kurrent:{ValidNamespace}:{ValidMessageName}");
	}

	[Test]
	public async Task throws_argument_exception_when_namespace_is_empty() {
		await Assert.That(() => SchemaUrn.Create("", ValidMessageName)).ThrowsExactly<ArgumentException>();
	}

	[Test]
	public async Task throws_argument_exception_when_namespace_is_whitespace() {
		await Assert.That(() => SchemaUrn.Create("   ", ValidMessageName)).ThrowsExactly<ArgumentException>();
	}

	[Test]
	public async Task throws_argument_exception_when_namespace_contains_separator() {
		await Assert.That(() => SchemaUrn.Create("company:department", ValidMessageName)).ThrowsExactly<ArgumentException>();
	}

	[Test]
	public async Task throws_argument_exception_when_message_name_is_empty() {
		await Assert.That(() => SchemaUrn.Create(ValidNamespace, "")).ThrowsExactly<ArgumentException>();
	}

	[Test]
	public async Task throws_argument_exception_when_message_name_is_whitespace() {
		await Assert.That(() => SchemaUrn.Create(ValidNamespace, "   ")).ThrowsExactly<ArgumentException>();
	}

	[Test]
	public async Task throws_argument_exception_when_message_name_contains_separator() {
		await Assert.That(() => SchemaUrn.Create(ValidNamespace, "message:name")).ThrowsExactly<ArgumentException>();
	}

	[Test]
	public async Task parse_returns_valid_urn_when_input_has_guid() {
		var urnString = $"urn:schemas-kurrent:{ValidNamespace}:{ValidMessageName}:{ValidGuid}";

		var urn = SchemaUrn.Parse(urnString);

		await Assert.That(urn.Namespace).IsEqualTo(ValidNamespace);
		await Assert.That(urn.MessageName).IsEqualTo(ValidMessageName);
		await Assert.That(urn.SchemaGuid).IsEqualTo(ValidGuid);
		await Assert.That(urn.Value).IsEqualTo(urnString);
	}

	[Test]
	public async Task parse_returns_valid_urn_when_input_has_no_guid() {
		var urnString = $"urn:schemas-kurrent:{ValidNamespace}:{ValidMessageName}";

		var urn = SchemaUrn.Parse(urnString);

		await Assert.That(urn.Namespace).IsEqualTo(ValidNamespace);
		await Assert.That(urn.MessageName).IsEqualTo(ValidMessageName);
		await Assert.That(urn.SchemaGuid).IsNull();
		await Assert.That(urn.Value).IsEqualTo(urnString);
	}

	[Test]
	public async Task parse_throws_argument_exception_when_input_is_empty() {
		await Assert.That(() => SchemaUrn.Parse("")).ThrowsExactly<ArgumentException>();
	}

	[Test]
	public async Task parse_throws_argument_exception_when_input_is_whitespace() {
		await Assert.That(() => SchemaUrn.Parse("   ")).ThrowsExactly<ArgumentException>();
	}

	[Test]
	public async Task parse_throws_format_exception_when_format_is_invalid() {
		await Assert.That(() => SchemaUrn.Parse("invalid-format")).ThrowsExactly<FormatException>();
	}

	[Test]
	public async Task parse_throws_format_exception_when_prefix_is_invalid() {
		await Assert.That(() => SchemaUrn.Parse($"urn:wrong-prefix:{ValidNamespace}:{ValidMessageName}")).ThrowsExactly<FormatException>();
	}

	[Test]
	public async Task parse_throws_format_exception_when_guid_format_is_invalid() {
		await Assert.That(() => SchemaUrn.Parse($"urn:schemas-kurrent:{ValidNamespace}:{ValidMessageName}:not-a-guid")).ThrowsExactly<FormatException>();
	}

	[Test]
	public async Task try_parse_returns_true_and_urn_when_input_has_guid() {
		var urnString = $"urn:schemas-kurrent:{ValidNamespace}:{ValidMessageName}:{ValidGuid}";

		var success = SchemaUrn.TryParse(urnString, out var urn);

		await Assert.That(success).IsTrue();
		await Assert.That(urn).IsNotNull();
		await Assert.That(urn.Namespace).IsEqualTo(ValidNamespace);
		await Assert.That(urn.MessageName).IsEqualTo(ValidMessageName);
		await Assert.That(urn.SchemaGuid).IsEqualTo(ValidGuid);
		await Assert.That(urn.Value).IsEqualTo(urnString);
	}

	[Test]
	public async Task try_parse_returns_true_and_urn_when_input_has_no_guid() {
		var urnString = $"urn:schemas-kurrent:{ValidNamespace}:{ValidMessageName}";

		var success = SchemaUrn.TryParse(urnString, out var urn);

		await Assert.That(success).IsTrue();
		await Assert.That(urn).IsNotNull();
		await Assert.That(urn.Namespace).IsEqualTo(ValidNamespace);
		await Assert.That(urn.MessageName).IsEqualTo(ValidMessageName);
		await Assert.That(urn.SchemaGuid).IsNull();
		await Assert.That(urn.Value).IsEqualTo(urnString);
	}

	[Test]
	public async Task try_parse_returns_false_when_input_is_empty() {
		var success = SchemaUrn.TryParse("", out var urn);

		await Assert.That(success).IsFalse();
		await Assert.That((object?)urn).IsNull();
	}

	[Test]
	public async Task try_parse_returns_false_when_format_is_invalid() {
		var success = SchemaUrn.TryParse("invalid-format", out var urn);

		await Assert.That(success).IsFalse();
		await Assert.That((object?)urn).IsNull();
	}

	[Test]
	public async Task try_parse_returns_false_when_guid_format_is_invalid() {
		var success = SchemaUrn.TryParse($"urn:schemas-kurrent:{ValidNamespace}:{ValidMessageName}:not-a-guid", out var urn);

		await Assert.That(success).IsFalse();
		await Assert.That((object?)urn).IsNull();
	}

	[Test]
	public async Task to_string_returns_correct_urn_string() {
		var urn = SchemaUrn.Create(ValidNamespace, ValidMessageName, ValidGuid);

		var urnString = urn.ToString();

		await Assert.That(urnString).IsEqualTo($"urn:schemas-kurrent:{ValidNamespace}:{ValidMessageName}:{ValidGuid}");
	}

	[Test]
	public async Task implicit_conversion_to_string_returns_correct_urn_string() {
		var urn = SchemaUrn.Create(ValidNamespace, ValidMessageName, ValidGuid);

		string urnString = urn;

		await Assert.That(urnString).IsEqualTo($"urn:schemas-kurrent:{ValidNamespace}:{ValidMessageName}:{ValidGuid}");
	}

	[Test]
	public async Task equal_objects_should_be_equal_and_have_same_hash_code() {
		var urn1 = SchemaUrn.Create(ValidNamespace, ValidMessageName, ValidGuid);
		var urn2 = SchemaUrn.Create(ValidNamespace, ValidMessageName, ValidGuid);

		await Assert.That(urn1).IsEqualTo(urn2);
		await Assert.That(urn1.GetHashCode()).IsEqualTo(urn2.GetHashCode());
	}

	[Test]
	public async Task objects_with_different_namespaces_should_not_be_equal() {
		var urn1 = SchemaUrn.Create(ValidNamespace, ValidMessageName, ValidGuid);
		var urn2 = SchemaUrn.Create("different.namespace", ValidMessageName, ValidGuid);

		await Assert.That(urn1).IsNotEqualTo(urn2);
		await Assert.That(urn1.GetHashCode()).IsNotEqualTo(urn2.GetHashCode());
	}

	[Test]
	public async Task objects_with_different_message_names_should_not_be_equal() {
		var urn1 = SchemaUrn.Create(ValidNamespace, ValidMessageName, ValidGuid);
		var urn2 = SchemaUrn.Create(ValidNamespace, "different.message", ValidGuid);

		await Assert.That(urn1).IsNotEqualTo(urn2);
		await Assert.That(urn1.GetHashCode()).IsNotEqualTo(urn2.GetHashCode());
	}

	[Test]
	public async Task objects_with_different_guids_should_not_be_equal() {
		var urn1 = SchemaUrn.Create(ValidNamespace, ValidMessageName, ValidGuid);
		var urn2 = SchemaUrn.Create(ValidNamespace, ValidMessageName, Guid.NewGuid());

		await Assert.That(urn1).IsNotEqualTo(urn2);
		await Assert.That(urn1.GetHashCode()).IsNotEqualTo(urn2.GetHashCode());
	}

	[Test]
	public async Task object_with_guid_should_not_equal_object_without_guid() {
		var urn1 = SchemaUrn.Create(ValidNamespace, ValidMessageName, ValidGuid);
		var urn2 = SchemaUrn.Create(ValidNamespace, ValidMessageName);

		await Assert.That(urn1).IsNotEqualTo(urn2);
		await Assert.That(urn1.GetHashCode()).IsNotEqualTo(urn2.GetHashCode());
	}

	[Test]
	public async Task parses_real_world_urn_example_correctly() {
		var urnString = "urn:schemas-kurrent:bend-studio.days-gone:dlc.broken-roads";

		var success = SchemaUrn.TryParse(urnString, out var urn);

		await Assert.That(success).IsTrue();
		await Assert.That(urn).IsNotNull();
		await Assert.That(urn.Namespace).IsEqualTo("bend-studio.days-gone");
		await Assert.That(urn.MessageName).IsEqualTo("dlc.broken-roads");
		await Assert.That(urn.SchemaGuid).IsNull();
	}

	[Test]
	public async Task parses_real_world_urn_with_guid_example_correctly() {
		var guid      = Guid.NewGuid();
		var urnString = $"urn:schemas-kurrent:bend-studio.days-gone:dlc.broken-roads:{guid}";

		var success = SchemaUrn.TryParse(urnString, out var urn);

		await Assert.That(success).IsTrue();
		await Assert.That(urn).IsNotNull();
		await Assert.That(urn.Namespace).IsEqualTo("bend-studio.days-gone");
		await Assert.That(urn.MessageName).IsEqualTo("dlc.broken-roads");
		await Assert.That(urn.SchemaGuid).IsEqualTo(guid);
	}
}

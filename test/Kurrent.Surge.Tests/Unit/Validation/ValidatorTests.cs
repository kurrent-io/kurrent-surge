using Kurrent.Validation;

namespace Kurrent.Surge.Tests.Validation;

[Category("Validation")]
public class ValidatorTests {
    #region ->> Test Models <<-

    record Person(string Name, int Age, string? Email = null) {
        public Person() : this("", 0) { }
    }

    record Range(int Min, int Max) {
        public Range() : this(0, 0) { }
    }

    record Container(Person? Person, int[] Numbers) {
        public Container() : this(null, []) { }
    }

    record Endpoint(string Host, int Port) {
        public Endpoint() : this("", 0) { }
    }

    record ServerConfig(string Name, Endpoint[] Endpoints, ConnectionMode Mode = ConnectionMode.Direct) {
        public ServerConfig() : this("", []) { }
    }

    enum ConnectionMode { Direct, Discover, Invalid = 99 }

    record SelfValidatingPerson : SelfValidating<SelfValidatingPerson, SelfValidatingPerson.PersonValidator> {
        public string Name { get; init; } = "";
        public int Age { get; init; }

        public class PersonValidator : Validator<PersonValidator, SelfValidatingPerson> {
            public PersonValidator() {
                RuleFor(x => x.Name).NotEmpty();
                RuleFor(x => x.Age).GreaterThan(0);
            }
        }
    }

    #endregion

    #region ->> Test Validators <<-

    class PersonValidator : Validator<PersonValidator, Person> {
        public PersonValidator() {
            RuleFor(x => x.Name).NotEmpty().WithMessage("Name is required.");
            RuleFor(x => x.Age).GreaterThanOrEqualTo(0).WithMessage("Age cannot be negative.");
        }
    }

    class StrictPersonValidator : Validator<StrictPersonValidator, Person> {
        public StrictPersonValidator() {
            RuleFor(x => x.Name).NotEmpty();
            RuleFor(x => x.Name).Must(n => n.Length <= 50, "Name must be 50 chars or less.");
            RuleFor(x => x.Age).InclusiveBetween(1, 120);
            RuleFor(x => x.Email).Must(e => e is null || e.Contains('@'), "Invalid email.");
        }
    }

    class RangeValidator : Validator<RangeValidator, Range> {
        public RangeValidator() {
            RuleFor(x => x.Min).GreaterThanOrEqualTo(0);
            RuleFor(x => x.Max).GreaterThan(x => x.Min)
                .WithMessage("Max must be greater than Min.");
        }
    }

    class EndpointValidator : Validator<EndpointValidator, Endpoint> {
        public EndpointValidator() {
            RuleFor(x => x.Host).NotEmpty().WithMessage("Host is required.");
            RuleFor(x => x.Port).InclusiveBetween(1, 65535).WithMessage("Port must be 1-65535.");
        }
    }

    class ServerConfigValidator : Validator<ServerConfigValidator, ServerConfig> {
        public ServerConfigValidator() {
            RuleFor(x => x.Name).NotEmpty();
            RuleFor(x => x.Mode).IsInEnum();

            When(x => x.Mode == ConnectionMode.Direct, () => {
                RuleFor(x => x.Endpoints.Length).Equal(1)
                    .WithMessage("Direct mode requires exactly one endpoint.");
            });

            When(x => x.Mode == ConnectionMode.Discover, () => {
                RuleFor(x => x.Endpoints.Length).GreaterThan(0)
                    .WithMessage("Discover mode requires at least one endpoint.");
            });

            RuleForEach(x => x.Endpoints).ChildRules(ep => {
                ep.RuleFor(e => e.Host).NotEmpty().WithMessage("Endpoint host is required.");
                ep.RuleFor(e => e.Port).InclusiveBetween(1, 65535)
                    .WithMessage("Endpoint port must be 1-65535.");
            });
        }
    }

    class NestedValidator : Validator<NestedValidator, Container> {
        public NestedValidator() {
            RuleFor(x => x.Person!).SetValidator(PersonValidator.Instance);
        }
    }

    class SeverityValidator : Validator<SeverityValidator, Person> {
        public SeverityValidator() {
            RuleFor(x => x.Name).NotEmpty().WithSeverity(RuleSeverity.Error);
            RuleFor(x => x.Age).GreaterThan(0).WithSeverity(RuleSeverity.Warning);
        }
    }

    class CascadeValidator : Validator<CascadeValidator, Person> {
        public CascadeValidator() {
            // Chained rules on a single RuleFor — cascade-stop: second should not run if first fails
            RuleFor(x => x.Name)
                .NotEmpty().WithMessage("Name cannot be empty.")
                .Must(n => n.Length >= 2, "Name must be at least 2 characters.");
        }
    }

    class DynamicMessageValidator : Validator<DynamicMessageValidator, Person> {
        public DynamicMessageValidator() {
            RuleFor(x => x.Age).GreaterThan(0)
                .WithMessage(p => $"Person '{p.Name}' has invalid age.");
        }
    }

    class NestedWhenValidator : Validator<NestedWhenValidator, ServerConfig> {
        public NestedWhenValidator() {
            When(x => x.Mode == ConnectionMode.Discover, () => {
                When(x => x.Endpoints.Length > 1, () => {
                    RuleFor(x => x.Name).Must(n => n.StartsWith("cluster-"), "Cluster name must start with 'cluster-'.");
                });
            });
        }
    }

    #endregion

    #region ->> Property Name Extraction <<-

    [Test]
    public async ValueTask extracts_simple_property_name() {
        // Arrange & Act
        var name = PropertyNameExtractor.Extract("x => x.Name");

        // Assert
        await Assert.That(name).IsEqualTo("Name");
    }

    [Test]
    public async ValueTask extracts_nested_property_name() {
        // Arrange & Act
        var name = PropertyNameExtractor.Extract("x => x.Retry.MaxAttempts");

        // Assert
        await Assert.That(name).IsEqualTo("Retry.MaxAttempts");
    }

    [Test]
    public async ValueTask extracts_deep_property_path() {
        // Arrange & Act
        var name = PropertyNameExtractor.Extract("x => x.Auth.Certificate.Path");

        // Assert
        await Assert.That(name).IsEqualTo("Auth.Certificate.Path");
    }

    [Test]
    public async ValueTask extracts_expression_without_arrow() {
        // Arrange & Act
        var name = PropertyNameExtractor.Extract("x.Name");

        // Assert
        await Assert.That(name).IsEqualTo("Name");
    }

    [Test]
    public async ValueTask extracts_parameterless_expression() {
        // Arrange & Act
        var name = PropertyNameExtractor.Extract("Name");

        // Assert
        await Assert.That(name).IsEqualTo("Name");
    }

    #endregion

    #region ->> NotEmpty <<-

    [Test]
    public async ValueTask not_empty_fails_for_null_string() {
        // Arrange
        var validator = new PersonValidator();
        var person = new Person(null!, 25);

        // Act
        var result = validator.Validate(person);

        // Assert
        await Assert.That(result.IsInvalid).IsTrue();
        await Assert.That(result.Problems).HasSingleItem();
        await Assert.That(result.Problems[0].FieldName).IsEqualTo("Name");
    }

    [Test]
    public async ValueTask not_empty_fails_for_empty_string() {
        // Arrange
        var validator = new PersonValidator();
        var person = new Person("", 25);

        // Act
        var result = validator.Validate(person);

        // Assert
        await Assert.That(result.IsInvalid).IsTrue();
    }

    [Test]
    public async ValueTask not_empty_fails_for_whitespace_string() {
        // Arrange
        var validator = new PersonValidator();
        var person = new Person("   ", 25);

        // Act
        var result = validator.Validate(person);

        // Assert
        await Assert.That(result.IsInvalid).IsTrue();
    }

    [Test]
    public async ValueTask not_empty_passes_for_valid_string() {
        // Arrange
        var validator = new PersonValidator();
        var person = new Person("Alice", 25);

        // Act
        var result = validator.Validate(person);

        // Assert
        await Assert.That(result.IsValid).IsTrue();
    }

    #endregion

    #region ->> InclusiveBetween <<-

    [Test]
    public async ValueTask inclusive_between_passes_at_min() {
        // Arrange
        var validator = new StrictPersonValidator();
        var person = new Person("Alice", 1);

        // Act
        var result = validator.Validate(person);

        // Assert
        await Assert.That(result.IsValid).IsTrue();
    }

    [Test]
    public async ValueTask inclusive_between_passes_at_max() {
        // Arrange
        var validator = new StrictPersonValidator();
        var person = new Person("Alice", 120);

        // Act
        var result = validator.Validate(person);

        // Assert
        await Assert.That(result.IsValid).IsTrue();
    }

    [Test]
    public async ValueTask inclusive_between_fails_below_min() {
        // Arrange
        var validator = new StrictPersonValidator();
        var person = new Person("Alice", 0);

        // Act
        var result = validator.Validate(person);

        // Assert
        await Assert.That(result.IsInvalid).IsTrue();
        await Assert.That(result.Problems[0].FieldName).IsEqualTo("Age");
    }

    [Test]
    public async ValueTask inclusive_between_fails_above_max() {
        // Arrange
        var validator = new StrictPersonValidator();
        var person = new Person("Alice", 121);

        // Act
        var result = validator.Validate(person);

        // Assert
        await Assert.That(result.IsInvalid).IsTrue();
    }

    #endregion

    #region ->> Comparison Operators <<-

    [Test]
    public async ValueTask greater_than_passes_when_above() {
        // Arrange
        var validator = new PersonValidator();
        var person = new Person("Alice", 1);

        // Act
        var result = validator.Validate(person);

        // Assert
        await Assert.That(result.IsValid).IsTrue();
    }

    [Test]
    public async ValueTask greater_than_or_equal_fails_when_negative() {
        // Arrange
        var validator = new PersonValidator();
        var person = new Person("Alice", -1);

        // Act
        var result = validator.Validate(person);

        // Assert
        await Assert.That(result.IsInvalid).IsTrue();
        await Assert.That(result.Problems[0].FieldName).IsEqualTo("Age");
    }

    #endregion

    #region ->> Cross-Field Comparison <<-

    [Test]
    public async ValueTask cross_field_greater_than_passes_when_valid() {
        // Arrange
        var validator = new RangeValidator();
        var range = new Range(5, 10);

        // Act
        var result = validator.Validate(range);

        // Assert
        await Assert.That(result.IsValid).IsTrue();
    }

    [Test]
    public async ValueTask cross_field_greater_than_fails_when_equal() {
        // Arrange
        var validator = new RangeValidator();
        var range = new Range(5, 5);

        // Act
        var result = validator.Validate(range);

        // Assert
        await Assert.That(result.IsInvalid).IsTrue();
        await Assert.That(result.Problems[0].FieldName).IsEqualTo("Max");
        await Assert.That(result.Problems[0].Message).IsEqualTo("Max must be greater than Min.");
    }

    [Test]
    public async ValueTask cross_field_greater_than_fails_when_less() {
        // Arrange
        var validator = new RangeValidator();
        var range = new Range(10, 5);

        // Act
        var result = validator.Validate(range);

        // Assert
        await Assert.That(result.IsInvalid).IsTrue();
    }

    #endregion

    #region ->> Equal <<-

    [Test]
    public async ValueTask equal_passes_when_matching() {
        // Arrange
        var validator = new ServerConfigValidator();
        var config = new ServerConfig("srv", [new Endpoint("localhost", 2113)], ConnectionMode.Direct);

        // Act
        var result = validator.Validate(config);

        // Assert
        await Assert.That(result.IsValid).IsTrue();
    }

    [Test]
    public async ValueTask equal_fails_when_not_matching() {
        // Arrange
        var validator = new ServerConfigValidator();
        var config = new ServerConfig("srv", [new("a", 1), new("b", 2)], ConnectionMode.Direct);

        // Act
        var result = validator.Validate(config);

        // Assert
        await Assert.That(result.IsInvalid).IsTrue();
        await Assert.That(result.Problems[0].Message).IsEqualTo("Direct mode requires exactly one endpoint.");
    }

    #endregion

    #region ->> Must <<-

    [Test]
    public async ValueTask must_with_property_predicate_passes() {
        // Arrange
        var validator = new StrictPersonValidator();
        var person = new Person("Alice", 25, "alice@example.com");

        // Act
        var result = validator.Validate(person);

        // Assert
        await Assert.That(result.IsValid).IsTrue();
    }

    [Test]
    public async ValueTask must_with_property_predicate_fails() {
        // Arrange
        var validator = new StrictPersonValidator();
        var person = new Person("Alice", 25, "not-an-email");

        // Act
        var result = validator.Validate(person);

        // Assert
        await Assert.That(result.IsInvalid).IsTrue();
        await Assert.That(result.Problems[0].Message).IsEqualTo("Invalid email.");
    }

    #endregion

    #region ->> IsInEnum <<-

    [Test]
    public async ValueTask is_in_enum_passes_for_valid_value() {
        // Arrange
        var validator = new ServerConfigValidator();
        var config = new ServerConfig("srv", [new("localhost", 2113)], ConnectionMode.Direct);

        // Act
        var result = validator.Validate(config);

        // Assert
        await Assert.That(result.IsValid).IsTrue();
    }

    [Test]
    public async ValueTask is_in_enum_fails_for_invalid_value() {
        // Arrange
        var validator = new ServerConfigValidator();
        var config = new ServerConfig("srv", [new("localhost", 2113)], (ConnectionMode)42);

        // Act
        var result = validator.Validate(config);

        // Assert
        await Assert.That(result.IsInvalid).IsTrue();
        await Assert.That(result.Problems[0].FieldName).IsEqualTo("Mode");
    }

    #endregion

    #region ->> WithMessage <<-

    [Test]
    public async ValueTask with_message_overrides_default() {
        // Arrange
        var validator = new PersonValidator();
        var person = new Person("", 25);

        // Act
        var result = validator.Validate(person);

        // Assert
        await Assert.That(result.Problems[0].Message).IsEqualTo("Name is required.");
    }

    [Test]
    public async ValueTask with_message_lambda_includes_instance_data() {
        // Arrange
        var validator = new DynamicMessageValidator();
        var person = new Person("Alice", 0);

        // Act
        var result = validator.Validate(person);

        // Assert
        await Assert.That(result.Problems[0].Message).IsEqualTo("Person 'Alice' has invalid age.");
    }

    #endregion

    #region ->> WithSeverity <<-

    [Test]
    public async ValueTask warning_severity_does_not_cause_failure() {
        // Arrange
        var validator = new SeverityValidator();
        var person = new Person("Alice", 0); // Age=0 fails but is Warning

        // Act
        var result = validator.Validate(person);

        // Assert
        await Assert.That(result.IsValid).IsTrue();
    }

    [Test]
    public async ValueTask error_severity_causes_failure() {
        // Arrange
        var validator = new SeverityValidator();
        var person = new Person("", 0); // Name empty is Error

        // Act
        var result = validator.Validate(person);

        // Assert
        await Assert.That(result.IsInvalid).IsTrue();
        await Assert.That(result.Problems).HasSingleItem();
        await Assert.That(result.Problems[0].FieldName).IsEqualTo("Name");
    }

    #endregion

    #region ->> When (Conditional) <<-

    [Test]
    public async ValueTask when_condition_true_rules_run() {
        // Arrange
        var validator = new ServerConfigValidator();
        var config = new ServerConfig("srv", [], ConnectionMode.Discover);

        // Act
        var result = validator.Validate(config);

        // Assert
        await Assert.That(result.IsInvalid).IsTrue();
        await Assert.That(result.Problems.Any(p => p.Message == "Discover mode requires at least one endpoint.")).IsTrue();
    }

    [Test]
    public async ValueTask when_condition_false_rules_skipped() {
        // Arrange
        var validator = new ServerConfigValidator();
        // Direct mode with one endpoint — the Discover rules should not fire
        var config = new ServerConfig("srv", [new("localhost", 2113)], ConnectionMode.Direct);

        // Act
        var result = validator.Validate(config);

        // Assert
        await Assert.That(result.IsValid).IsTrue();
    }

    #endregion

    #region ->> Nested When <<-

    [Test]
    public async ValueTask nested_when_both_true_rules_run() {
        // Arrange
        var validator = new NestedWhenValidator();
        var config = new ServerConfig("bad-name", [new("a", 1), new("b", 2)], ConnectionMode.Discover);

        // Act
        var result = validator.Validate(config);

        // Assert
        await Assert.That(result.IsInvalid).IsTrue();
        await Assert.That(result.Problems[0].Message).IsEqualTo("Cluster name must start with 'cluster-'.");
    }

    [Test]
    public async ValueTask nested_when_outer_false_inner_skipped() {
        // Arrange
        var validator = new NestedWhenValidator();
        var config = new ServerConfig("anything", [new("a", 1), new("b", 2)], ConnectionMode.Direct);

        // Act
        var result = validator.Validate(config);

        // Assert
        await Assert.That(result.IsValid).IsTrue();
    }

    [Test]
    public async ValueTask nested_when_outer_true_inner_false_skipped() {
        // Arrange
        var validator = new NestedWhenValidator();
        // Discover mode but only 1 endpoint — inner When is false
        var config = new ServerConfig("anything", [new("a", 1)], ConnectionMode.Discover);

        // Act
        var result = validator.Validate(config);

        // Assert
        await Assert.That(result.IsValid).IsTrue();
    }

    #endregion

    #region ->> SetValidator (Nested) <<-

    [Test]
    public async ValueTask nested_validator_passes_when_valid() {
        // Arrange
        var validator = new NestedValidator();
        var container = new Container(new Person("Alice", 25), [1, 2, 3]);

        // Act
        var result = validator.Validate(container);

        // Assert
        await Assert.That(result.IsValid).IsTrue();
    }

    [Test]
    public async ValueTask nested_validator_fails_with_prefixed_field() {
        // Arrange
        var validator = new NestedValidator();
        var container = new Container(new Person("", 25), []);

        // Act
        var result = validator.Validate(container);

        // Assert
        await Assert.That(result.IsInvalid).IsTrue();
        await Assert.That(result.Problems[0].FieldName).IsEqualTo("Person.Name");
    }

    [Test]
    public async ValueTask nested_validator_skips_null_value() {
        // Arrange
        var validator = new NestedValidator();
        var container = new Container(null, []);

        // Act
        var result = validator.Validate(container);

        // Assert
        await Assert.That(result.IsValid).IsTrue();
    }

    #endregion

    #region ->> RuleForEach + ChildRules <<-

    [Test]
    public async ValueTask rule_for_each_passes_when_all_elements_valid() {
        // Arrange
        var validator = new ServerConfigValidator();
        var config = new ServerConfig("srv", [new("host1", 80), new("host2", 443)], ConnectionMode.Discover);

        // Act
        var result = validator.Validate(config);

        // Assert
        await Assert.That(result.IsValid).IsTrue();
    }

    [Test]
    public async ValueTask rule_for_each_fails_with_indexed_field_name() {
        // Arrange
        var validator = new ServerConfigValidator();
        var config = new ServerConfig("srv", [new("host1", 80), new("", 443)], ConnectionMode.Discover);

        // Act
        var result = validator.Validate(config);

        // Assert
        await Assert.That(result.IsInvalid).IsTrue();
        await Assert.That(result.Problems.Any(p => p.FieldName == "Endpoints[1].Host")).IsTrue();
    }

    [Test]
    public async ValueTask rule_for_each_reports_port_out_of_range() {
        // Arrange
        var validator = new ServerConfigValidator();
        var config = new ServerConfig("srv", [new("host1", 0)], ConnectionMode.Direct);

        // Act
        var result = validator.Validate(config);

        // Assert
        await Assert.That(result.IsInvalid).IsTrue();
        await Assert.That(result.Problems.Any(p => p.FieldName == "Endpoints[0].Port")).IsTrue();
    }

    #endregion

    #region ->> Cascade Stop <<-

    [Test]
    public async ValueTask cascade_stops_after_first_failure_on_same_property() {
        // Arrange
        var validator = new CascadeValidator();
        // Empty name will fail the first RuleFor(Name).NotEmpty()
        // The second RuleFor(Name).Must(length >= 2) should NOT run
        var person = new Person("", 25);

        // Act
        var result = validator.Validate(person);

        // Assert
        await Assert.That(result.IsInvalid).IsTrue();
        // Should only have 1 problem for Name, not 2
        var nameProblems = result.Problems.Where(p => p.FieldName == "Name").ToArray();
        await Assert.That(nameProblems).HasSingleItem();
        await Assert.That(nameProblems[0].Message).IsEqualTo("Name cannot be empty.");
    }

    [Test]
    public async ValueTask cascade_allows_second_rule_when_first_passes() {
        // Arrange
        var validator = new CascadeValidator();
        // "A" passes NotEmpty but fails length >= 2
        var person = new Person("A", 25);

        // Act
        var result = validator.Validate(person);

        // Assert
        await Assert.That(result.IsInvalid).IsTrue();
        await Assert.That(result.Problems[0].Message).IsEqualTo("Name must be at least 2 characters.");
    }

    #endregion

    #region ->> Multiple Properties <<-

    [Test]
    public async ValueTask collects_failures_across_multiple_properties() {
        // Arrange
        var validator = new StrictPersonValidator();
        var person = new Person("", 0); // Both Name and Age fail

        // Act
        var result = validator.Validate(person);

        // Assert
        await Assert.That(result.IsInvalid).IsTrue();
        await Assert.That(result.Problems.Length).IsGreaterThanOrEqualTo(2);

        var fields = result.Problems.Select(p => p.FieldName).Distinct().ToArray();
        await Assert.That(fields).Contains("Name");
        await Assert.That(fields).Contains("Age");
    }

    #endregion

    #region ->> Integration with SelfValidating <<-

    [Test]
    public async ValueTask self_validating_ensure_valid_throws_on_invalid() {
        // Arrange
        var record = new SelfValidatingPerson { Name = "", Age = 0 };

        // Act & Assert
        await Assert.That(record.EnsureValid).ThrowsExactly<ValidationException>();
    }

    [Test]
    public async ValueTask self_validating_ensure_valid_does_not_throw_on_valid() {
        // Arrange
        var record = new SelfValidatingPerson { Name = "Alice", Age = 25 };

        // Act & Assert
        await Assert.That(record.EnsureValid).ThrowsNothing();
    }

    [Test]
    public async ValueTask self_validating_validate_returns_valid() {
        // Arrange
        var record = new SelfValidatingPerson { Name = "Alice", Age = 25 };

        // Act
        var result = record.Validate();

        // Assert
        await Assert.That(result.IsValid).IsTrue();
    }

    [Test]
    public async ValueTask self_validating_validate_returns_invalid() {
        // Arrange
        var record = new SelfValidatingPerson { Name = "", Age = -1 };

        // Act
        var result = record.Validate();

        // Assert
        await Assert.That(result.IsInvalid).IsTrue();
    }

    #endregion

    #region ->> ValidationResult <<-

    [Test]
    public async ValueTask validation_result_valid_singleton_is_valid() {
        // Act
        var result = ValidationResult.Valid;

        // Assert
        await Assert.That(result.IsValid).IsTrue();
        await Assert.That(result.IsInvalid).IsFalse();
        await Assert.That(result.Problems).IsEmpty();
    }

    [Test]
    public async ValueTask validation_result_failure_contains_problems() {
        // Act
        var result = ValidationResult.Failure(
            ErrorCode.From("TEST_VALIDATION"),
            ValidationProblem.CreateForField("Field1", "Bad value"));

        // Assert
        await Assert.That(result.IsInvalid).IsTrue();
        await Assert.That(result.Problems).HasSingleItem();
        await Assert.That(result.ErrorCode.Value).IsEqualTo("TEST_VALIDATION");
    }

    [Test]
    public async ValueTask validation_result_ensure_valid_throws_on_failure() {
        // Arrange
        var result = ValidationResult.Failure(
            ErrorCode.From("TEST_VALIDATION"),
            ValidationProblem.CreateForField("Field1", "Bad value"));

        // Act & Assert
        await Assert.That(() => result.EnsureValid()).ThrowsExactly<ValidationException>();
    }

    [Test]
    public async ValueTask validation_result_implicit_from_success() {
        // Act
        ValidationResult result = Results.Success;

        // Assert
        await Assert.That(result.IsValid).IsTrue();
        await Assert.That(result).IsSameReferenceAs(ValidationResult.Valid);
    }

    [Test]
    public async ValueTask validation_result_from_problem_creates_failure() {
        // Act
        var result = ValidationResult.FromProblem(
            ErrorCode.From("TEST"),
            "bad value");

        // Assert
        await Assert.That(result.IsInvalid).IsTrue();
        await Assert.That(result.Problems).HasSingleItem();
        await Assert.That(result.ErrorCode.Value).IsEqualTo("TEST");
    }

    [Test]
    public async ValueTask validation_result_failure_without_error_code_uses_validation_failure() {
        // Act
        var result = ValidationResult.Failure(
            ValidationProblem.CreateForField("Field1", "Bad value"));

        // Assert
        await Assert.That(result.IsInvalid).IsTrue();
        await Assert.That(result.ErrorCode.Value).IsEqualTo("VALIDATION_FAILURE");
    }

    [Test]
    public async ValueTask validation_result_to_string_returns_valid_when_valid() {
        // Act
        var text = ValidationResult.Valid.ToString();

        // Assert
        await Assert.That(text).IsEqualTo("Valid");
    }

    [Test]
    public async ValueTask validation_result_to_string_returns_error_message_when_invalid() {
        // Arrange
        var result = ValidationResult.Failure(
            ErrorCode.From("TEST_VALIDATION"),
            ValidationProblem.CreateForField("Name", "is required"));

        // Act
        var text = result.ToString();

        // Assert
        await Assert.That(text).Contains("Name");
        await Assert.That(text).Contains("is required");
    }

    #endregion

    #region ->> Error Code Generation <<-

    [Test]
    public async ValueTask error_code_is_generated_from_type_name() {
        // Act
        var errorCode = PersonValidator.Instance.ErrorCode;

        // Assert
        await Assert.That(errorCode).IsEqualTo("INVALID_PERSON");
    }

    #endregion
}

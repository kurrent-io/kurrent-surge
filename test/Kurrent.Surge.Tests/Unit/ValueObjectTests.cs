// using Kurrent.Surge.Validation;
//
// namespace Kurrent.Surge.Tests;
//
// public class ValueObjectTests {
//     [Test]
//     public void from_creates_instance_when_value_is_valid() {
//         const string testValue = "test";
//
//         var result = TestStringValue.From(testValue);
//
//         result.ShouldNotBeNull();
//         result.Value.ShouldBe(testValue);
//     }
//
//     [Test]
//     public void from_creates_instance_with_integer_value() {
//         const int testValue = 42;
//
//         var result = TestIntValue.From(testValue);
//
//         result.ShouldNotBeNull();
//         result.Value.ShouldBe(testValue);
//     }
//
//     [Test]
//     public void from_throws_argument_null_exception_when_value_is_null() {
//         Should.Throw<ArgumentNullException>(() => TestStringValue.From(null!));
//     }
//
//     [Test]
//     public void from_calls_validation_and_throws_when_validation_fails() {
//         const string invalidValue = "";
//
//         Should.Throw<ValidationException>(() => ValidatedStringValue.From(invalidValue))
//             .Message.ShouldContain("Validation failed");
//     }
//
//     [Test]
//     public void from_calls_validation_and_succeeds_when_validation_passes() {
//         const string validValue = "valid@email.com";
//
//         var result = EmailValue.From(validValue);
//
//         result.ShouldNotBeNull();
//         result.Value.ShouldBe(validValue);
//     }
//
//     [Test]
//     public void from_throws_custom_validation_exception_with_specific_message() {
//         const string shortValue = "ab";
//
//         var exception = Should.Throw<ValidationException>(() => ValidatedStringValue.From(shortValue));
//         exception.Message.ShouldContain("Validation failed");
//     }
//
//     [Test]
//     public void equality_returns_true_when_values_are_equal() {
//         var value1 = TestStringValue.From("test");
//         var value2 = TestStringValue.From("test");
//
//         value1.ShouldBe(value2);
//         (value1 == value2).ShouldBeTrue();
//         (value1 != value2).ShouldBeFalse();
//     }
//
//     [Test]
//     public void equality_returns_false_when_values_are_different() {
//         var value1 = TestStringValue.From("test1");
//         var value2 = TestStringValue.From("test2");
//
//         value1.ShouldNotBe(value2);
//         (value1 == value2).ShouldBeFalse();
//         (value1 != value2).ShouldBeTrue();
//     }
//
//     [Test]
//     public void equality_handles_null_values_correctly() {
//         var              value     = TestStringValue.From("test");
//         TestStringValue? nullValue = null;
//
//         (value == nullValue).ShouldBeFalse();
//         (nullValue == value).ShouldBeFalse();
//         (nullValue == nullValue).ShouldBeTrue();
//     }
//
//     [Test]
//     public void equals_returns_false_when_compared_to_different_type() {
//         var stringValue = TestStringValue.From("42");
//         var intValue    = TestIntValue.From(42);
//
//         // ReSharper disable once SuspiciousTypeConversion.Global
//         stringValue.Equals(intValue).ShouldBeFalse();
//     }
//
//     [Test]
//     public void equals_returns_false_when_compared_to_null() {
//         var value = TestStringValue.From("test");
//
//         value.Equals(null).ShouldBeFalse();
//     }
//
//     [Test]
//     public void get_hash_code_returns_same_value_for_equal_instances() {
//         var value1 = TestStringValue.From("test");
//         var value2 = TestStringValue.From("test");
//
//         value1.GetHashCode().ShouldBe(value2.GetHashCode());
//     }
//
//     [Test]
//     public void get_hash_code_returns_different_values_for_different_instances() {
//         var value1 = TestStringValue.From("test1");
//         var value2 = TestStringValue.From("test2");
//
//         value1.GetHashCode().ShouldNotBe(value2.GetHashCode());
//     }
//
//     [Test]
//     public void get_hash_code_is_consistent_for_same_wrapped_value() {
//         var value1 = TestStringValue.From("test");
//         var value2 = TestStringValue.From("test");
//
//         value1.GetHashCode().ShouldBe(value2.GetHashCode());
//     }
//
//     // String representation tests
//     [Test]
//     public void to_string_returns_wrapped_value_string_representation() {
//         const string testValue = "test";
//
//         var value = TestStringValue.From(testValue);
//
//         value.ToString().ShouldBe(testValue);
//     }
//
//     [Test]
//     public void to_string_returns_integer_value_as_string() {
//         const int testValue = 42;
//
//         var value = TestIntValue.From(testValue);
//
//         value.ToString().ShouldBe("42");
//     }
//
//     [Test]
//     public void to_string_handles_empty_string_correctly() {
//         const string testValue = "";
//
//         var value = TestStringValue.From(testValue);
//
//         value.ToString().ShouldBe(testValue);
//     }
//
//     [Test]
//     public void ensure_valid_method_is_called_during_from_creation() {
//         const string invalidValue = "no-at-sign";
//
//         Should.Throw<ValidationException>(() => EmailValue.From(invalidValue))
//             .Message.ShouldContain("Validation failed");
//     }
//
//     [Test]
//     public void ensure_valid_method_does_nothing_by_default() {
//         const string testValue = "any-value";
//
//         Should.NotThrow(() => TestStringValue.From(testValue));
//     }
//
//     [Test]
//     public void with_expression_creates_new_instance_with_different_value() {
//         var original = TestStringValue.From("original");
//         var modified = original with { Value = "modified" };
//
//         original.Value.ShouldBe("original");
//         modified.Value.ShouldBe("modified");
//         original.ShouldNotBe(modified);
//     }
//
//     [Test]
//     public void handles_special_characters_in_string_values() {
//         const string specialValue = "test@#$%^&*()_+{}|:<>?[];',./";
//
//         var value = TestStringValue.From(specialValue);
//
//         value.Value.ShouldBe(specialValue);
//         value.ToString().ShouldBe(specialValue);
//     }
//
//     [Test]
//     public void handles_unicode_characters_in_string_values() {
//         const string unicodeValue = "测试值🎯";
//
//         var value = TestStringValue.From(unicodeValue);
//
//         value.Value.ShouldBe(unicodeValue);
//         value.ToString().ShouldBe(unicodeValue);
//     }
//
//     [Test]
//     public void handles_maximum_integer_values() {
//         const int maxValue = int.MaxValue;
//
//         var value = TestIntValue.From(maxValue);
//
//         value.Value.ShouldBe(maxValue);
//         value.ToString().ShouldBe(maxValue.ToString());
//     }
//
//     [Test]
//     public void handles_minimum_integer_values() {
//         const int minValue = int.MinValue;
//
//         var value = TestIntValue.From(minValue);
//
//         value.Value.ShouldBe(minValue);
//         value.ToString().ShouldBe(minValue.ToString());
//     }
//
//     [Test]
//     public void multiple_validation_rules_work_together() {
//         // Valid case
//         var validValue = "test@email.com";
//         var result     = ComplexValidatedValue.From(validValue);
//         result.Value.ShouldBe(validValue);
//
//         // Invalid cases
//         Should.Throw<ValidationException>(() => ComplexValidatedValue.From(""));
//         Should.Throw<ValidationException>(() => ComplexValidatedValue.From("ab"));
//         Should.Throw<ValidationException>(() => ComplexValidatedValue.From("short"));
//     }
//
//     [Test]
//     public void from_creates_instance_with_tuple_value() {
//         var testTuple = ("John", 25);
//
//         var result = TestTupleValue.From(testTuple);
//
//         result.ShouldNotBeNull();
//         result.Value.ShouldBe(testTuple);
//         result.Value.Name.ShouldBe("John");
//         result.Value.Age.ShouldBe(25);
//     }
//
//     [Test]
//     public void from_handles_tuple_with_null_string_element() {
//         var testTuple = (null as string, 25);
//
//         var result = TestTupleValue.From(testTuple);
//
//         result.ShouldNotBeNull();
//         result.Value.ShouldBe(testTuple);
//         result.Value.Name.ShouldBeNull();
//         result.Value.Age.ShouldBe(25);
//     }
//
//     [Test]
//     public void from_handles_tuple_with_empty_string_element() {
//         var testTuple = ("", 25);
//
//         var result = TestTupleValue.From(testTuple);
//
//         result.ShouldNotBeNull();
//         result.Value.ShouldBe(testTuple);
//         result.Value.Name.ShouldBe("");
//         result.Value.Age.ShouldBe(25);
//     }
//
//     [Test]
//     public void tuple_equality_returns_true_when_values_are_equal() {
//         var tuple1 = TestTupleValue.From(("John", 25));
//         var tuple2 = TestTupleValue.From(("John", 25));
//
//         tuple1.ShouldBe(tuple2);
//         (tuple1 == tuple2).ShouldBeTrue();
//         (tuple1 != tuple2).ShouldBeFalse();
//     }
//
//     [Test]
//     public void tuple_equality_returns_false_when_names_differ() {
//         var tuple1 = TestTupleValue.From(("John", 25));
//         var tuple2 = TestTupleValue.From(("Jane", 25));
//
//         tuple1.ShouldNotBe(tuple2);
//         (tuple1 == tuple2).ShouldBeFalse();
//         (tuple1 != tuple2).ShouldBeTrue();
//     }
//
//     [Test]
//     public void tuple_equality_returns_false_when_ages_differ() {
//         var tuple1 = TestTupleValue.From(("John", 25));
//         var tuple2 = TestTupleValue.From(("John", 30));
//
//         tuple1.ShouldNotBe(tuple2);
//         (tuple1 == tuple2).ShouldBeFalse();
//         (tuple1 != tuple2).ShouldBeTrue();
//     }
//
//     [Test]
//     public void tuple_equality_handles_null_values_correctly() {
//         var tuple1 = TestTupleValue.From((null, 25));
//         var tuple2 = TestTupleValue.From((null, 25));
//         var tuple3 = TestTupleValue.From(("John", 25));
//
//         tuple1.ShouldBe(tuple2);
//         tuple1.ShouldNotBe(tuple3);
//         (tuple1 == tuple2).ShouldBeTrue();
//         (tuple1 == tuple3).ShouldBeFalse();
//     }
//
//     [Test]
//     public void tuple_get_hash_code_returns_same_value_for_equal_instances() {
//         var tuple1 = TestTupleValue.From(("John", 25));
//         var tuple2 = TestTupleValue.From(("John", 25));
//
//         tuple1.GetHashCode().ShouldBe(tuple2.GetHashCode());
//     }
//
//     [Test]
//     public void tuple_get_hash_code_returns_different_values_for_different_instances() {
//         var tuple1 = TestTupleValue.From(("John", 25));
//         var tuple2 = TestTupleValue.From(("Jane", 30));
//
//         tuple1.GetHashCode().ShouldNotBe(tuple2.GetHashCode());
//     }
//
//     [Test]
//     public void tuple_to_string_returns_tuple_string_representation() {
//         var tupleValue = TestTupleValue.From(("John", 25));
//
//         var result = tupleValue.ToString();
//
//         result.ShouldBe("(John, 25)");
//     }
//
//     [Test]
//     public void tuple_to_string_handles_null_string_element() {
//         var tupleValue = TestTupleValue.From((null, 25));
//
//         var result = tupleValue.ToString();
//
//         result.ShouldBe("(, 25)");
//     }
//
//     [Test]
//     public void tuple_to_string_handles_empty_string_element() {
//         var tupleValue = TestTupleValue.From(("", 25));
//
//         var result = tupleValue.ToString();
//
//         result.ShouldBe("(, 25)");
//     }
//
//     [Test]
//     public void tuple_to_string_handles_special_characters() {
//         var tupleValue = TestTupleValue.From(("John@#$%", 25));
//
//         var result = tupleValue.ToString();
//
//         result.ShouldBe("(John@#$%, 25)");
//     }
//
//     [Test]
//     public void tuple_with_expression_creates_new_instance_with_different_value() {
//         var original = TestTupleValue.From(("John", 25));
//         var modified = original with { Value = ("Jane", 30) };
//
//         original.Value.Name.ShouldBe("John");
//         original.Value.Age.ShouldBe(25);
//         modified.Value.Name.ShouldBe("Jane");
//         modified.Value.Age.ShouldBe(30);
//         original.ShouldNotBe(modified);
//     }
//
//     [Test]
//     public void tuple_with_expression_preserves_original_instance() {
//         var original      = TestTupleValue.From(("John", 25));
//         var originalTuple = original.Value;
//         var modified      = original with { Value = ("Jane", 30) };
//
//         // Original should remain unchanged
//         original.Value.ShouldBe(originalTuple);
//         original.Value.Name.ShouldBe("John");
//         original.Value.Age.ShouldBe(25);
//
//         // Modified should have new values
//         modified.Value.Name.ShouldBe("Jane");
//         modified.Value.Age.ShouldBe(30);
//     }
//
//     [Test]
//     public void tuple_supports_complex_tuple_scenarios() {
//         var complexTuple = (Name: "John Doe", Age: 25, IsActive: true, LastLogin: DateTime.Parse("2024-01-01"));
//         var tupleValue   = TestComplexTupleValue.From(complexTuple);
//
//         tupleValue.Value.Name.ShouldBe("John Doe");
//         tupleValue.Value.Age.ShouldBe(25);
//         tupleValue.Value.IsActive.ShouldBeTrue();
//         tupleValue.Value.LastLogin.ShouldBe(DateTime.Parse("2024-01-01"));
//
//         var modified = tupleValue with {
//             Value = complexTuple with { IsActive = false }
//         };
//
//         modified.Value.IsActive.ShouldBeFalse();
//         modified.Value.Name.ShouldBe("John Doe"); // Other values preserved
//     }
//
//     [Test]
//     public void validated_tuple_from_succeeds_with_valid_person_data() {
//         var validPerson = ("John Doe", 25);
//
//         var result = PersonValue.From(validPerson);
//
//         result.ShouldNotBeNull();
//         result.Value.Name.ShouldBe("John Doe");
//         result.Value.Age.ShouldBe(25);
//     }
//
//     [Test]
//     public void validated_tuple_from_throws_when_name_is_empty() {
//         var invalidPerson = ("", 25);
//
//         Should.Throw<ValidationException>(() => PersonValue.From(invalidPerson))
//             .Message.ShouldContain("Validation failed");
//     }
//
//     [Test]
//     public void validated_tuple_from_throws_when_name_is_null() {
//         var invalidPerson = (null as string, 25);
//
//         Should.Throw<ValidationException>(() => PersonValue.From(invalidPerson))
//             .Message.ShouldContain("Validation failed");
//     }
//
//     [Test]
//     public void validated_tuple_from_throws_when_age_is_negative() {
//         var invalidPerson = ("John Doe", -5);
//
//         Should.Throw<ValidationException>(() => PersonValue.From(invalidPerson))
//             .Message.ShouldContain("Validation failed");
//     }
//
//     [Test]
//     public void validated_tuple_from_throws_when_age_is_too_high() {
//         var invalidPerson = ("John Doe", 200);
//
//         Should.Throw<ValidationException>(() => PersonValue.From(invalidPerson))
//             .Message.ShouldContain("Validation failed");
//     }
//
//     [Test]
//     public void validated_tuple_handles_edge_case_ages() {
//         var newborn     = PersonValue.From(("Baby Doe", 0));
//         var centenarian = PersonValue.From(("Elder Doe", 150));
//
//         newborn.Value.Age.ShouldBe(0);
//         centenarian.Value.Age.ShouldBe(150);
//     }
//
//     [Test]
//     public void ensure_valid_returns_self_for_fluent_usage() {
//         var person = PersonValue.From(("John", 25));
//
//         var result = person.EnsureValid();
//
//         result.ShouldBe(person);
//         result.Value.Name.ShouldBe("John");
//         result.Value.Age.ShouldBe(25);
//     }
//
//     [Test]
//     public void ensure_valid_can_be_chained_fluently() {
//         var coordinate = CoordinateValue.From((45.5, -122.6));
//
//         // This demonstrates fluent validation - the method returns itself
//         var result = coordinate
//             .EnsureValid() // Validates the coordinate
//             .ToString();   // Can chain other methods
//
//         result.ShouldBe("45.5°N, 122.6°W");
//     }
//
//     [Test]
//     public void ensure_valid_throws_on_invalid_data_during_fluent_chain() {
//         var invalidCoordinate = CoordinateValue.From((0, 0));
//
//         // Manually set invalid data for demonstration
//         var modifiedCoordinate = invalidCoordinate with { Value = (200, 0) };
//
//         Should.Throw<ValidationException>(() => modifiedCoordinate.EnsureValid())
//             .Message.ShouldContain("Validation failed");
//     }
//
//     [Test]
//     public void demonstrate_fluent_validation_patterns() {
//         // Example 1: Simple fluent validation with method chaining
//         var validCoordinate = CoordinateValue
//             .From((37.7749, -122.4194)) // San Francisco
//             .EnsureValid()              // Validate the coordinate
//             .ToString();                // Chain additional methods
//
//         validCoordinate.ShouldBe("37.7749°N, 122.4194°W");
//
//         // Example 2: Validation as part of complex expressions
//         var people = new[] { ("Alice", 25), ("Bob", 30), ("Charlie", 35) };
//
//         var validatedPeople = people
//             .Select(data => PersonValue.From(data).EnsureValid()) // Each person is validated
//             .Select(person => person.ToString())                  // Convert to string representation
//             .ToList();
//
//         validatedPeople.ShouldContain("Alice (25 years old)");
//         validatedPeople.ShouldContain("Bob (30 years old)");
//         validatedPeople.ShouldContain("Charlie (35 years old)");
//
//         // Example 3: Re-validation after modifications
//         var person         = PersonValue.From(("John", 25));
//         var modifiedPerson = person with { Value = ("John", 30) };
//
//         // Can re-validate the modified instance
//         var validatedResult = modifiedPerson.EnsureValid();
//         validatedResult.Value.Age.ShouldBe(30);
//     }
//
//     [Test]
//     public void custom_to_string_for_tuple_returns_formatted_representation() {
//         var person = PersonValue.From(("John Doe", 25));
//
//         var result = person.ToString();
//
//         result.ShouldBe("John Doe (25 years old)");
//     }
//
//     [Test]
//     public void custom_to_string_handles_single_character_name() {
//         var person = PersonValue.From(("A", 100));
//
//         var result = person.ToString();
//
//         result.ShouldBe("A (100 years old)");
//     }
//
//     [Test]
//     public void coordinate_value_with_custom_to_string_returns_formatted_representation() {
//         var coordinate = CoordinateValue.From((45.5, -122.6));
//
//         var result = coordinate.ToString();
//
//         result.ShouldBe("45.5°N, 122.6°W");
//     }
//
//     [Test]
//     public void coordinate_value_handles_southern_and_eastern_coordinates() {
//         var coordinate = CoordinateValue.From((-33.9, 151.2));
//
//         var result = coordinate.ToString();
//
//         result.ShouldBe("33.9°S, 151.2°E");
//     }
//
//     // Result-based validation tests
//     [Test]
//     public void validate_returns_success_for_valid_value() {
//         var person = PersonValue.From(("John", 25));
//
//         var result = person.Validate();
//
//         result.IsSuccess.ShouldBeTrue();
//         result.Value.ShouldBe(new Success());
//     }
//
//     [Test]
//     public void validate_returns_error_for_invalid_person_name() {
//         var person = new PersonWithResultValidation { Value = ("", 25) };
//
//         var result = person.Validate();
//
//         result.IsFailure.ShouldBeTrue();
//         result.Error.ErrorCode.ShouldBe(ErrorCode.From("INVALID_PERSON"));
//         result.Error.Problems.Length.ShouldBe(1);
//         result.Error.Problems[0].FieldName.ShouldBe("Name");
//         result.Error.Problems[0].Message.ShouldBe("Name cannot be null or empty");
//     }
//
//     [Test]
//     public void validate_returns_error_for_invalid_person_age() {
//         var person = new PersonWithResultValidation { Value = ("John", -5) };
//
//         var result = person.Validate();
//
//         result.IsFailure.ShouldBeTrue();
//         result.Error.ErrorCode.ShouldBe(ErrorCode.From("INVALID_PERSON"));
//         result.Error.Problems.Length.ShouldBe(1);
//         result.Error.Problems[0].FieldName.ShouldBe("Age");
//         result.Error.Problems[0].Message.ShouldBe("Age must be between 0 and 150");
//         result.Error.Problems[0].AttemptedValue.ShouldBe(-5);
//     }
//
//     [Test]
//     public void validate_returns_multiple_errors_for_multiple_failures() {
//         var person = new PersonWithResultValidation { Value = ("", -5) };
//
//         var result = person.Validate();
//
//         result.IsFailure.ShouldBeTrue();
//         result.Error.ErrorCode.ShouldBe(ErrorCode.From("INVALID_PERSON"));
//         result.Error.Problems.Length.ShouldBe(2);
//
//         var nameError = result.Error.Problems.First(f => f.FieldName == "Name");
//         var ageError  = result.Error.Problems.First(f => f.FieldName == "Age");
//
//         nameError.Message.ShouldBe("Name cannot be null or empty");
//         ageError.Message.ShouldBe("Age must be between 0 and 150");
//         ageError.AttemptedValue.ShouldBe(-5);
//     }
//
//     [Test]
//     public void ensure_valid_uses_validate_method_internally() {
//         var person = new PersonWithResultValidation { Value = ("", 25) };
//
//         var exception = Should.Throw<ValidationException>(() => person.EnsureValid());
//
//         exception.ErrorCode.ShouldBe("INVALID_PERSON");
//         exception.Errors.Length.ShouldBe(1);
//         exception.Errors[0].FieldName.ShouldBe("Name");
//     }
//
//     [Test]
//     public void complex_validation_example_with_business_rules() {
//         var email = new EmailWithComplexValidation { Value = "invalid-email" };
//
//         var result = email.Validate();
//
//         result.IsFailure.ShouldBeTrue();
//         result.Error.ErrorCode.ShouldBe(ErrorCode.From("INVALID_EMAIL"));
//         result.Error.Problems.Length.ShouldBe(2);
//
//         var formatError = result.Error.Problems.First(f => f.FieldName == "Format");
//         var domainError = result.Error.Problems.First(f => f.FieldName == "Domain");
//
//         formatError.Message.ShouldBe("Email must contain @ symbol");
//         domainError.Message.ShouldBe("Email must have a valid domain");
//     }
//
//     [Test]
//     public void custom_equality_compares_only_specified_components() {
//         var person1 = PersonWithCustomEquality.From(("John", 25));
//         var person2 = PersonWithCustomEquality.From(("John", 35));
//
//         person1.ShouldBe(person2, "Same name, different age should be equal");
//         (person1 == person2).ShouldBeTrue("Same name, different age should be equal");
//     }
//
//     [Test]
//     public void custom_equality_hash_codes_match_for_equal_objects() {
//         var person1 = PersonWithCustomEquality.From(("John", 25));
//         var person2 = PersonWithCustomEquality.From(("John", 35)); // Different age, same name
//
//         person1.GetHashCode().ShouldBe(person2.GetHashCode());
//     }
//
//     [Test]
//     public void custom_equality_hash_codes_differ_for_unequal_objects() {
//         var person1 = PersonWithCustomEquality.From(("John", 25));
//         var person2 = PersonWithCustomEquality.From(("Jane", 25)); // Different name
//
//         person1.GetHashCode().ShouldNotBe(person2.GetHashCode());
//     }
//
//     record TestTupleValue : ValueObject<(string Name, int Age), TestTupleValue>;
//
//     record TestComplexTupleValue : ValueObject<(string Name, int Age, bool IsActive, DateTime LastLogin), TestComplexTupleValue>;
//
//     record TestStringValue : ValueObject<string, TestStringValue>;
//
//     record TestIntValue : ValueObject<int, TestIntValue>;
//
//     record ValidatedStringValue : ValueObject<string, ValidatedStringValue> {
//         public override ValidationResult Validate() {
//             var failures = new List<ValidationProblem>();
//
//             if (string.IsNullOrWhiteSpace(Value)) {
//                 failures.Add(ValidationProblem.CreateForField("Value", "Value cannot be null or whitespace", Value));
//             }
//             else if (Value.Length < 3) {
//                 failures.Add(ValidationProblem.CreateForField("Value", "Value must be at least 3 characters long", Value));
//             }
//
//             if (failures.Count > 0) {
//                 return ValidationError.Create("INVALID_STRING", failures.ToArray());
//             }
//
//             return new Success();
//         }
//     }
//
//     record EmailValue : ValueObject<string, EmailValue> {
//         public override ValidationResult Validate() {
//             if (!Value.Contains('@')) {
//                 return ValidationError.Create(
//                     "INVALID_EMAIL",
//                     ValidationProblem.CreateForField("Value", "Email must contain @ symbol", Value)
//                 );
//             }
//
//             return new Success();
//         }
//     }
//
//     record ComplexValidatedValue : ValueObject<string, ComplexValidatedValue> {
//         public override ValidationResult Validate() {
//             var failures = new List<ValidationProblem>();
//
//             if (string.IsNullOrWhiteSpace(Value)) {
//                 failures.Add(ValidationProblem.CreateForField("Value", "Value cannot be null or whitespace", Value));
//             }
//             else if (Value.Length < 5) {
//                 failures.Add(ValidationProblem.CreateForField("Value", "Value must be at least 5 characters", Value));
//             }
//
//             if (!Value.Contains('@')) {
//                 failures.Add(ValidationProblem.CreateForField("Value", "Value must contain @ symbol", Value));
//             }
//
//             if (failures.Count > 0) {
//                 return ValidationError.Create("INVALID_COMPLEX_VALUE", failures.ToArray());
//             }
//
//             return new Success();
//         }
//     }
//
//     record PersonValue : ValueObject<(string Name, int Age), PersonValue> {
//         public override ValidationResult Validate() {
//             var failures = new List<ValidationProblem>();
//
//             if (string.IsNullOrEmpty(Value.Name)) {
//                 failures.Add(ValidationProblem.CreateForField("Name", "Name cannot be null or empty", Value.Name));
//             }
//
//             if (Value.Age is < 0 or > 150) {
//                 failures.Add(ValidationProblem.CreateForField("Age", "Age must be between 0 and 150", Value.Age));
//             }
//
//             if (failures.Count > 0) {
//                 return ValidationError.Create("INVALID_PERSON", failures.ToArray());
//             }
//
//             return new Success();
//         }
//
//         protected override string GetDisplayString() => $"{Value.Name} ({Value.Age} years old)";
//     }
//
//     record CoordinateValue : ValueObject<(double Latitude, double Longitude), CoordinateValue> {
//         public override ValidationResult Validate() {
//             var failures = new List<ValidationProblem>();
//
//             if (Value.Latitude is < -90 or > 90) {
//                 failures.Add(ValidationProblem.CreateForField("Latitude", "Latitude must be between -90 and 90", Value.Latitude));
//             }
//
//             if (Value.Longitude is < -180 or > 180) {
//                 failures.Add(ValidationProblem.CreateForField("Longitude", "Longitude must be between -180 and 180", Value.Longitude));
//             }
//
//             if (failures.Count > 0) {
//                 return ValidationError.Create("INVALID_COORDINATE", failures.ToArray());
//             }
//
//             return new Success();
//         }
//
//         protected override string GetDisplayString() {
//             var latDirection = Value.Latitude >= 0 ? "N" : "S";
//             var lonDirection = Value.Longitude >= 0 ? "E" : "W";
//             return
//                 $"{Math.Abs(Value.Latitude).ToString(System.Globalization.CultureInfo.InvariantCulture)}°{latDirection}, {Math.Abs(Value.Longitude).ToString(System.Globalization.CultureInfo.InvariantCulture)}°{lonDirection}";
//         }
//     }
//
//     record PersonWithResultValidation : ValueObject<(string Name, int Age), PersonWithResultValidation> {
//         public override ValidationResult Validate() {
//             var failures = new List<ValidationProblem>();
//
//             if (string.IsNullOrEmpty(Value.Name)) {
//                 failures.Add(ValidationProblem.CreateForField("Name", "Name cannot be null or empty", Value.Name));
//             }
//
//             if (Value.Age is < 0 or > 150) {
//                 failures.Add(ValidationProblem.CreateForField("Age", "Age must be between 0 and 150", Value.Age));
//             }
//
//             if (failures.Count > 0) {
//                 return ValidationError.Create("INVALID_PERSON", failures.ToArray());
//             }
//
//             return new Success();
//         }
//
//         protected override string GetDisplayString() => $"{Value.Name} ({Value.Age} years old)";
//     }
//
//     record EmailWithComplexValidation : ValueObject<string, EmailWithComplexValidation> {
//         public override ValidationResult Validate() {
//             var failures = new List<ValidationProblem>();
//
//             if (!Value.Contains('@'))
//                 failures.Add(ValidationProblem.CreateForField("Format", "Email must contain @ symbol", Value));
//
//             if (!Value.Contains('.') || Value.EndsWith('.') || Value.StartsWith('.'))
//                 failures.Add(ValidationProblem.CreateForField("Domain", "Email must have a valid domain", Value));
//
//             if (failures.Count > 0)
//                 return ValidationError.Create("INVALID_EMAIL", failures.ToArray());
//
//             return new Success();
//         }
//     }
//
//     record PersonWithCustomEquality : ValueObject<(string Name, int Age), PersonWithCustomEquality> {
//         protected override IEnumerable<object> EqualityComponents() {
//             yield return Value.Name; // Only include Name in equality comparison
//         }
//
//         public override ValidationResult Validate() {
//             var failures = new List<ValidationProblem>();
//
//             if (string.IsNullOrEmpty(Value.Name))
//                 failures.Add(ValidationProblem.CreateForField("Name", "Name cannot be null or empty", Value.Name));
//
//             if (Value.Age is < 0 or > 150)
//                 failures.Add(ValidationProblem.CreateForField("Age", "Age must be between 0 and 150", Value.Age));
//
//             if (failures.Count > 0)
//                 return ValidationError.Create("INVALID_PERSON", failures.ToArray());
//
//             return new Success();
//         }
//
//         protected override string GetDisplayString() => $"{Value.Name} ({Value.Age} years old)";
//     }
// }

// using Kurrent.Client.Streams;
//
// namespace Kurrent.Client.Tests.Filters;
//
// [Category("Filters"), Skip("Filter tests are currently disabled pending implementation of the full filter evaluation engine.")]
// public class FilterParserTests {
//
//     [Test]
//     public void parses_simple_equality_expression_correctly() {
//         var result = FilterExpression.Create("stream = 'orders'");
//
//         result.IsSuccess.ShouldBeTrue();
//         result.Value.IsStreamSpecific.ShouldBeTrue();
//         result.Value.TargetStream.ShouldBe("orders");
//     }
//
//     [Test]
//     public void parses_not_equals_expression_with_exclamation_mark() {
//         var result = FilterExpression.Create("stream != 'test'");
//
//         result.IsSuccess.ShouldBeTrue();
//         result.Value.IsStreamSpecific.ShouldBeTrue();
//     }
//
//     [Test]
//     public void parses_not_equals_expression_with_angle_brackets() {
//         var result = FilterExpression.Create("stream <> 'test'");
//
//         result.IsSuccess.ShouldBeTrue();
//         result.Value.IsStreamSpecific.ShouldBeTrue();
//     }
//
//     [Test]
//     public void parses_comparison_operators_correctly() {
//         var expressions = new[] {
//             "metadata.count > 100",
//             "metadata.count < 50",
//             "metadata.count >= 100",
//             "metadata.count <= 50"
//         };
//
//         foreach (var expression in expressions) {
//             var result = FilterExpression.Create(expression);
//             result.IsSuccess.ShouldBeTrue($"Failed to parse: {expression}");
//         }
//     }
//
//     [Test]
//     public void parses_postgresql_regex_operators() {
//         var expressions = new[] {
//             "stream ~ 'order.*'",
//             "stream ~* 'Order.*'",
//             "stream !~ 'test.*'",
//             "stream !~* 'Test.*'"
//         };
//
//         foreach (var expression in expressions) {
//             var result = FilterExpression.Create(expression);
//             result.IsSuccess.ShouldBeTrue($"Failed to parse: {expression}");
//         }
//     }
//
//     [Test]
//     public void parses_is_null_expressions() {
//         var result = FilterExpression.Create("metadata.userId IS NULL");
//
//         result.IsSuccess.ShouldBeTrue();
//     }
//
//     [Test]
//     public void parses_is_not_null_expressions() {
//         var result = FilterExpression.Create("metadata.userId IS NOT NULL");
//
//         result.IsSuccess.ShouldBeTrue();
//     }
//
//     [Test]
//     public void parses_in_expressions() {
//         var result = FilterExpression.Create("stream IN ('orders', 'payments')");
//
//         result.IsSuccess.ShouldBeTrue();
//     }
//
//     [Test]
//     public void parses_not_in_expressions() {
//         var result = FilterExpression.Create("schema.name NOT IN ('SystemEvent', 'TestEvent')");
//
//         result.IsSuccess.ShouldBeTrue();
//     }
//
//     [Test]
//     public void parses_logical_and_expressions() {
//         var result = FilterExpression.Create("stream = 'orders' AND schema.name = 'OrderCreated'");
//
//         result.IsSuccess.ShouldBeTrue();
//         result.Value.IsStreamSpecific.ShouldBeTrue();
//     }
//
//     [Test]
//     public void parses_logical_or_expressions() {
//         var result = FilterExpression.Create("stream = 'orders' OR stream = 'payments'");
//
//         result.IsSuccess.ShouldBeTrue();
//         result.Value.IsStreamSpecific.ShouldBeFalse(); // Multiple streams, not optimizable
//     }
//
//     [Test]
//     public void parses_complex_expressions_with_parentheses() {
//         var result = FilterExpression.Create("(stream = 'orders' AND schema.name = 'OrderCreated') OR metadata.userId = 'user-123'");
//
//         result.IsSuccess.ShouldBeTrue();
//     }
//
//     [Test]
//     public void returns_error_for_empty_expression() {
//         var result = FilterExpression.Create("");
//
//         result.IsEmpty.ShouldBeTrue();
//     }
//
//     [Test]
//     public void returns_error_for_null_expression() {
//         var result = FilterExpression.Create(null!);
//
//         result.IsEmpty.ShouldBeTrue();
//     }
//
//     [Test]
//     public void returns_error_for_invalid_syntax() {
//         var result = FilterExpression.Create("stream = ");
//
//         result.IsSuccess.ShouldBeFalse();
//         result.Error.ShouldNotBeNullOrWhiteSpace();
//     }
//
//     [Test]
//     public void handles_span_input_correctly() {
//         var input = "stream = 'orders'".AsSpan();
//         var result = FilterExpression.Create(input);
//
//         result.IsSuccess.ShouldBeTrue();
//         result.Value.TargetStream.ShouldBe("orders");
//     }
// }

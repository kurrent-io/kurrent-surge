using System.Collections;
using System.Runtime.CompilerServices;
using Kurrent.Client;

namespace Kurrent.Validation;

/// <summary>
/// Severity level for a validation rule.
/// </summary>
public enum RuleSeverity {
    /// <summary>
    /// A hard failure that blocks validation.
    /// </summary>
    Error,

    /// <summary>
    /// An advisory warning that does not block validation.
    /// </summary>
    Warning
}

/// <summary>
/// Abstract base class for defining validation rules using a fluent API.
/// Uses <see cref="CallerArgumentExpressionAttribute"/> to extract property names
/// at compile time — zero reflection, fully AOT-compatible.
/// </summary>
/// <summary>
/// Non-generic validation contract. Allows <see cref="RuleBuilder{T,TProperty}.SetValidator"/>
/// to accept a validator instance without requiring a <c>class</c> constraint on <c>TProperty</c>.
/// </summary>
public interface IValidateWith<T> {
    ValidationResult Validate(T instance);
}

/// <typeparam name="TValidator">The concrete validator type (CRTP).</typeparam>
/// <typeparam name="T">The type being validated.</typeparam>
[PublicAPI]
public abstract class Validator<TValidator, T> : IValidator<TValidator, T>, IValidateWith<T>
    where T : class
    where TValidator : Validator<TValidator, T>, new() {

    public static readonly TValidator Instance = new();

    readonly List<IRuleAction<T>> _rules = [];

    Func<T, bool>? _activeCondition;

    public virtual string ErrorCode => $"INVALID_{typeof(T).Name.Underscore().ToUpperInvariant()}";

    #region ->> Rule Registration <<-

    /// <summary>
    /// Defines a validation rule for a property accessed via a lambda expression.
    /// The property name is extracted at compile time via <see cref="CallerArgumentExpressionAttribute"/>.
    /// </summary>
    protected RuleBuilder<T, TProperty> RuleFor<TProperty>(
        Func<T, TProperty> accessor,
        [CallerArgumentExpression(nameof(accessor))] string expr = "") {
        var propertyName = ExtractPropertyName(expr);
        var builder = new RuleBuilder<T, TProperty>(accessor, propertyName, _activeCondition);
        _rules.Add(builder);
        return builder;
    }

    /// <summary>
    /// Defines validation rules for each element in a collection property.
    /// </summary>
    protected CollectionRuleBuilder<T, TElement> RuleForEach<TElement>(
        Func<T, IEnumerable<TElement>> accessor,
        [CallerArgumentExpression(nameof(accessor))] string expr = "") {
        var propertyName = ExtractPropertyName(expr);
        var builder = new CollectionRuleBuilder<T, TElement>(accessor, propertyName, _activeCondition);
        _rules.Add(builder);
        return builder;
    }

    /// <summary>
    /// Makes all rules defined inside <paramref name="action"/> conditional on <paramref name="condition"/>.
    /// </summary>
    protected void When(Func<T, bool> condition, Action action) {
        var outer = _activeCondition;

        _activeCondition = outer is null
            ? condition
            : instance => outer(instance) && condition(instance);

        action();
        _activeCondition = outer;
    }

    #endregion

    #region ->> Validation Execution <<-

    /// <summary>
    /// Validates the given instance and returns a <see cref="ValidationResult"/>.
    /// Only <see cref="RuleSeverity.Error"/> problems cause a failure result.
    /// </summary>
    public ValidationResult Validate(T instance) {
        var problems = new List<ValidationProblem>();

        foreach (var rule in _rules)
            rule.Evaluate(instance, problems);

        // filter to Error-severity only for the failure result
        var errors = problems.Where(p => p.Severity == RuleSeverity.Error).ToArray();

        return errors.Length > 0
            ? ValidationResult.Failure(ErrorCode, errors)
            : ValidationResult.Valid;
    }

    #endregion

    static string ExtractPropertyName(string expr) => PropertyNameExtractor.Extract(expr);
}

#region ->> Rule Infrastructure <<-

/// <summary>
/// Non-generic interface for evaluating a rule against an instance.
/// </summary>
interface IRuleAction<in T> {
    void Evaluate(T instance, List<ValidationProblem> problems);
}

/// <summary>
/// Stores a single validation check with its message and severity.
/// </summary>
sealed class RuleDefinition<T, TProperty> {
    public required Func<T, TProperty, bool> Check { get; init; }
    public RuleSeverity Severity { get; set; } = RuleSeverity.Error;

    /// <summary>
    /// Static message string. If null, <see cref="MessageFactory"/> is used.
    /// </summary>
    public string? Message { get; set; }

    /// <summary>
    /// Dynamic message factory. Used when <see cref="Message"/> is null.
    /// </summary>
    public Func<T, string>? MessageFactory { get; set; }

    public string GetMessage(T instance) =>
        Message ?? MessageFactory?.Invoke(instance) ?? "Validation failed.";
}

#endregion

#region ->> RuleBuilder <<-

/// <summary>
/// Fluent builder for defining validation rules on a single property.
/// Supports cascade-stop behavior: the first failing rule on a property stops evaluation of subsequent rules.
/// </summary>
[PublicAPI]
public sealed class RuleBuilder<T, TProperty> : IRuleAction<T> {
    static readonly Comparer<TProperty> Comparer = Comparer<TProperty>.Default;

    readonly Func<T, TProperty> _accessor;
    readonly string _propertyName;
    readonly Func<T, bool>? _condition;
    readonly List<RuleDefinition<T, TProperty>> _definitions = [];

    // for SetValidator — runs after own rules
    IRuleAction<T>? _nestedValidatorAction;

    internal RuleBuilder(Func<T, TProperty> accessor, string propertyName, Func<T, bool>? condition) {
        _accessor     = accessor;
        _propertyName = propertyName;
        _condition    = condition;
    }

    #region ->> Built-in Rules <<-

    /// <summary>
    /// The value must not be null, empty, or whitespace (for strings),
    /// default (for value types), or empty (for collections/arrays).
    /// </summary>
    public RuleBuilder<T, TProperty> NotEmpty() {
        AddRule((_, value) => {
            if (value is null) return false;
            if (value is string s) return !string.IsNullOrWhiteSpace(s);
            if (value is ICollection { Count: 0 }) return false;
            if (value is Array { Length: 0 }) return false;
            return !EqualityComparer<TProperty>.Default.Equals(value, default!);
        }, $"'{_propertyName}' must not be empty.");
        return this;
    }

    /// <summary>
    /// The value must be between <paramref name="min"/> and <paramref name="max"/> (inclusive).
    /// Requires <typeparamref name="TProperty"/> to implement <see cref="IComparable{T}"/>.
    /// </summary>
    public RuleBuilder<T, TProperty> InclusiveBetween(TProperty min, TProperty max) {
        AddRule((_, value) => value is not null && Comparer.Compare(value, min) >= 0 && Comparer.Compare(value, max) <= 0,
            $"'{_propertyName}' must be between {min} and {max}.");
        return this;
    }

    /// <summary>
    /// The value must be greater than <paramref name="threshold"/>.
    /// </summary>
    public RuleBuilder<T, TProperty> GreaterThan(TProperty threshold) {
        AddRule((_, value) => value is not null && Comparer.Compare(value, threshold) > 0,
            $"'{_propertyName}' must be greater than {threshold}.");
        return this;
    }

    /// <summary>
    /// The value must be greater than or equal to <paramref name="threshold"/>.
    /// </summary>
    public RuleBuilder<T, TProperty> GreaterThanOrEqualTo(TProperty threshold) {
        AddRule((_, value) => value is not null && Comparer.Compare(value, threshold) >= 0,
            $"'{_propertyName}' must be greater than or equal to {threshold}.");
        return this;
    }

    /// <summary>
    /// The value must be less than <paramref name="threshold"/>.
    /// </summary>
    public RuleBuilder<T, TProperty> LessThan(TProperty threshold) {
        AddRule((_, value) => value is not null && Comparer.Compare(value, threshold) < 0,
            $"'{_propertyName}' must be less than {threshold}.");
        return this;
    }

    /// <summary>
    /// The value must be less than or equal to <paramref name="threshold"/>.
    /// </summary>
    public RuleBuilder<T, TProperty> LessThanOrEqualTo(TProperty threshold) {
        AddRule((_, value) => value is not null && Comparer.Compare(value, threshold) <= 0,
            $"'{_propertyName}' must be less than or equal to {threshold}.");
        return this;
    }

    /// <summary>
    /// The value must be greater than another property on the same instance.
    /// </summary>
    public RuleBuilder<T, TProperty> GreaterThan(Func<T, TProperty> other) {
        AddRule((instance, value) => value is not null && Comparer.Compare(value, other(instance)) > 0,
            $"'{_propertyName}' must be greater than the compared property.");
        return this;
    }

    /// <summary>
    /// The value must be greater than or equal to another property on the same instance.
    /// </summary>
    public RuleBuilder<T, TProperty> GreaterThanOrEqualTo(Func<T, TProperty> other) {
        AddRule((instance, value) => value is not null && Comparer.Compare(value, other(instance)) >= 0,
            $"'{_propertyName}' must be greater than or equal to the compared property.");
        return this;
    }

    /// <summary>
    /// The value must be less than another property on the same instance.
    /// </summary>
    public RuleBuilder<T, TProperty> LessThan(Func<T, TProperty> other) {
        AddRule((instance, value) => value is not null && Comparer.Compare(value, other(instance)) < 0,
            $"'{_propertyName}' must be less than the compared property.");
        return this;
    }

    /// <summary>
    /// The value must be less than or equal to another property on the same instance.
    /// </summary>
    public RuleBuilder<T, TProperty> LessThanOrEqualTo(Func<T, TProperty> other) {
        AddRule((instance, value) => value is not null && Comparer.Compare(value, other(instance)) <= 0,
            $"'{_propertyName}' must be less than or equal to the compared property.");
        return this;
    }

    /// <summary>
    /// The value must equal the specified <paramref name="expected"/> value.
    /// </summary>
    public RuleBuilder<T, TProperty> Equal(TProperty expected) {
        AddRule((_, value) => EqualityComparer<TProperty>.Default.Equals(value, expected),
            $"'{_propertyName}' must equal {expected}.");
        return this;
    }

    /// <summary>
    /// The value must equal the value of another property on the same instance.
    /// </summary>
    public RuleBuilder<T, TProperty> Equal(Func<T, TProperty> other) {
        AddRule((instance, value) => EqualityComparer<TProperty>.Default.Equals(value, other(instance)),
            $"'{_propertyName}' must equal the compared property.");
        return this;
    }

    /// <summary>
    /// The value must satisfy the given predicate.
    /// </summary>
    public RuleBuilder<T, TProperty> Must(Func<TProperty, bool> predicate, string message) {
        AddRule((_, value) => predicate(value), message);
        return this;
    }

    /// <summary>
    /// The value must satisfy the given predicate that also receives the parent instance.
    /// </summary>
    public RuleBuilder<T, TProperty> Must(Func<T, TProperty, bool> predicate, string message) {
        AddRule(predicate, message);
        return this;
    }

    /// <summary>
    /// The value must be a defined member of its enum type.
    /// Only callable when <typeparamref name="TProperty"/> is an enum.
    /// </summary>
    public RuleBuilder<T, TProperty> IsInEnum() {
        AddRule((_, value) => value is Enum e && Enum.IsDefined(e.GetType(), e),
            $"'{_propertyName}' has a value that is not a valid member of its enum type.");
        return this;
    }

    /// <summary>
    /// Delegates validation to a nested validator instance.
    /// </summary>
    public RuleBuilder<T, TProperty> SetValidator(IValidateWith<TProperty> validator) {
        _nestedValidatorAction = new NestedValidatorAction<T, TProperty>(
            _accessor, _propertyName, _condition, validator.Validate);
        return this;
    }

    /// <summary>
    /// Delegates validation to a nested validator via a validate delegate.
    /// </summary>
    public RuleBuilder<T, TProperty> SetValidator(Func<TProperty, ValidationResult> validate) {
        _nestedValidatorAction = new NestedValidatorAction<T, TProperty>(
            _accessor, _propertyName, _condition, validate);
        return this;
    }

    #endregion

    #region ->> Message & Severity Modifiers <<-

    /// <summary>
    /// Overrides the message on the most recently added rule.
    /// </summary>
    public RuleBuilder<T, TProperty> WithMessage(string message) {
        if (_definitions.Count > 0) {
            var last = _definitions[^1];
            last.Message        = message;
            last.MessageFactory = null;
        }
        return this;
    }

    /// <summary>
    /// Overrides the message on the most recently added rule with a dynamic factory.
    /// </summary>
    public RuleBuilder<T, TProperty> WithMessage(Func<T, string> messageFactory) {
        if (_definitions.Count > 0) {
            var last = _definitions[^1];
            last.Message        = null;
            last.MessageFactory = messageFactory;
        }
        return this;
    }

    /// <summary>
    /// Sets the severity on the most recently added rule.
    /// </summary>
    public RuleBuilder<T, TProperty> WithSeverity(RuleSeverity severity) {
        if (_definitions.Count > 0)
            _definitions[^1].Severity = severity;
        return this;
    }

    #endregion

    #region ->> Evaluation <<-

    void IRuleAction<T>.Evaluate(T instance, List<ValidationProblem> problems) {
        if (_condition is not null && !_condition(instance))
            return;

        var value = _accessor(instance);

        // cascade-stop: first failure on this property stops evaluation
        foreach (var def in _definitions) {
            if (def.Check(instance, value))
                continue;

            problems.Add(ValidationProblem.CreateForField(
                _propertyName,
                def.GetMessage(instance),
                value,
                severity: def.Severity));
            return; // cascade stop
        }

        // nested validator runs after own rules pass
        _nestedValidatorAction?.Evaluate(instance, problems);
    }

    #endregion

    void AddRule(Func<T, TProperty, bool> check, string defaultMessage) =>
        _definitions.Add(new() {
            Check   = check,
            Message = defaultMessage
        });
}

#endregion

#region ->> Collection Rule Builder <<-

/// <summary>
/// Fluent builder for defining validation rules on each element of a collection property.
/// </summary>
[PublicAPI]
public sealed class CollectionRuleBuilder<T, TElement> : IRuleAction<T> {
    readonly Func<T, IEnumerable<TElement>> _accessor;
    readonly string _propertyName;
    readonly Func<T, bool>? _condition;

    InlineValidator<TElement>? _childValidator;

    internal CollectionRuleBuilder(Func<T, IEnumerable<TElement>> accessor, string propertyName, Func<T, bool>? condition) {
        _accessor     = accessor;
        _propertyName = propertyName;
        _condition    = condition;
    }

    /// <summary>
    /// Defines child rules to apply to each element in the collection.
    /// </summary>
    public CollectionRuleBuilder<T, TElement> ChildRules(Action<InlineValidator<TElement>> configure) {
        _childValidator = new();
        configure(_childValidator);
        return this;
    }

    void IRuleAction<T>.Evaluate(T instance, List<ValidationProblem> problems) {
        if (_condition is not null && !_condition(instance))
            return;

        if (_childValidator is null)
            return;

        var collection = _accessor(instance);
        var index = 0;

        foreach (var element in collection) {
            _childValidator.EvaluateElement(element, _propertyName, index, problems);
            index++;
        }
    }
}

#endregion

#region ->> Inline Validator (for ChildRules) <<-

/// <summary>
/// A lightweight validator without the CRTP constraint, used inside
/// <see cref="CollectionRuleBuilder{T,TElement}.ChildRules"/> to define per-element rules.
/// </summary>
[PublicAPI]
public sealed class InlineValidator<T> {
    readonly List<IRuleAction<T>> _rules = [];

    /// <summary>
    /// Defines a rule for a property on the element.
    /// </summary>
    public RuleBuilder<T, TProperty> RuleFor<TProperty>(
        Func<T, TProperty> accessor,
        [CallerArgumentExpression(nameof(accessor))] string expr = "") {
        var propertyName = PropertyNameExtractor.Extract(expr);
        var builder = new RuleBuilder<T, TProperty>(accessor, propertyName, condition: null);
        _rules.Add(builder);
        return builder;
    }

    /// <summary>
    /// Evaluates all rules for a single element, prefixing field names with the collection path and index.
    /// </summary>
    internal void EvaluateElement(T element, string collectionPropertyName, int index, List<ValidationProblem> problems) {
        var elementProblems = new List<ValidationProblem>();

        foreach (var rule in _rules)
            rule.Evaluate(element, elementProblems);

        // prefix field names: "Endpoints[0].Host"
        foreach (var problem in elementProblems) {
            problems.Add(ValidationProblem.CreateForField(
                $"{collectionPropertyName}[{index}].{problem.FieldName}",
                problem.Message,
                problem.AttemptedValue,
                problem.Code,
                problem.Severity));
        }
    }
}

#endregion

#region ->> Property Name Extraction <<-

/// <summary>
/// Shared helper for extracting property names from <see cref="CallerArgumentExpressionAttribute"/> strings.
/// </summary>
static class PropertyNameExtractor {
    /// <summary>
    /// Extracts the property path from a caller expression.
    /// <c>"x => x.Retry.MaxAttempts"</c> → <c>"Retry.MaxAttempts"</c>.
    /// Strips null-forgiving operators (<c>!</c>) from the result.
    /// </summary>
    public static string Extract(string expr) {
        var arrowIdx = expr.IndexOf("=>", StringComparison.Ordinal);
        var body = arrowIdx >= 0 ? expr.AsSpan(arrowIdx + 2).Trim() : expr.AsSpan().Trim();

        var dotIdx = body.IndexOf('.');
        var result = dotIdx >= 0
            ? body.Slice(dotIdx + 1).Trim().ToString()
            : body.ToString();

        // strip null-forgiving operators: "Person!.Name" → "Person.Name"
        return result.Replace("!", "");
    }
}

#endregion

#region ->> Nested Validator Action <<-

/// <summary>
/// Delegates validation to a nested validator and prefixes any resulting problems
/// with the parent property name. Uses a typed delegate — zero reflection, fully AOT-safe.
/// </summary>
sealed class NestedValidatorAction<T, TProperty>(
    Func<T, TProperty> accessor,
    string propertyName,
    Func<T, bool>? condition,
    Func<TProperty, ValidationResult> validate) : IRuleAction<T> {

    public void Evaluate(T instance, List<ValidationProblem> problems) {
        if (condition is not null && !condition(instance))
            return;

        var value = accessor(instance);
        if (value is null) return;

        var result = validate(value);
        if (result.IsValid) return;

        // prefix nested problems with parent property name
        foreach (var problem in result.Problems) {
            var prefixedField = string.IsNullOrWhiteSpace(problem.FieldName)
                ? propertyName
                : $"{propertyName}.{problem.FieldName}";

            problems.Add(ValidationProblem.CreateForField(
                prefixedField,
                problem.Message,
                problem.AttemptedValue,
                problem.Code,
                problem.Severity));
        }
    }
}

#endregion

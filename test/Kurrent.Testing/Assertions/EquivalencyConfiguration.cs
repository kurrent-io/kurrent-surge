using System.Linq.Expressions;
using System.Reflection;

namespace Kurrent.Testing.Assertions;

public class EquivalencyConfiguration {
    readonly Dictionary<Type, Delegate> _customComparers      = new();
    readonly HashSet<string>            _excludedPaths       = [];
    readonly HashSet<string>            _excludedMemberNames = [];
    readonly HashSet<Type>              _excludedMemberTypes = [];
    readonly HashSet<Type>              _valueComparedTypes  = [];
    readonly HashSet<Type>              _memberComparedTypes = [];
    readonly HashSet<string>              _includedPaths      = [];
    readonly List<Func<MemberInfo, bool>> _includedPredicates = [];

    public StringComparison StringComparison      { get; private set; } = StringComparison.Ordinal;
    public double           NumericTolerance      { get; private set; }
    public bool             IgnoreCollectionOrder { get; private set; }
    public bool             NormalizeNewlines      { get; private set; }
    public bool             TrimLeadingWhitespace  { get; private set; }
    public bool             TrimTrailingWhitespace { get; private set; }
    public bool             CompareEnumsByName     { get; private set; }
    public bool             ExcludeNestedObjects   { get; private set; }
    public bool             ExcludeMissingMembers  { get; private set; }
    public int?             MaxRecursionDepth      { get; private set; }

    bool HasInclusions     => _includedPaths.Count > 0 || _includedPredicates.Count > 0;
    bool HasPathExclusions => _excludedPaths.Count > 0;

    public EquivalencyConfiguration Excluding<T>(Expression<Func<T, object>> propertyExpression) {
        if (HasInclusions)
            throw new InvalidOperationException("Cannot use Including and Excluding together.");

        _excludedPaths.Add(ExpressionPathResolver.GetPath(propertyExpression));
        return this;
    }

    public EquivalencyConfiguration Excluding(string propertyPath) {
        if (HasInclusions)
            throw new InvalidOperationException("Cannot use Including and Excluding together.");

        _excludedPaths.Add(propertyPath);
        return this;
    }

    public EquivalencyConfiguration ExcludingMembersNamed(params string[] names) {
        foreach (var name in names)
            _excludedMemberNames.Add(name);
        return this;
    }

    public EquivalencyConfiguration ExcludingType<TType>() {
        _excludedMemberTypes.Add(typeof(TType));
        return this;
    }

    public EquivalencyConfiguration Including<T>(params Expression<Func<T, object>>[] propertyExpressions) {
        if (HasPathExclusions)
            throw new InvalidOperationException("Cannot use Including and Excluding together.");

        foreach (var expr in propertyExpressions)
            _includedPaths.Add(ExpressionPathResolver.GetPath(expr));
        return this;
    }

    public EquivalencyConfiguration Including(Func<MemberInfo, bool> predicate) {
        if (HasPathExclusions)
            throw new InvalidOperationException("Cannot use Including and Excluding together.");

        _includedPredicates.Add(predicate);
        return this;
    }

    public EquivalencyConfiguration Using<T>(Func<T, T, bool> comparer) {
        _customComparers[typeof(T)] = comparer;
        return this;
    }

    public EquivalencyConfiguration ComparingByValue<T>() {
        _valueComparedTypes.Add(typeof(T));
        return this;
    }

    public EquivalencyConfiguration ComparingByMembers<T>() {
        _memberComparedTypes.Add(typeof(T));
        return this;
    }

    public EquivalencyConfiguration WithStringComparison(StringComparison comparison) {
        StringComparison = comparison;
        return this;
    }

    public EquivalencyConfiguration WithNumericTolerance(double tolerance) {
        NumericTolerance = tolerance;
        return this;
    }

    public EquivalencyConfiguration IgnoringCollectionOrder() {
        IgnoreCollectionOrder = true;
        return this;
    }

    public EquivalencyConfiguration IgnoringNewlineStyle() {
        NormalizeNewlines = true;
        return this;
    }

    public EquivalencyConfiguration IgnoringLeadingWhitespace() {
        TrimLeadingWhitespace = true;
        return this;
    }

    public EquivalencyConfiguration IgnoringTrailingWhitespace() {
        TrimTrailingWhitespace = true;
        return this;
    }

    public EquivalencyConfiguration ComparingEnumsByName() {
        CompareEnumsByName = true;
        return this;
    }

    public EquivalencyConfiguration ExcludingNestedObjects() {
        ExcludeNestedObjects = true;
        return this;
    }

    public EquivalencyConfiguration ExcludingMissingMembers() {
        ExcludeMissingMembers = true;
        return this;
    }

    public EquivalencyConfiguration WithMaxRecursionDepth(int depth) {
        ArgumentOutOfRangeException.ThrowIfNegative(depth);
        MaxRecursionDepth = depth;
        return this;
    }

    internal bool ShouldCompareByValue(Type type) => _valueComparedTypes.Contains(type);
    internal bool ShouldCompareByMembers(Type type) => _memberComparedTypes.Contains(type);
    internal bool IsExcludedByName(string memberName) => _excludedMemberNames.Contains(memberName);

    internal bool IsExcludedByType(Type memberType) =>
        _excludedMemberTypes.Count > 0 && _excludedMemberTypes.Any(t => t.IsAssignableFrom(memberType));

    internal bool IsIncluded(MemberInfo member, IEnumerable<string> path) {
        if (!HasInclusions)
            return true;

        if (_includedPredicates.Any(p => p(member)))
            return true;

        var cleanPath = path.Select(p => {
            var bracketIndex = p.IndexOf(" [", StringComparison.Ordinal);
            return bracketIndex >= 0 ? p[..bracketIndex] : p;
        }).Where(p => !string.IsNullOrEmpty(p) && !p.StartsWith(" [")).ToList();

        var pathString         = string.Join(".", cleanPath);
        var relativePathString = cleanPath.Count > 1 ? string.Join(".", cleanPath.Skip(1)) : pathString;

        return _includedPaths.Contains(pathString) || _includedPaths.Contains(relativePathString);
    }

    public bool IsExcluded(IEnumerable<string> path) {
        var cleanPath = path.Select(p => {
                var bracketIndex = p.IndexOf(" [", StringComparison.Ordinal);
                return bracketIndex >= 0 ? p[..bracketIndex] : p;
            }
        ).Where(p => !string.IsNullOrEmpty(p) && !p.StartsWith(" [")).ToList();

        var pathString         = string.Join(".", cleanPath);
        var relativePathString = cleanPath.Count > 1 ? string.Join(".", cleanPath.Skip(1)) : pathString;

        return _excludedPaths.Contains(pathString) || _excludedPaths.Contains(relativePathString);
    }

    public bool TryGetCustomComparer<T>(out Func<T, T, bool>? comparer) {
        if (_customComparers.TryGetValue(typeof(T), out var del)) {
            comparer = (Func<T, T, bool>)del;
            return true;
        }

        comparer = null;
        return false;
    }

    internal bool TryGetCustomComparer(Type type, out Delegate? comparer) =>
        _customComparers.TryGetValue(type, out comparer);
}

static class ExpressionPathResolver {
    public static string GetPath<T>(Expression<Func<T, object>> expression) {
        var visitor = new PropertyPathVisitor();
        visitor.Visit(expression.Body);
        return string.Join(".", visitor.Path.AsEnumerable().Reverse());
    }

    class PropertyPathVisitor : ExpressionVisitor {
        public readonly Stack<string> Path = new();

        protected override Expression VisitMember(MemberExpression node) {
            if (node.Member is PropertyInfo or FieldInfo)
                Path.Push(node.Member.Name);

            return base.VisitMember(node);
        }

        protected override Expression VisitUnary(UnaryExpression node) {
            if (node.NodeType == ExpressionType.Convert)
                return Visit(node.Operand);

            return base.VisitUnary(node);
        }
    }
}

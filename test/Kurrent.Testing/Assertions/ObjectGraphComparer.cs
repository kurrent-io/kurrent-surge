using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace Kurrent.Testing.Assertions;

public class ObjectGraphComparisonException(string message) : Exception(message);

static class ObjectGraphComparer {
    const BindingFlags DefaultBindingFlags = BindingFlags.Public | BindingFlags.Instance;

    public static void AssertEquivalent(
        object? actual,
        object? expected,
        EquivalencyConfiguration? config = null
    ) =>
        CompareObjects(actual, expected, [], new Dictionary<object, IList<object?>>(ReferenceEqualityComparer.Instance), config);

    static void CompareObjects(
        [NotNullIfNotNull(nameof(expected))] object? actual,
        [NotNullIfNotNull(nameof(actual))] object?   expected,
        IList<string>                                 path,
        IDictionary<object, IList<object?>>           previousComparisons,
        EquivalencyConfiguration?                     config,
        int                                           depth = 0
    ) {
        if (BothValuesAreNull(actual, expected, path))
            return;

        var (actualType, expectedType) = GetTypesToCompare(actual, expected, path, config);

        if (config != null && TryCustomCompare(actual, expected, actualType, config, path))
            return;

        if (config?.ShouldCompareByValue(actualType) == true) {
            if (!actual.Equals(expected))
                Throw(actual, expected, path);
            return;
        }

        if (actualType == typeof(string))
            CompareStrings((string)actual, (string)expected, path, config);
        else if (typeof(IEnumerable).IsAssignableFrom(actualType))
            CompareEnumerables((IEnumerable)actual, (IEnumerable)expected, path, previousComparisons, config, depth);
        else if (actualType.IsValueType)
            CompareValueTypes((ValueType)actual, (ValueType)expected, path, config);
        else
            CompareReferenceTypes(actual, expected, actualType, expectedType, path, previousComparisons, config, depth);
    }

    static bool TryCustomCompare(
        object actual, object expected, Type type, EquivalencyConfiguration config, IEnumerable<string> path
    ) {
        if (!config.TryGetCustomComparer(type, out var comparer))
            return false;

        var result = (bool)comparer!.DynamicInvoke(actual, expected)!;

        if (!result)
            Throw(actual, expected, path);

        return true;
    }

    static bool BothValuesAreNull(
        [NotNullWhen(false)] object? actual,
        [NotNullWhen(false)] object? expected,
        IEnumerable<string>          path
    ) {
        if (expected == null) {
            if (actual == null)
                return true;
            Throw(actual, expected, path);
        } else if (actual == null) {
            Throw(actual, expected, path);
        }

        return false;
    }

    static (Type actualType, Type expectedType) GetTypesToCompare(
        object actual, object expected, IList<string> path, EquivalencyConfiguration? config
    ) {
        var expectedType = expected.GetType();
        var actualType   = actual.GetType();

        if (actualType != expectedType && config?.ExcludeMissingMembers != true)
            Throw(actualType, expectedType, path);

        var typeName = $" [{actualType.FullName}]";

        if (path.Count == 0)
            path.Add(typeName);
        else
            path[^1] += typeName;

        return (actualType, expectedType);
    }

    static void CompareValueTypes(
        ValueType actual, ValueType expected, IEnumerable<string> path, EquivalencyConfiguration? config
    ) {
        if (config?.CompareEnumsByName == true && actual.GetType().IsEnum) {
            if (!string.Equals(actual.ToString(), expected.ToString(), StringComparison.Ordinal))
                Throw(actual, expected, path);
            return;
        }

        if (config is { NumericTolerance: > 0 } && IsNumericType(actual.GetType())) {
            var actualDouble   = Convert.ToDouble(actual);
            var expectedDouble = Convert.ToDouble(expected);

            if (Math.Abs(actualDouble - expectedDouble) <= config.NumericTolerance)
                return;
        }

        if (!actual.Equals(expected))
            Throw(actual, expected, path);
    }

    static bool IsNumericType(Type type) => Type.GetTypeCode(type) is
        TypeCode.Int16 or TypeCode.Int32 or TypeCode.Int64 or
        TypeCode.UInt16 or TypeCode.UInt32 or TypeCode.UInt64 or
        TypeCode.Single or TypeCode.Double or TypeCode.Decimal or
        TypeCode.Byte or TypeCode.SByte;

    static void CompareReferenceTypes(
        object actual, object expected, Type actualType, Type expectedType,
        IList<string> path, IDictionary<object, IList<object?>> previousComparisons,
        EquivalencyConfiguration? config, int depth
    ) {
        if (config?.ExcludeNestedObjects == true && depth > 0)
            return;

        if (config is { MaxRecursionDepth: not null, ExcludeNestedObjects: false } && depth > config.MaxRecursionDepth)
            throw new ObjectGraphComparisonException(
                $"Maximum recursion depth {config.MaxRecursionDepth} exceeded at '{string.Join(".", path)}'"
            );

        if (ReferenceEquals(actual, expected) || previousComparisons.Contains(actual, expected))
            return;

        previousComparisons.Record(actual, expected);

        CompareMembers(
            actual, expected, actualType, expectedType, path, previousComparisons, config, depth
        );
    }

    static void CompareStrings(
        string actual, string expected, IEnumerable<string> path, EquivalencyConfiguration? config
    ) {
        if (config?.NormalizeNewlines == true) {
            actual   = actual.ReplaceLineEndings("\n");
            expected = expected.ReplaceLineEndings("\n");
        }

        if (config?.TrimLeadingWhitespace == true) {
            actual   = actual.TrimStart();
            expected = expected.TrimStart();
        }

        if (config?.TrimTrailingWhitespace == true) {
            actual   = actual.TrimEnd();
            expected = expected.TrimEnd();
        }

        var comparison = config?.StringComparison ?? StringComparison.Ordinal;

        if (!actual.Equals(expected, comparison))
            Throw(actual, expected, path);
    }

    static void CompareEnumerables(
        IEnumerable actual, IEnumerable expected, IEnumerable<string> path,
        IDictionary<object, IList<object?>> previousComparisons, EquivalencyConfiguration? config,
        int depth
    ) {
        var expectedList = expected.Cast<object?>().ToList();
        var actualList   = actual.Cast<object?>().ToList();

        if (actualList.Count != expectedList.Count) {
            var newPath = path.Concat(["Count"]);
            Throw(actualList.Count, expectedList.Count, newPath);
        }

        if (config?.IgnoreCollectionOrder == true) {
            CompareEnumerablesOrderIndependent(actualList, expectedList, path, previousComparisons, config, depth);
        } else {
            for (var i = 0; i < actualList.Count; i++) {
                var newPath = path.Concat([$"Element [{i}]"]);
                CompareObjects(actualList[i], expectedList[i], newPath.ToList(), previousComparisons, config, depth);
            }
        }
    }

    static void CompareEnumerablesOrderIndependent(
        IList<object?> actualList, IList<object?> expectedList,
        IEnumerable<string> path, IDictionary<object, IList<object?>> previousComparisons,
        EquivalencyConfiguration? config, int depth
    ) {
        var expectedCopy = expectedList.ToList();

        for (var i = 0; i < actualList.Count; i++) {
            var actualItem = actualList[i];
            var matchFound = false;

            for (var j = 0; j < expectedCopy.Count; j++) {
                try {
                    var newPath = path.Concat([$"Element [{i}]"]).ToList();
                    CompareObjects(actualItem, expectedCopy[j], newPath, previousComparisons, config, depth);
                    expectedCopy.RemoveAt(j);
                    matchFound = true;
                    break;
                } catch (ObjectGraphComparisonException) {
                    // not a match
                }
            }

            if (!matchFound) {
                var newPath = path.Concat([$"Element [{i}]"]);
                Throw(actualItem, "<no matching element>", newPath);
            }
        }
    }

    static void CompareMembers(
        object actual, object expected, Type actualType, Type expectedType,
        IList<string> path, IDictionary<object, IList<object?>> previousComparisons,
        EquivalencyConfiguration? config, int depth
    ) {
        // Use expected's members as the reference set
        IEnumerable<MemberInfo> members = expectedType.GetFields(DefaultBindingFlags);
        members = members.Concat(expectedType.GetProperties(DefaultBindingFlags));

        // When types differ and ExcludeMissingMembers is on, build lookup for actual's members
        Dictionary<string, MemberInfo>? actualMembersByName = null;
        if (actualType != expectedType && config?.ExcludeMissingMembers == true) {
            actualMembersByName = actualType.GetFields(DefaultBindingFlags)
                .Cast<MemberInfo>()
                .Concat(actualType.GetProperties(DefaultBindingFlags))
                .ToDictionary(m => m.Name);
        }

        foreach (var member in members) {
            if (member is PropertyInfo p2 && p2.GetIndexParameters().Length != 0)
                throw new NotSupportedException("Comparing types that have indexers is not supported.");

            // When types differ, skip members not found on actual
            MemberInfo actualMember = member;
            if (actualMembersByName != null) {
                if (!actualMembersByName.TryGetValue(member.Name, out var found))
                    continue;
                actualMember = found;
            }

            if (config?.IsExcludedByName(member.Name) == true)
                continue;

            var memberType = member switch {
                FieldInfo f    => f.FieldType,
                PropertyInfo p => p.PropertyType,
                _              => null
            };

            if (memberType != null && config?.IsExcludedByType(memberType) == true)
                continue;

            var newPath = path.Concat([member.Name]).ToList();

            if (config?.IsIncluded(member, newPath) == false)
                continue;

            if (config?.IsExcluded(newPath) == true)
                continue;

            var actualVal = actualMember switch {
                FieldInfo f    => f.GetValue(actual),
                PropertyInfo p => p.GetValue(actual, []),
                _              => throw new NotSupportedException($"Unsupported member type: {actualMember.GetType()}")
            };

            var expectedVal = member switch {
                FieldInfo f    => f.GetValue(expected),
                PropertyInfo p => p.GetValue(expected, []),
                _              => throw new NotSupportedException($"Unsupported member type: {member.GetType()}")
            };

            CompareObjects(actualVal, expectedVal, newPath, previousComparisons, config, depth + 1);
        }
    }

    [DoesNotReturn]
    static void Throw(object? actual, object? expected, IEnumerable<string> path) {
        var pathStr = string.Join(".", path.Select(p => {
            var bracketIndex = p.IndexOf(" [", StringComparison.Ordinal);
            return bracketIndex >= 0 ? p[..bracketIndex] : p;
        }).Where(p => !string.IsNullOrEmpty(p)));

        var location = string.IsNullOrEmpty(pathStr) ? "root" : pathStr;

        throw new ObjectGraphComparisonException(
            $"At '{location}': expected '{Format(expected)}' but was '{Format(actual)}'"
        );
    }

    static string Format(object? value) => value switch {
        null       => "<null>",
        string s   => $"\"{s}\"",
        Type t     => t.FullName ?? t.Name,
        _          => value.ToString() ?? "<null>"
    };

    static bool Contains(this IDictionary<object, IList<object?>> comparisons, object actual, object? expected) =>
        comparisons.TryGetValue(actual, out var list) && list.Any(e => ReferenceEquals(e, expected));

    static void Record(this IDictionary<object, IList<object?>> comparisons, object actual, object? expected) {
        if (comparisons.TryGetValue(actual, out var list))
            list.Add(expected);
        else
            comparisons.Add(actual, new List<object?>([expected]));
    }
}

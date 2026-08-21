using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace Kurrent.Testkit.TUnit.Assertions;

public class ObjectGraphComparisonException(string message) : Exception(message);

static class ObjectGraphComparer {
    const BindingFlags DefaultBindingFlags = BindingFlags.Public | BindingFlags.Instance;

    public static void AssertEquivalent(
        object? actual,
        object? expected,
        EquivalencyConfiguration? config = null
    ) =>
        CompareObjects(actual, expected, [], new Dictionary<object, IList<object?>>(ReferenceEqualityComparer.Instance), config ?? new());

    static void CompareObjects(
        [NotNullIfNotNull(nameof(expected))] object? actual,
        [NotNullIfNotNull(nameof(actual))] object?   expected,
        IList<string>                                 path,
        IDictionary<object, IList<object?>>           previousComparisons,
        EquivalencyConfiguration                      config,
        int                                           depth = 0
    ) {
        if (BothValuesAreNull(actual, expected, path))
            return;

        if (actual.GetType() != expected.GetType()) {
            if (TryImplicitConversion(actual, expected.GetType(), out var converted)) {
                CompareObjects(converted, expected, path, previousComparisons, config, depth);
                return;
            }

            if (TryImplicitConversion(expected, actual.GetType(), out converted)) {
                CompareObjects(actual, converted, path, previousComparisons, config, depth);
                return;
            }

            var actualIsNumeric   = actual is ValueType && IsNumericType(actual.GetType());
            var expectedIsNumeric = expected is ValueType && IsNumericType(expected.GetType());

            if (actualIsNumeric && expectedIsNumeric) {
                CompareNumericCrossType(actual, expected, path, config);
                return;
            }

            if (expectedIsNumeric && TryImplicitConversionToNumeric(actual, out var numericValue)) {
                CompareNumericCrossType(numericValue, expected, path, config);
                return;
            }

            if (actualIsNumeric && TryImplicitConversionToNumeric(expected, out numericValue)) {
                CompareNumericCrossType(actual, numericValue, path, config);
                return;
            }
        }

        if (TryCustomCompare(actual, expected, actual.GetType(), config, path))
            return;

        if (config.ShouldCompareByValue(actual.GetType())) {
            if (!actual.Equals(expected))
                Throw(actual, expected, path);
            return;
        }

        if (actual is string actualText && expected is string expectedText) {
            CompareStrings(actualText, expectedText, path, config);
            return;
        }

        // Shape decides how a collection is compared, never its concrete type. A List<T>, a T[], an
        // ImmutableArray<T> and a FrozenSet<T> holding the same elements are equivalent, and so are two
        // dictionaries from different implementations. Only the contents are the assertion, so the type
        // check below is reached by everything except a collection.
        if (IsDictionary(actual.GetType()) && IsDictionary(expected.GetType())) {
            CompareDictionaries(ReadEntries(actual), ReadEntries(expected), path, previousComparisons, config, depth);
            return;
        }

        // A string is a sequence of characters, and comparing one to a char collection is far more likely
        // to be a mistake than an intent, so it keeps the type check rather than matching element by
        // element.
        if (actual is not string && expected is not string
         && actual is IEnumerable actualItems && expected is IEnumerable expectedItems) {
            CompareEnumerables(actualItems, expectedItems, path, previousComparisons, config, depth);
            return;
        }

        var (actualType, expectedType) = GetTypesToCompare(actual, expected, path, config);

        if (actualType.IsValueType)
            CompareValueTypes((ValueType)actual, (ValueType)expected, path, config);
        else
            CompareReferenceTypes(actual, expected, actualType, expectedType, path, previousComparisons, config, depth);
    }

    /// <summary>Every dictionary shape, whatever the implementation.</summary>
    /// <remarks>
    /// The interfaces are tested rather than the element type, so a <c>List&lt;KeyValuePair&lt;K,V&gt;&gt;</c>
    /// stays an ordered sequence. <c>IReadOnlyDictionary</c> is included because
    /// <c>FrozenDictionary</c> and the immutable dictionaries reach it without the non-generic
    /// <c>IDictionary</c>.
    /// </remarks>
    static bool IsDictionary(Type type) =>
        typeof(IDictionary).IsAssignableFrom(type)
     || type.GetInterfaces().Any(contract => contract.IsGenericType
         && contract.GetGenericTypeDefinition() is var definition
         && (definition == typeof(IDictionary<,>) || definition == typeof(IReadOnlyDictionary<,>)));

    static List<KeyValuePair<object, object?>> ReadEntries(object value) {
        var entries = new List<KeyValuePair<object, object?>>();

        if (value is IDictionary dictionary) {
            foreach (DictionaryEntry entry in dictionary)
                entries.Add(new KeyValuePair<object, object?>(entry.Key, entry.Value));

            return entries;
        }

        // Reached by the read-only dictionaries, which enumerate as KeyValuePair<,> without exposing the
        // non-generic interface. The pair is a closed generic per key/value pair, so it is read by name.
        foreach (var item in (IEnumerable)value) {
            if (item is null) continue;

            var pair = item.GetType();
            var key  = pair.GetProperty("Key")?.GetValue(item);

            if (key is not null)
                entries.Add(new KeyValuePair<object, object?>(key, pair.GetProperty("Value")?.GetValue(item)));
        }

        return entries;
    }

    /// <remarks>
    /// A dictionary has no order, so entries match by key by default. An explicit strict-ordering request
    /// switches to entry-by-entry comparison, which is the only way to put a dictionary's own iteration
    /// order under test.
    /// </remarks>
    static void CompareDictionaries(
        List<KeyValuePair<object, object?>> actual, List<KeyValuePair<object, object?>> expected,
        IEnumerable<string> path, IDictionary<object, IList<object?>> previousComparisons,
        EquivalencyConfiguration config, int depth
    ) {
        if (actual.Count != expected.Count)
            Throw(actual.Count, expected.Count, path.Concat(["Count"]));

        if (!config.ShouldMatchDictionaryByKey(path)) {
            for (var index = 0; index < expected.Count; index++) {
                var entryPath = path.Concat([$"Entry [{index}]"]).ToList();

                CompareObjects(actual[index].Key, expected[index].Key, entryPath, previousComparisons, config, depth);
                CompareObjects(actual[index].Value, expected[index].Value, entryPath, previousComparisons, config, depth);
            }

            return;
        }

        foreach (var entry in expected) {
            var index = actual.FindIndex(candidate => Equals(candidate.Key, entry.Key));

            if (index < 0)
                Throw("<no entry>", $"an entry keyed '{entry.Key}'", path.Concat([$"[{entry.Key}]"]));

            CompareObjects(
                actual[index].Value, entry.Value, path.Concat([$"[{entry.Key}]"]).ToList(),
                previousComparisons, config, depth
            );
        }
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

    static bool TryImplicitConversion(object value, Type targetType, [NotNullWhen(true)] out object? converted) {
        converted = null;
        var sourceType = value.GetType();

        var method = FindImplicitOperator(sourceType, sourceType, targetType)
                  ?? FindImplicitOperator(targetType, sourceType, targetType);

        if (method == null)
            return false;

        converted = method.Invoke(null, [value])!;
        return true;
    }

    static MethodInfo? FindImplicitOperator(Type declaringType, Type sourceType, Type targetType) =>
        declaringType.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .FirstOrDefault(m => m.Name == "op_Implicit"
                              && m.ReturnType == targetType
                              && m.GetParameters() is [var p] && p.ParameterType == sourceType);

    static bool TryImplicitConversionToNumeric(object value, [NotNullWhen(true)] out object? converted) {
        converted = null;
        var sourceType = value.GetType();

        var method = sourceType.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .FirstOrDefault(m => m.Name == "op_Implicit"
                              && IsNumericType(m.ReturnType)
                              && m.GetParameters() is [var p] && p.ParameterType == sourceType);

        if (method == null)
            return false;

        converted = method.Invoke(null, [value])!;
        return true;
    }

    static void CompareNumericCrossType(
        object actual, object expected, IEnumerable<string> path, EquivalencyConfiguration config
    ) {
        if (IsIeeeFloatingPoint(actual.GetType()) || IsIeeeFloatingPoint(expected.GetType())) {
            var actualDouble   = Convert.ToDouble(actual);
            var expectedDouble = Convert.ToDouble(expected);

            if (config.NumericTolerance > 0 && Math.Abs(actualDouble - expectedDouble) <= config.NumericTolerance)
                return;

            if (actualDouble != expectedDouble)
                Throw(actual, expected, path);
        } else {
            var actualDecimal   = Convert.ToDecimal(actual);
            var expectedDecimal = Convert.ToDecimal(expected);

            if (config.NumericTolerance > 0 && Math.Abs(actualDecimal - expectedDecimal) <= (decimal)config.NumericTolerance)
                return;

            if (actualDecimal != expectedDecimal)
                Throw(actual, expected, path);
        }
    }

    static bool IsIeeeFloatingPoint(Type type) => Type.GetTypeCode(type) is TypeCode.Single or TypeCode.Double;

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
        object actual, object expected, IList<string> path, EquivalencyConfiguration config
    ) {
        var expectedType = expected.GetType();
        var actualType   = actual.GetType();

        if (actualType != expectedType && !config.ExcludeMissingMembers)
            Throw(actualType, expectedType, path);

        var typeName = $" [{actualType.FullName}]";

        if (path.Count == 0)
            path.Add(typeName);
        else
            path[^1] += typeName;

        return (actualType, expectedType);
    }

    static void CompareValueTypes(
        ValueType actual, ValueType expected, IEnumerable<string> path, EquivalencyConfiguration config
    ) {
        if (config.CompareEnumsByName && actual.GetType().IsEnum) {
            if (!string.Equals(actual.ToString(), expected.ToString(), StringComparison.Ordinal))
                Throw(actual, expected, path);
            return;
        }

        if (config.NumericTolerance > 0 && IsNumericType(actual.GetType())) {
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
        EquivalencyConfiguration config, int depth
    ) {
        if (config.ExcludeNestedObjects && depth > 0)
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
        string actual, string expected, IEnumerable<string> path, EquivalencyConfiguration config
    ) {
        if (config.NormalizeNewlines) {
            actual   = actual.ReplaceLineEndings("\n");
            expected = expected.ReplaceLineEndings("\n");
        }

        if (config.TrimLeadingWhitespace) {
            actual   = actual.TrimStart();
            expected = expected.TrimStart();
        }

        if (config.TrimTrailingWhitespace) {
            actual   = actual.TrimEnd();
            expected = expected.TrimEnd();
        }

        var comparison = config.StringComparison;

        if (!actual.Equals(expected, comparison))
            Throw(actual, expected, path);
    }

    static void CompareEnumerables(
        IEnumerable actual, IEnumerable expected, IEnumerable<string> path,
        IDictionary<object, IList<object?>> previousComparisons, EquivalencyConfiguration config,
        int depth
    ) {
        var expectedList = expected.Cast<object?>().ToList();
        var actualList   = actual.Cast<object?>().ToList();

        if (actualList.Count != expectedList.Count) {
            var newPath = path.Concat(["Count"]);
            Throw(actualList.Count, expectedList.Count, newPath);
        }

        if (config.ShouldIgnoreOrder(path)) {
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
        EquivalencyConfiguration config, int depth
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
        EquivalencyConfiguration config, int depth
    ) {
        // Use expected's members as the reference set
        IEnumerable<MemberInfo> members = expectedType.GetFields(DefaultBindingFlags);
        members = members.Concat(expectedType.GetProperties(DefaultBindingFlags));

        // When types differ and ExcludeMissingMembers is on, build lookup for actual's members
        Dictionary<string, MemberInfo>? actualMembersByName = null;
        if (actualType != expectedType && config.ExcludeMissingMembers) {
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

            if (config.IsExcludedByName(member.Name))
                continue;

            var memberType = member switch {
                FieldInfo f    => f.FieldType,
                PropertyInfo p => p.PropertyType,
                _              => null
            };

            if (memberType != null && config.IsExcludedByType(memberType))
                continue;

            var newPath = path.Concat([member.Name]).ToList();

            if (!config.IsIncluded(member, newPath))
                continue;

            if (config.IsExcluded(newPath))
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

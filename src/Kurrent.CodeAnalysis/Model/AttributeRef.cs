namespace Kurrent.CodeAnalysis;

public sealed record AttributeRef(
    TypeSymbol Type,
    ConstantValue[] ConstructorArgs,
    NamedArg[] NamedArgs
) {
    public ConstantValue? Arg(int index) =>
        index >= 0 && index < ConstructorArgs.Length ? ConstructorArgs[index] : null;

    public ConstantValue? Arg(string name) {
        foreach (var a in NamedArgs) {
            if (a.Name == name)
                return a.Value;
        }

        return null;
    }

    public bool Equals(AttributeRef? other) {
        if (other is null)
            return false;

        if (ReferenceEquals(this, other))
            return true;

        return Type.Equals(other.Type)
            && ConstructorArgs.AsSpan().SequenceEqual(other.ConstructorArgs.AsSpan())
            && NamedArgs.AsSpan().SequenceEqual(other.NamedArgs.AsSpan());
    }

    public override int GetHashCode() {
        var hash = HashCombine.Start;
        hash = HashCombine.Add(hash, Type);
        hash = HashCombine.AddRange(hash, ConstructorArgs);
        hash = HashCombine.AddRange(hash, NamedArgs);
        return hash;
    }
}
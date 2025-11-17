namespace Kurrent.Client.Streams;

// [PublicAPI]
// public record CategoryName : ValueObject<string, CategoryName> {
//     public static readonly CategoryName None = new("");
//
//     public override ValidationResult Validate() {
//         if (string.IsNullOrWhiteSpace(Value)) {
//             var violation = ValidationProblem.CreateForField(nameof(CategoryName), "Category name cannot be empty or whitespace.", Value);
//
//             return ValidationError.Create(nameof(CategoryName), violation);
//         }
//
//         return Results.Success;
//     }
//
//     public static implicit operator CategoryName(string _)  => From(_);
//     public static implicit operator string(CategoryName id) => id.ToString();
// }



[PublicAPI]
public readonly record struct CategoryName {
    CategoryName(string value) => Value = value;

    public static readonly CategoryName None = new("");

    public string Value { get; }

    public override string ToString() => Value;

    public static CategoryName From(string value) {
        return string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException($"CategoryName '{value}' is not valid.", nameof(value))
            : new(value);
    }

    public static implicit operator CategoryName(string _) => From(_);
    public static implicit operator string(CategoryName _) => _.ToString();
}

# Composite Formatting

## Overview

Composite formatting allows you to create parameterized resource strings with placeholders (`{0}`, `{1}`, `{2}`, etc.) that are replaced with values at runtime. This is essential for creating flexible, localizable messages with dynamic content.

## Basic Syntax

### Format Items

```
{index[,alignment][:formatString]}
```

- **index**: Zero-based parameter position
- **alignment** (optional): Field width and alignment (positive = right, negative = left)
- **formatString** (optional): Format specifier for the value

### Simple Parameters

```xml
<!-- Resource definition -->
<data name="WelcomeUser" xml:space="preserve">
  <value>Welcome, {0}!</value>
  <comment>{0} = username</comment>
</data>
```

```csharp
// Usage
string message = string.Format(Resources.WelcomeUser, "Alice");
// Output: "Welcome, Alice!"

// IStringLocalizer
string message = _localizer["WelcomeUser", "Alice"];
// Output: "Welcome, Alice!"
```

### Multiple Parameters

```xml
<data name="OrderSummary" xml:space="preserve">
  <value>Order {0} for {1} items totaling {2}</value>
  <comment>{0} = order ID, {1} = quantity, {2} = total price</comment>
</data>
```

```csharp
string message = string.Format(Resources.OrderSummary, "ORD-12345", 5, "$125.50");
// Output: "Order ORD-12345 for 5 items totaling $125.50"
```

## Alignment

### Right Alignment (Positive Width)

```xml
<data name="TableRow" xml:space="preserve">
  <value>|{0,10}|{1,15}|{2,8}|</value>
</data>
```

```csharp
string row = string.Format(Resources.TableRow, "ID", "Name", "Price");
// Output: "|        ID|           Name|   Price|"
```

### Left Alignment (Negative Width)

```xml
<data name="TableRowLeft" xml:space="preserve">
  <value>|{0,-10}|{1,-15}|{2,-8}|</value>
</data>
```

```csharp
string row = string.Format(Resources.TableRowLeft, "ID", "Name", "Price");
// Output: "|ID        |Name           |Price   |"
```

## Format Specifiers

### Numeric Format Specifiers

#### Currency (C)

```xml
<data name="TotalPrice" xml:space="preserve">
  <value>Total: {0:C}</value>
  <comment>{0} = price amount</comment>
</data>
```

```csharp
decimal price = 1234.56m;

// US culture
CultureInfo.CurrentCulture = new CultureInfo("en-US");
string message = string.Format(Resources.TotalPrice, price);
// Output: "Total: $1,234.56"

// Mexican Spanish culture
CultureInfo.CurrentCulture = new CultureInfo("es-MX");
string message = string.Format(Resources.TotalPrice, price);
// Output: "Total: $1,234.56" (MXN symbol)

// Euro culture
CultureInfo.CurrentCulture = new CultureInfo("de-DE");
string message = string.Format(Resources.TotalPrice, price);
// Output: "Total: 1.234,56 €"
```

#### Decimal (D) - Integers Only

```xml
<data name="OrderNumber" xml:space="preserve">
  <value>Order: {0:D6}</value>
  <comment>{0} = order number, padded to 6 digits</comment>
</data>
```

```csharp
int orderNum = 123;
string message = string.Format(Resources.OrderNumber, orderNum);
// Output: "Order: 000123"
```

#### Fixed-Point (F)

```xml
<data name="Measurement" xml:space="preserve">
  <value>Length: {0:F2} meters</value>
  <comment>{0} = length, 2 decimal places</comment>
</data>
```

```csharp
double length = 12.3456;
string message = string.Format(Resources.Measurement, length);
// Output: "Length: 12.35 meters"
```

#### Number (N)

```xml
<data name="Population" xml:space="preserve">
  <value>Population: {0:N0}</value>
  <comment>{0} = population count</comment>
</data>
```

```csharp
int population = 1234567;

// US culture
CultureInfo.CurrentCulture = new CultureInfo("en-US");
string message = string.Format(Resources.Population, population);
// Output: "Population: 1,234,567"

// German culture
CultureInfo.CurrentCulture = new CultureInfo("de-DE");
string message = string.Format(Resources.Population, population);
// Output: "Population: 1.234.567"
```

#### Percent (P)

```xml
<data name="Progress" xml:space="preserve">
  <value>Progress: {0:P0}</value>
  <comment>{0} = progress value (0.0 to 1.0)</comment>
</data>
```

```csharp
double progress = 0.755;
string message = string.Format(Resources.Progress, progress);
// Output: "Progress: 76%" (rounded)
```

#### Hexadecimal (X)

```xml
<data name="ColorCode" xml:space="preserve">
  <value>Color: #{0:X6}</value>
  <comment>{0} = color value</comment>
</data>
```

```csharp
int color = 0xFF5733;
string message = string.Format(Resources.ColorCode, color);
// Output: "Color: #FF5733"
```

### Date and Time Format Specifiers

#### Short Date (d)

```xml
<data name="Birthdate" xml:space="preserve">
  <value>Born: {0:d}</value>
  <comment>{0} = birth date</comment>
</data>
```

```csharp
DateTime date = new DateTime(1990, 5, 15);

// US culture
CultureInfo.CurrentCulture = new CultureInfo("en-US");
string message = string.Format(Resources.Birthdate, date);
// Output: "Born: 5/15/1990"

// German culture
CultureInfo.CurrentCulture = new CultureInfo("de-DE");
string message = string.Format(Resources.Birthdate, date);
// Output: "Born: 15.05.1990"
```

#### Long Date (D)

```xml
<data name="EventDate" xml:space="preserve">
  <value>Event: {0:D}</value>
</data>
```

```csharp
DateTime date = new DateTime(2025, 12, 25);

// US culture
string message = string.Format(Resources.EventDate, date);
// Output: "Event: Thursday, December 25, 2025"

// French culture
CultureInfo.CurrentCulture = new CultureInfo("fr-FR");
string message = string.Format(Resources.EventDate, date);
// Output: "Event: jeudi 25 décembre 2025"
```

#### Short Time (t)

```xml
<data name="MeetingTime" xml:space="preserve">
  <value>Meeting at {0:t}</value>
</data>
```

```csharp
DateTime time = new DateTime(2025, 1, 1, 14, 30, 0);

// US culture (12-hour)
CultureInfo.CurrentCulture = new CultureInfo("en-US");
string message = string.Format(Resources.MeetingTime, time);
// Output: "Meeting at 2:30 PM"

// German culture (24-hour)
CultureInfo.CurrentCulture = new CultureInfo("de-DE");
string message = string.Format(Resources.MeetingTime, time);
// Output: "Meeting at 14:30"
```

#### Custom Date/Time Formats

```xml
<data name="CustomDateTime" xml:space="preserve">
  <value>Updated: {0:yyyy-MM-dd HH:mm:ss}</value>
</data>
```

```csharp
DateTime timestamp = DateTime.Now;
string message = string.Format(Resources.CustomDateTime, timestamp);
// Output: "Updated: 2025-10-28 14:35:22"
```

### Custom Format Strings

```xml
<!-- Custom numeric format -->
<data name="PhoneNumber" xml:space="preserve">
  <value>Phone: {0:(###) ###-####}</value>
</data>
```

```csharp
long phone = 5551234567;
string message = string.Format(Resources.PhoneNumber, phone);
// Output: "Phone: (555) 123-4567"
```

## Parameter Reordering for Localization

Different languages may require different word order. Parameters can be reordered:

```xml
<!-- English: Subject-Verb-Object -->
<data name="UserAction" xml:space="preserve">
  <value>{0} deleted {1}</value>
  <comment>{0} = username, {1} = item name</comment>
</data>

<!-- Japanese: Subject-Object-Verb -->
<data name="UserAction" xml:space="preserve">
  <value>{0}が{1}を削除しました</value>
  <comment>{0} = username, {1} = item name</comment>
</data>
```

```csharp
string message = string.Format(Resources.UserAction, "Alice", "document.pdf");
// English: "Alice deleted document.pdf"
// Japanese: "Aliceがdocument.pdfを削除しました"
```

## Escaping Braces

To include literal braces in the output, double them:

```xml
<data name="JsonExample" xml:space="preserve">
  <value>JSON: {{"key": "{0}"}}</value>
  <comment>{{ and }} are literal braces. {0} = value</comment>
</data>
```

```csharp
string message = string.Format(Resources.JsonExample, "value123");
// Output: "JSON: {"key": "value123"}"
```

## Culture-Specific Formatting

### Using IFormatProvider

```csharp
// Format with specific culture regardless of CurrentCulture
decimal price = 1234.56m;
CultureInfo culture = new CultureInfo("fr-FR");

string formatted = string.Format(culture, Resources.TotalPrice, price);
// Output: "Total: 1 234,56 €"
```

### Culture-Neutral Format Strings

For data that should not be localized (e.g., log files, APIs):

```xml
<data name="LogEntry" xml:space="preserve">
  <value>{0:yyyy-MM-dd HH:mm:ss.fff} [{1}] {2}</value>
  <comment>ISO 8601 timestamp for log entries</comment>
</data>
```

```csharp
// Use InvariantCulture for culture-neutral formatting
string log = string.Format(
    CultureInfo.InvariantCulture,
    Resources.LogEntry,
    DateTime.UtcNow,
    "INFO",
    "Application started"
);
// Output: "2025-10-28 14:35:22.123 [INFO] Application started"
```

## Pluralization Strategies

.NET doesn't have built-in pluralization, but you can implement strategies:

### Simple Conditional Pluralization

```xml
<data name="ItemCountSingular" xml:space="preserve">
  <value>{0} item</value>
</data>
<data name="ItemCountPlural" xml:space="preserve">
  <value>{0} items</value>
</data>
```

```csharp
public static string GetItemCountMessage(int count)
{
    if (count == 1)
        return string.Format(Resources.ItemCountSingular, count);
    else
        return string.Format(Resources.ItemCountPlural, count);
}
```

### ICU Message Format Pattern (Custom Implementation)

```xml
<data name="ItemCountICU" xml:space="preserve">
  <value>{0} {0, plural, =0{items} =1{item} other{items}}</value>
  <comment>ICU MessageFormat style (requires custom parser)</comment>
</data>
```

### Complex Pluralization

```csharp
public static class PluralizationHelper
{
    public static string FormatItemCount(int count)
    {
        return count switch
        {
            0 => Resources.ItemCountNone,              // "No items"
            1 => string.Format(Resources.ItemCountOne, count),   // "1 item"
            _ => string.Format(Resources.ItemCountMany, count)   // "{0} items"
        };
    }
}
```

## Gender-Specific Translations

Some languages require gender-specific forms:

```xml
<!-- English (gender-neutral) -->
<data name="WelcomeUser" xml:space="preserve">
  <value>Welcome, {0}!</value>
</data>

<!-- Spanish (gender-specific options) -->
<data name="WelcomeUserMale" xml:space="preserve">
  <value>¡Bienvenido, {0}!</value>
</data>
<data name="WelcomeUserFemale" xml:space="preserve">
  <value>¡Bienvenida, {0}!</value>
</data>
```

```csharp
public static string GetWelcomeMessage(string name, Gender gender)
{
    if (CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "es")
    {
        return gender == Gender.Female
            ? string.Format(Resources.WelcomeUserFemale, name)
            : string.Format(Resources.WelcomeUserMale, name);
    }

    return string.Format(Resources.WelcomeUser, name);
}
```

## Best Practices

### 1. Always Include Comments

```xml
<data name="OrderConfirmation" xml:space="preserve">
  <value>Order {0} placed on {1:d} for {2:C}</value>
  <comment>
    Order confirmation message.
    {0} = order ID (string)
    {1} = order date (DateTime)
    {2} = total amount (decimal)
  </comment>
</data>
```

### 2. Use Appropriate Format Specifiers

```xml
<!-- ✓ Good - specific format -->
<data name="Price" xml:space="preserve">
  <value>{0:C2}</value>
</data>

<!-- ❌ Bad - no format, inconsistent display -->
<data name="Price" xml:space="preserve">
  <value>{0}</value>
</data>
```

### 3. Consider Parameter Reordering

```xml
<!-- ✓ Good - flexible parameter order -->
<data name="FileOperation" xml:space="preserve">
  <value>{0} moved {1} to {2}</value>
  <comment>{0} = user, {1} = source, {2} = destination. Parameters can be reordered for other languages.</comment>
</data>

<!-- ❌ Bad - hardcoded word order -->
<data name="FileOperation" xml:space="preserve">
  <value>moved from to</value>
  <comment>Missing parameters - can't customize for other languages</comment>
</data>
```

### 4. Use Culture-Specific Formatting

```csharp
// ✓ Good - culture-aware
decimal price = 1234.56m;
string message = string.Format(CultureInfo.CurrentCulture, Resources.Price, price);

// ❌ Bad - invariant culture for user-facing content
string message = string.Format(CultureInfo.InvariantCulture, Resources.Price, price);
```

### 5. Separate Data from Format

```xml
<!-- ✓ Good - parameterized -->
<data name="ErrorMessage" xml:space="preserve">
  <value>Error: {0}</value>
</data>

<!-- ❌ Bad - hardcoded data -->
<data name="ErrorMessage" xml:space="preserve">
  <value>Error: File not found</value>
</data>
```

## Advanced Formatting Examples

### Combining Multiple Format Specifiers

```xml
<data name="TransactionSummary" xml:space="preserve">
  <value>Transaction #{0:D8} on {1:D} for {2:C2} - Status: {3}</value>
  <comment>
    Complete transaction summary.
    {0} = transaction ID (padded 8 digits)
    {1} = date (long date format)
    {2} = amount (currency with 2 decimals)
    {3} = status (string)
  </comment>
</data>
```

```csharp
string summary = string.Format(
    Resources.TransactionSummary,
    12345,                          // Transaction ID
    DateTime.Now,                   // Date
    1234.567m,                      // Amount
    "Completed"                     // Status
);
// Output: "Transaction #00012345 on Thursday, October 28, 2025 for $1,234.57 - Status: Completed"
```

### Alignment with Format Specifiers

```xml
<data name="Report Line" xml:space="preserve">
  <value>|{0,-20}|{1,10:N0}|{2,12:C2}|</value>
  <comment>Report row: {0} = product name, {1} = quantity, {2} = price</comment>
</data>
```

```csharp
string line = string.Format(Resources.ReportLine, "Widget Pro", 1500, 45.99m);
// Output: "|Widget Pro          |     1,500|      $45.99|"
```

## IStringLocalizer Composite Formatting

ASP.NET Core `IStringLocalizer` supports composite formatting directly:

```csharp
// Simple parameter
string message = _localizer["WelcomeUser", userName];

// Multiple parameters
string message = _localizer["OrderSummary", orderId, quantity, total];

// With format specifiers (must be in resource string)
string message = _localizer["FormattedPrice", price]; // {0:C2}
```

## FormattableString (C# Interpolation)

For scenarios where you want interpolation with localization:

```csharp
// Define extension method
public static class LocalizerExtensions
{
    public static string Format(this IStringLocalizer localizer, FormattableString formattable)
    {
        string format = localizer[formattable.Format].Value;
        return string.Format(CultureInfo.CurrentCulture, format, formattable.GetArguments());
    }
}

// Usage
decimal price = 123.45m;
string message = _localizer.Format($"Price: {price:C}");
```

## Summary

- **Composite Formatting**: Flexible parameterized strings with `{index}` placeholders
- **Alignment**: Control field width with positive (right) or negative (left) values
- **Format Specifiers**: Currency (C), Decimal (D), Fixed-point (F), Number (N), Percent (P), Date/Time
- **Culture Awareness**: Automatic formatting based on CurrentCulture
- **Parameter Reordering**: Support different word orders for localization
- **Escaping**: Use `{{` and `}}` for literal braces
- **Best Practices**: Always comment parameters, use appropriate specifiers, consider reordering

Composite formatting is the foundation of flexible, localizable resource strings in .NET applications.

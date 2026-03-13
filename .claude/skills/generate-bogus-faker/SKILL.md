---
name: generate-bogus-faker
description: |
  Generate a complete Faker<T> class from a C# model definition. Use this skill whenever
  the user provides a C# class, record, or struct and wants a Bogus faker for it. Triggers
  on: "generate faker for X", "create a faker", "scaffold bogus for this model", "I need
  test data for Customer", "write a Faker<T>", "faker from this class", "map model to
  Bogus", "build fake data generator", or any request where the user points at a C# type
  and wants a corresponding Bogus data generator. Also use when the user pastes a model
  and says something like "make test data for this", "I need realistic fakes", or "create
  a data builder". Even if the user just says "faker" alongside a class name or file path,
  use this skill. Do NOT use for general Bogus API questions.
argument-hint: "<model-class-or-file-path>"
---

## Purpose

Generate a complete `Faker<T>` class from a C# model. Accept either a pasted class definition
or a file path, analyze its properties, and produce a fully mapped Bogus faker with intelligent
method selection based on property names, types, and data annotation attributes.

## Contents

- [Workflow](#workflow)
    - [Step 1: Accept input](#step-1-accept-input)
    - [Step 2: Analyze the model](#step-2-analyze-the-model)
    - [Step 3: Map properties to Bogus methods](#step-3-map-properties-to-bogus-methods)
    - [Step 4: Generate the faker class](#step-4-generate-the-faker-class)
    - [Step 5: Handle edge cases](#step-5-handle-edge-cases)
    - [Step 6: Present the output](#step-6-present-the-output)
- [Bogus patterns reference](#bogus-patterns-reference)
- [Mapping decision log](#mapping-decision-log)

## Workflow

### Step 1: Accept input

Parse `$ARGUMENTS` to determine the input source:

- If `$ARGUMENTS` is a file path (ends in `.cs` or contains `/`), read the file
- If `$ARGUMENTS` is a class name, search the codebase for the class definition
- If the user pastes a class directly, use the pasted content

```text
Input: /generate-faker OrderRequest
Action: Grep for "class OrderRequest" or "record OrderRequest" in the codebase

Input: /generate-faker src/Models/Order.cs
Action: Read the file directly

Input: /generate-faker <pasted class>
Action: Parse the pasted content
```

If the model cannot be found, report the error and suggest alternatives:

```text
Could not find class "OrderRequest" in the codebase.

Searched for:
- "class OrderRequest"
- "record OrderRequest"
- "record struct OrderRequest"

Try:
- Provide the full file path: /generate-faker src/Models/OrderRequest.cs
- Paste the class definition directly
```

### Step 2: Analyze the model

Extract from the class definition:

- Class name and type (class, record, record struct, struct)
- Constructor parameters (for records and primary constructors)
- All public settable properties (name, type, nullability)
- Data annotation attributes (`[EmailAddress]`, `[Phone]`, `[Range]`, `[StringLength]`, etc.)
- Navigation properties and collection types
- Enum types referenced by properties
- Base classes and interfaces

Determine the instantiation strategy:

- Parameterless constructor available: use default `Faker<T>` construction
- Record or primary constructor only: use `CustomInstantiator`
- Immutable with init-only setters: use `CustomInstantiator` with object initializer

### Step 3: Map properties to Bogus methods

Apply the following mapping rules in priority order. Check name-based patterns first, then
attribute-based, then fall back to type-based.

#### Name-based mapping

Match property names (case-insensitive) to Bogus methods. Use partial and suffix matching:

| Property name pattern                               | Bogus method                      |
|-----------------------------------------------------|-----------------------------------|
| `FirstName`, `GivenName`                            | `f.Name.FirstName()`              |
| `LastName`, `Surname`, `FamilyName`                 | `f.Name.LastName()`               |
| `FullName`, `DisplayName`, `Name` (on Person)       | `f.Name.FullName()`               |
| `Email`, `EmailAddress`, `Mail`                     | `f.Internet.Email()`              |
| `Phone`, `PhoneNumber`, `Telephone`, `Mobile`       | `f.Phone.PhoneNumber()`           |
| `UserName`, `Login`, `Handle`                       | `f.Internet.UserName()`           |
| `Password`, `Secret`                                | `f.Internet.Password()`           |
| `Url`, `Uri`, `Website`, `Link`, `Homepage`         | `f.Internet.Url()`                |
| `Avatar`, `AvatarUrl`, `ProfileImage`               | `f.Internet.Avatar()`             |
| `Street`, `StreetAddress`, `AddressLine`            | `f.Address.StreetAddress()`       |
| `City`, `Town`                                      | `f.Address.City()`                |
| `State`, `Province`, `Region`                       | `f.Address.State()`               |
| `ZipCode`, `PostalCode`, `PostCode`, `Zip`          | `f.Address.ZipCode()`             |
| `Country`, `CountryName`                            | `f.Address.Country()`             |
| `CountryCode`                                       | `f.Address.CountryCode()`         |
| `Latitude`, `Lat`                                   | `f.Address.Latitude()`            |
| `Longitude`, `Lng`, `Lon`                           | `f.Address.Longitude()`           |
| `Company`, `CompanyName`, `Organization`, `Org`     | `f.Company.CompanyName()`         |
| `JobTitle`, `Position`, `Role`, `Title` (on Person) | `f.Name.JobTitle()`               |
| `Department`                                        | `f.Commerce.Department()`         |
| `Price`, `Cost`, `Amount`, `Total`, `Subtotal`      | `f.Finance.Amount()`              |
| `Currency`, `CurrencyCode`                          | `f.Finance.Currency().Code`       |
| `Iban`                                              | `f.Finance.Iban()`                |
| `CreditCard`, `CardNumber`                          | `f.Finance.CreditCardNumber()`    |
| `AccountNumber`, `Account`                          | `f.Finance.Account()`             |
| `ProductName`, `Product`, `ItemName`                | `f.Commerce.ProductName()`        |
| `Sku`, `ProductCode`                                | `f.Commerce.Ean13()`              |
| `Description`, `Summary`, `Bio`, `About`            | `f.Lorem.Sentence()`              |
| `Body`, `Content`, `Text`, `Notes`, `Comment`       | `f.Lorem.Paragraph()`             |
| `Color`, `Colour`                                   | `f.Commerce.Color()`              |
| `FileName`, `File`                                  | `f.System.FileName()`             |
| `MimeType`, `ContentType`, `MediaType`              | `f.System.MimeType()`             |
| `Ip`, `IpAddress`                                   | `f.Internet.Ip()`                 |
| `MacAddress`                                        | `f.Internet.Mac()`                |
| `UserAgent`                                         | `f.Internet.UserAgent()`          |
| `Vin`                                               | `f.Vehicle.Vin()`                 |
| `Ssn`, `SocialSecurityNumber`                       | `f.Random.Replace("###-##-####")` |
| `Age`                                               | `f.Random.Int(18, 80)`            |
| `Quantity`, `Count`, `Number`                       | `f.Random.Int(1, 100)`            |
| `Rating`, `Score`                                   | `f.Random.Int(1, 5)`              |
| `Percentage`, `Percent`                             | `f.Random.Decimal(0, 100)`        |
| `ImageUrl`, `Image`, `Photo`, `Picture`             | `f.Image.PicsumUrl()`             |
| `Slug`                                              | `f.Internet.Slug()`               |
| `Version`                                           | `f.System.Version().ToString()`   |
| `Tag`, `Label`                                      | `f.Lorem.Word()`                  |
| `Tags`, `Labels`, `Keywords` (collection)           | `f.Make(3, () => f.Lorem.Word())` |
| `CreatedAt`, `CreatedOn`, `CreatedDate`             | `f.Date.Past()`                   |
| `UpdatedAt`, `UpdatedOn`, `ModifiedAt`              | `f.Date.Recent()`                 |
| `DeletedAt`                                         | `f.Date.Past().OrNull(f)`         |
| `IsActive`, `IsEnabled`, `IsDeleted`                | `f.Random.Bool()`                 |

#### Attribute-based mapping

Data annotation attributes override type-based defaults:

| Attribute                           | Bogus method                                                     |
|-------------------------------------|------------------------------------------------------------------|
| `[EmailAddress]`                    | `f.Internet.Email()`                                             |
| `[Phone]`                           | `f.Phone.PhoneNumber()`                                          |
| `[Url]`                             | `f.Internet.Url()`                                               |
| `[CreditCard]`                      | `f.Finance.CreditCardNumber()`                                   |
| `[Range(min, max)]`                 | `f.Random.Int(min, max)` or `f.Random.Decimal(min, max)`         |
| `[StringLength(max)]`               | `f.Lorem.Letter(max)` or `f.Random.String2(1, max)`              |
| `[MinLength(n)]` / `[MaxLength(n)]` | Constrain the generated value length accordingly                 |
| `[RegularExpression(pattern)]`      | `f.Random.Replace(pattern)` if simple; note in comment otherwise |

#### Type-based mapping (fallback)

When no name or attribute match applies, map by CLR type:

| Type                   | Bogus method                                              |
|------------------------|-----------------------------------------------------------|
| `string`               | `f.Lorem.Word()`                                          |
| `int`, `long`, `short` | `f.Random.Int()` / `f.Random.Long()` / `f.Random.Short()` |
| `decimal`              | `f.Random.Decimal()`                                      |
| `double`, `float`      | `f.Random.Double()` / `f.Random.Float()`                  |
| `bool`                 | `f.Random.Bool()`                                         |
| `Guid`                 | `f.Random.Guid()`                                         |
| `DateTime`             | `f.Date.Past()`                                           |
| `DateTimeOffset`       | `f.Date.PastOffset()`                                     |
| `DateOnly`             | `DateOnly.FromDateTime(f.Date.Past())`                    |
| `TimeOnly`             | `TimeOnly.FromTimeSpan(f.Date.Timespan())`                |
| `TimeSpan`             | `f.Date.Timespan()`                                       |
| `byte[]`               | `f.Random.Bytes(16)`                                      |
| `Uri`                  | `new Uri(f.Internet.Url())`                               |
| `Enum` types           | `f.PickRandom<TEnum>()`                                   |

### Step 4: Generate the faker class

Produce a complete, compilable C# class following these rules:

- Enable `StrictMode(true)` so Bogus throws a `ValidationException` at generation time if any property lacks a
  `RuleFor` call. This catches missing rules early rather than producing incomplete test data silently
- Always include a `public static readonly {ModelName}Faker Instance = new();` for easy reuse
- Prefer `.UseSeed(seed)` per faker over `Randomizer.Seed` for parallel test safety. Global seeds create ordering
  dependencies between tests
- Map every public settable property with `RuleFor`
- Use `CustomInstantiator` for records and types without parameterless constructors
- Apply context-aware rules where properties are related (email derived from name, etc.)
- Use `.OrNull(f)` for nullable reference type properties
- Use `.Ignore()` for computed or read-only properties that StrictMode would flag
- Include the necessary `using` statements (`Bogus`, `Bogus.Extensions`, model namespace)
- Place the class in a `{ProjectNamespace}.Testing` or `{ProjectNamespace}.Tests` namespace
- Make the faker class `sealed` with a descriptive name: `{ModelName}Faker`
- Create as many faker classes as needed for nested types,
- Never generate fakers for service types (classes with constructor parameters that are interfaces/services)

#### Output template for class with parameterless constructor

```csharp
using Bogus;
using Bogus.Extensions;

namespace MyProject.Testing;

public sealed class OrderFaker : Faker<Order> {
    public static readonly OrderFaker Instance = new();

    public OrderFaker() {
        StrictMode(true);

        RuleFor(o => o.Id, f => f.Random.Guid());
        RuleFor(o => o.CustomerName, f => f.Name.FullName());
        RuleFor(o => o.Email, (f, o) => f.Internet.Email(o.CustomerName));
        RuleFor(o => o.Total, f => f.Finance.Amount(10, 5000));
        RuleFor(o => o.Status, f => f.PickRandom<OrderStatus>());
        RuleFor(o => o.Notes, f => f.Lorem.Sentence().OrNull(f));
        RuleFor(o => o.CreatedAt, f => f.Date.Past());
    }
}
```

#### Output template for record types

```csharp
using Bogus;
using Bogus.Extensions;

namespace MyProject.Testing;

public sealed class CreateOrderCommandFaker : Faker<CreateOrderCommand> {
    public static readonly CreateOrderCommandFaker Instance = new();

    public CreateOrderCommandFaker() {
        StrictMode(true);

        CustomInstantiator(f => new CreateOrderCommand(
            Id: f.Random.Guid(),
            CustomerName: f.Name.FullName(),
            Email: f.Internet.Email(),
            Items: OrderItemFaker.Instance.Generate(3),
            PlacedAt: f.Date.Recent()));
    }
}
```

### Step 5: Handle edge cases

#### Nested types

When the model references another complex type, check whether a faker already exists for it.
If not, generate one and include it in the output:

```csharp
// When Order has a property: Address ShippingAddress
RuleFor(o => o.ShippingAddress, _ => AddressFaker.Instance.Generate());
```

Generate the `AddressFaker` class as well and include both classes in the output.

#### Collections

Map collection properties by generating the inner type:

```csharp
// List<OrderLine> Lines
RuleFor(o => o.Lines, _ => OrderLineFaker.Instance.Generate(3));

// IReadOnlyList<string> Tags
RuleFor(o => o.Tags, f => f.Make(3, () => f.Lorem.Word()));

// Dictionary<string, string> Metadata
RuleFor(o => o.Metadata, f => f.Make(3, () =>
    new KeyValuePair<string, string>(f.Lorem.Word(), f.Lorem.Sentence()))
    .ToDictionary(kv => kv.Key, kv => kv.Value));
```

#### Enums

Generate random enum values with `PickRandom<TEnum>()`. Use `PickRandomWithout` to exclude specific values (e.g.,
sentinel or soft-delete states):

```csharp
RuleFor(o => o.Status, f => f.PickRandom<OrderStatus>());
RuleFor(o => o.Status, f => f.PickRandomWithout(OrderStatus.Deleted));
RuleFor(o => o.Priority, f => f.PickRandom(new[] { "Low", "Medium", "High" }));
```

If the enum is defined in the same file or can be found in the codebase, reference it
directly. If it cannot be resolved, add a comment noting the assumption.

#### Primary constructors (C# 12+)

Treat primary constructor parameters like record parameters. Use `CustomInstantiator`:

```csharp
public class OrderService(ILogger<OrderService> logger, IOrderRepository repository);

// Skip service types; only generate fakers for data models
```

If the type is a service (constructor parameters are interfaces/services), inform the user
that Bogus is intended for data models and suggest using a mocking library instead.

#### FinishWith

`FinishWith` runs after all rules have been applied. Use it for cross-property fixups or final validation where
a `RuleFor` with the `(f, t)` overload is insufficient:

```csharp
.FinishWith((f, u) => {
    u.Email = f.Internet.Email(u.FirstName, u.LastName);
});
```

Only include `FinishWith` when the generated faker genuinely needs post-generation fixups. Do not add it
speculatively.

#### Weighted distribution

Use `f.Random.WeightedRandom` when a property should follow a non-uniform distribution. This is common for ratings,
scores, and status fields where some values are far more frequent than others:

```csharp
.RuleFor(o => o.Rating, f => f.Random.WeightedRandom(
    [1, 2, 3, 4, 5],
    [0.05f, 0.10f, 0.20f, 0.35f, 0.30f]))
```

Only use weighted distribution when the domain clearly calls for it (e.g., ratings skew high, most orders are
completed). Default to uniform `PickRandom` otherwise.

#### Inheritance

When the model inherits from a base class, include `RuleFor` calls for all inherited
properties. Add a comment separating base and derived properties:

```csharp
public sealed class PremiumCustomerFaker : Faker<PremiumCustomer> {
    public static readonly PremiumCustomerFaker Instance = new();

    public PremiumCustomerFaker() {
        StrictMode(true);

        // Base: Customer properties
        RuleFor(c => c.Id, f => f.Random.Guid());
        RuleFor(c => c.Name, f => f.Name.FullName());
        RuleFor(c => c.Email, (f, c) => f.Internet.Email(c.Name));

        // PremiumCustomer properties
        RuleFor(c => c.Tier, f => f.PickRandom<PremiumTier>());
        RuleFor(c => c.Discount, f => f.Random.Decimal(5, 25));
    }
}
```

#### Polymorphic hierarchies

When the base type is abstract or has multiple concrete subtypes, create a dispatcher faker that delegates to
per-subtype fakers. The dispatcher constructor must accept a nullable discriminator parameter (e.g., `DeviceType?`)
so callers can pin a specific subtype or get a random one. This is the key difference from plain inheritance: the
caller controls which concrete type gets generated.

```csharp
public sealed class DeviceFaker : Faker<Device> {
    public static readonly DeviceFaker Instance = new();

    public DeviceFaker(DeviceType? deviceType = null) {
        CustomInstantiator(f => {
            var type = deviceType ?? f.PickRandom<DeviceType>();
            return type switch {
                DeviceType.Thermostat   => ThermostatFaker.Instance.Generate(),
                DeviceType.MotionSensor => MotionSensorFaker.Instance.Generate(),
                DeviceType.SmartLight   => SmartLightFaker.Instance.Generate(),
                DeviceType.DoorLock     => DoorLockFaker.Instance.Generate(),
                _                       => throw new ArgumentOutOfRangeException(nameof(type))
            };
        });
    }
}
```

Each concrete subtype faker is a standard `Faker<T>` with `StrictMode(true)` and `Instance`. The dispatcher faker
does not need `RuleFor` calls because construction is fully delegated.

### Step 6: Present the output

Present the generated faker class(es) with:

1. A brief summary of the model analysis (property count, types detected, instantiation
   strategy chosen)
2. The complete generated code
3. A mapping table showing which properties mapped by name, attribute, or type fallback
4. Any warnings (unresolved types, assumptions made, properties ignored)
5. Usage example showing how to generate instances, including the full set of generation methods:

```csharp
// Singleton instance
var single = OrderFaker.Instance.Generate();
var batch  = OrderFaker.Instance.Generate(50);

// Custom instance with constructor parameter
var faker = new OrderFaker(OrderStatus.Processing);
var single = faker.Generate();

// Override rules for a specific test
var faker = OrderFaker.Instance
    .RuleFor(o => o.Total, f => f.Finance.Amount(100, 10000))
    .RuleFor(o => o.Status, f => f.PickRandom(OrderStatus.Pending, OrderStatus.Processing));

// Other generation methods
List<Order> orders    = faker.GenerateBetween(5, 20);     // random count in range
IEnumerable<Order> lazy = faker.GenerateLazy(1000);       // lazy evaluation
IEnumerable<Order> inf  = faker.GenerateForever().Take(10); // infinite, use with Take()
```

## Bogus patterns reference

Quick reference for Bogus API patterns the generated faker should use where appropriate. These are not always
needed; apply them when the model or domain warrants it.

### Generation methods

| Method                         | Returns              | Use case                                          |
|--------------------------------|----------------------|---------------------------------------------------|
| `Generate()`                   | `T`                  | Single instance                                   |
| `Generate(int count)`          | `List<T>`            | Fixed-count batch                                 |
| `GenerateBetween(int, int)`    | `List<T>`            | Random count in range                             |
| `GenerateLazy(int count)`      | `IEnumerable<T>`     | Lazy evaluation for streaming                     |
| `GenerateForever()`            | `IEnumerable<T>`     | Infinite stream; always pair with `.Take()`        |

### Seed isolation

Prefer `.UseSeed(seed)` per faker over `Randomizer.Seed` for parallel test safety. Global seeds create ordering
dependencies between tests:

```csharp
var faker = new Faker<Order>()
    .UseSeed(42)
    .StrictMode(true)
    .RuleFor(o => o.Id, f => f.Random.Guid());
```

### Enum selection

```csharp
.RuleFor(o => o.Status, f => f.PickRandom<OrderStatus>())            // uniform from all values
.RuleFor(o => o.Status, f => f.PickRandomWithout(OrderStatus.Deleted)) // exclude specific values
.RuleFor(o => o.Category, f => f.Random.Enum<Category>())             // alternative syntax
```

### Weighted distribution

```csharp
.RuleFor(o => o.Rating, f => f.Random.WeightedRandom(
    [1, 2, 3, 4, 5],
    [0.05f, 0.10f, 0.20f, 0.35f, 0.30f]))
```

### FinishWith

Runs after all rules. Use for cross-property fixups or logging:

```csharp
.FinishWith((f, u) => {
    Console.WriteLine($"Generated user: {u.FirstName} {u.LastName}");
});
```

## Mapping decision log

After generating the faker, include a brief mapping summary and if more fakers were generated for nested types, include a mapping summary for each. For example:

```text
Property mappings:
  Id (Guid)              -> f.Random.Guid()              [type]
  FirstName (string)     -> f.Name.FirstName()           [name]
  LastName (string)      -> f.Name.LastName()            [name]
  Email (string)         -> f.Internet.Email()           [attribute: EmailAddress]
  Phone (string?)        -> f.Phone.PhoneNumber().OrNull [name + nullable]
  Status (OrderStatus)   -> f.PickRandom<OrderStatus>()  [type: enum]
  CreatedAt (DateTime)   -> f.Date.Past()                [type]
  Notes (string?)        -> f.Lorem.Sentence().OrNull    [type + nullable]
```

The tag in brackets indicates which mapping rule was applied: `[name]`, `[attribute]`, or
`[type]`.

# generate-bogus-faker Skill Eval Report

Iteration 1. 3 test cases, each run with-skill and without-skill (baseline).

## Aggregate

| Config        | Eval 1 | Eval 2 | Eval 3 | Total | Pass Rate |
|---------------|--------|--------|--------|-------|-----------|
| with_skill    | 9/9    | 7/7    | 5/5    | 21/21 | 100%      |
| without_skill | 3/9    | 5/7    | 3/5    | 11/21 | 52.4%     |

| Metric         | with_skill | without_skill | Delta    |
|----------------|------------|---------------|----------|
| Mean tokens    | 25,267     | 18,669        | +35.3%   |
| Mean duration  | 63.5s      | 65.0s         | -2.3%    |

## Eval 1: Class with settable properties

Prompt: Generate a faker for `Customer` with 8 settable properties including nullable `Notes`,
`List<Order> Orders`, and `OrderStatus Status` enum.

### With skill (9/9)

```csharp
using Bogus;
using Bogus.Extensions;

namespace MyProject.Testing;

public class CustomerFaker : Faker<Customer> {
    public static readonly CustomerFaker Instance = new();

    public CustomerFaker(OrderStatus? status = null) {
        StrictMode(true);

        RuleFor(c => c.Id, f => f.Random.Guid());
        RuleFor(c => c.FirstName, f => f.Name.FirstName());
        RuleFor(c => c.LastName, f => f.Name.LastName());
        RuleFor(c => c.Email, (f, c) => f.Internet.Email(c.FirstName, c.LastName));
        RuleFor(c => c.Status, f => status ?? f.PickRandom<OrderStatus>());
        RuleFor(c => c.Orders, _ => OrderFaker.Instance.Generate(3));
        RuleFor(c => c.CreatedAt, f => f.Date.Past());
        RuleFor(c => c.Notes, f => f.Lorem.Paragraph().OrNull(f));
    }
}
```

### Without skill (3/9)

```csharp
using Bogus;

public sealed class CustomerFaker : Faker<Customer> {
    public CustomerFaker() {
        RuleFor(c => c.Id, f => f.Random.Guid());
        RuleFor(c => c.FirstName, f => f.Person.FirstName);
        RuleFor(c => c.LastName, f => f.Person.LastName);
        RuleFor(c => c.Email, (f, c) => f.Internet.Email(c.FirstName, c.LastName));
        RuleFor(c => c.Status, f => f.PickRandom<OrderStatus>());
        RuleFor(c => c.Orders, f => new OrderFaker().GenerateBetween(0, 5));
        RuleFor(c => c.CreatedAt, f => f.Date.Past(2));
        RuleFor(c => c.Notes, f => f.Random.Bool(0.3f) ? f.Lorem.Sentence() : null);
    }
}
```

### Assertions

| # | Assertion                                            | With | Without |
|---|------------------------------------------------------|------|---------|
| 1 | Inherits `Faker<Customer>`                           | PASS | PASS    |
| 2 | `public static readonly CustomerFaker Instance`      | PASS | FAIL    |
| 3 | `StrictMode(true)`                                   | PASS | FAIL    |
| 4 | All 8 properties have RuleFor                        | PASS | PASS    |
| 5 | Email uses `(f, t)` referencing FirstName/LastName   | PASS | PASS    |
| 6 | Orders uses faker composition via Instance           | PASS | FAIL    |
| 7 | Notes uses `.OrNull(f)`                              | PASS | FAIL    |
| 8 | Constructor has nullable parameter                   | PASS | FAIL    |
| 9 | Class is NOT sealed                                  | PASS | FAIL    |

---

## Eval 2: Record with positional parameters

Prompt: Create a faker for `TransferOrder` record with value objects (`TransferId`, `PlayerId`,
`PlayerName`) and optionally accept a `TransferType`.

### With skill (7/7)

```csharp
using Bogus;
using Bogus.Extensions;

namespace MyProject.Testing;

public class TransferOrderFaker : Faker<TransferOrder> {
    public static readonly TransferOrderFaker Instance = new();

    public TransferOrderFaker(TransferType? type = null) {
        StrictMode(true);

        CustomInstantiator(f => {
            var firstName = f.Name.FirstName();
            var lastName = f.Name.LastName();

            return new TransferOrder(
                Id: TransferId.New(),
                PlayerId: PlayerId.New(),
                PlayerName: PlayerName.From(firstName, lastName),
                Type: type ?? f.PickRandom<TransferType>(),
                Fee: f.Finance.Amount(0, 100_000_000),
                TransferDate: f.Date.PastOffset());
        });
    }
}
```

### Without skill (5/7)

```csharp
using Bogus;
using Transfermarkt.Domain;

public class TransferOrderFaker : Faker<TransferOrder> {
    public TransferOrderFaker(TransferType? transferType = null) {
        CustomInstantiator(f => new TransferOrder(
            Id:           TransferId.New(),
            PlayerId:     PlayerId.New(),
            PlayerName:   PlayerName.From(f.Person.FirstName, f.Person.LastName),
            Type:         transferType ?? f.PickRandom<TransferType>(),
            Fee:          f.Finance.Amount(0, 100_000_000),
            TransferDate: f.Date.RecentOffset()
        ));
    }
}
```

### Assertions

| # | Assertion                                              | With | Without |
|---|--------------------------------------------------------|------|---------|
| 1 | Inherits `Faker<TransferOrder>`                        | PASS | PASS    |
| 2 | `public static readonly TransferOrderFaker Instance`   | PASS | FAIL    |
| 3 | Contains `CustomInstantiator`                          | PASS | PASS    |
| 4 | Constructor accepts `TransferType?`                    | PASS | PASS    |
| 5 | Uses value object factories (`TransferId.New()`, etc.) | PASS | PASS    |
| 6 | Uses `PlayerName.From()` or similar                    | PASS | PASS    |
| 7 | Contains `StrictMode(true)`                            | PASS | FAIL    |

Note: Baseline found the value object factories by searching the codebase (17 tool uses, 76.7s).
With-skill produced the correct pattern from the skill instructions alone (6 tool uses, 46.5s).

---

## Eval 3: Polymorphic hierarchy

Prompt: Faker for abstract `Device` with 4 subtypes (Thermostat, MotionSensor, SmartLight, DoorLock),
each with a Guid Id and type-specific properties.

### With skill (5/5)

```csharp
using Bogus;
using Testing.Datasets.HomeAutomation.Model;

namespace Testing.Datasets.HomeAutomation;

public class DeviceFaker : Faker<Device> {
    public static readonly DeviceFaker Instance = new();

    public DeviceFaker(DeviceType? deviceType = null, string? id = null, string? friendlyName = null) {
        CustomInstantiator(f => {
            var type      = deviceType ?? f.PickRandom(DeviceType.Thermostat,
                                DeviceType.MotionSensor, DeviceType.SmartLight, DeviceType.DoorLock);
            var deviceId  = id ?? $"urn:iot:{f.Random.AlphaNumeric(6).ToLower()}:...";
            var name      = friendlyName ?? $"{f.Lorem.Word()} {type}";
            var isBattery = type is DeviceType.Thermostat;
            var battery   = isBattery ? f.Random.Int(60, 100) : (int?)null;

            return type switch {
                DeviceType.Thermostat   => new Thermostat { Id = deviceId, ... },
                DeviceType.MotionSensor => new MotionSensor { Id = deviceId, ... },
                DeviceType.SmartLight   => new SmartLight { Id = deviceId, ... },
                DeviceType.DoorLock     => new DoorLock { Id = deviceId, ... },
                _ => throw new ArgumentOutOfRangeException(nameof(type))
            };
        });
    }
}
```

### Without skill (3/5)

```csharp
using Bogus;

namespace Testing.Fakers;

public class DeviceFaker : Faker<Device> {
    public DeviceFaker(Type? concreteType = null) {
        CustomInstantiator(f => {
            var type = concreteType ?? PickRandomSubtype(f);
            var id           = f.Random.Guid();
            var friendlyName = $"{f.Commerce.Department()} {type.Name}";

            if (type == typeof(Thermostat)) return new Thermostat { ... };
            if (type == typeof(MotionSensor)) return new MotionSensor { ... };
            if (type == typeof(SmartLight)) return new SmartLight { ... };
            return new DoorLock { ... };
        });
    }
    // Also generated convenience subtype fakers (ThermostatFaker, etc.)
}
```

### Assertions

| # | Assertion                                             | With | Without |
|---|-------------------------------------------------------|------|---------|
| 1 | Contains `DeviceFaker` class                          | PASS | PASS    |
| 2 | Contains `Instance` static property                   | PASS | FAIL    |
| 3 | Handles all 4 subtypes                                | PASS | PASS    |
| 4 | Uses switch expression or conditional branching       | PASS | PASS    |
| 5 | Constructor accepts `DeviceType?` or similar nullable | PASS | FAIL    |

Note: Baseline used `System.Type?` instead of a domain `DeviceType?` enum. Used if/else instead of
switch expression. With-skill agent found the actual codebase models and matched their conventions.

---

## Analysis

**Consistently discriminating assertions** (baseline always fails, with-skill always passes):

- `Instance` static property (0/3 baseline, 3/3 with-skill)
- `StrictMode(true)` (0/2 applicable baseline, 2/2 with-skill)

**Non-discriminating assertions** (both pass):

- Inherits `Faker<T>` (both know this)
- `CustomInstantiator` for records (both know this)
- Value object factories (baseline found via codebase search)

**Token/time tradeoff**: With-skill uses ~35% more tokens (skill file overhead) but comparable wall
time. Baseline sometimes spent more time searching the codebase to discover conventions the skill
provides upfront.

**Qualitative observations**:

- With-skill output is leaner and more consistent across all 3 evals
- Baseline produced extra artifacts (model definitions, enum stubs, convenience subclasses)
- With-skill correctly used `Notes` `.OrNull(f)` pattern; baseline used manual ternary
- With-skill eval 3 adapted to actual codebase models (string IDs, URN format); this may be
  undesirable since it went beyond the prompt

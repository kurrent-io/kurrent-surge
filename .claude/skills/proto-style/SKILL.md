---
name: proto-style
description: |
  Protocol Buffer style conventions for edition 2023, based on Google AIPs.
  Use when writing or editing .proto files, reviewing protobuf definitions,
  designing gRPC APIs, adding new messages/fields/enums, or migrating
  proto3 files to edition 2023.
model: haiku
---

## Edition 2023

This guide targets `edition = "2023"`. Key differences from proto3:

### Field Presence

All singular fields have **explicit presence** by default. Do not use the `optional` keyword; it is unnecessary
and not recognized in editions:

```protobuf
// Good — explicit presence is the default.
int64 checkpoint_id = 2;

// Bad — `optional` keyword is not used in editions.
optional int64 checkpoint_id = 2;
```

To opt out of presence tracking for a specific field (proto3 behavior), set the feature at field level:

```protobuf
int32 counter = 1 [features.field_presence = IMPLICIT];
```

Do not use `LEGACY_REQUIRED`. It exists only for migration from proto2.

### Default Values

Edition 2023 supports explicit default values on fields with explicit presence:

```protobuf
int32 page_size = 1 [default = 100];
bool include_deleted = 2 [default = false];
```

Use defaults sparingly and document them in comments. Do not set defaults on fields with `IMPLICIT` presence.

### Features

Edition 2023 uses feature flags instead of syntax-level behavior. The defaults match proto3 for most features:

| Feature                    | Default            | Notes                              |
|----------------------------|--------------------|------------------------------------|
| `field_presence`           | `EXPLICIT`         | Changed from proto3's `IMPLICIT`.  |
| `enum_type`                | `OPEN`             | Same as proto3.                    |
| `repeated_field_encoding`  | `PACKED`           | Same as proto3.                    |
| `utf8_validation`          | `VERIFY`           | Same as proto3.                    |
| `message_encoding`         | `LENGTH_PREFIXED`  | Same as proto3.                    |
| `json_format`              | `ALLOW`            | Same as proto3.                    |

Set features at the narrowest scope needed. Prefer field-level over file-level overrides:

```protobuf
// Good — targeted override.
int32 counter = 1 [features.field_presence = IMPLICIT];

// Avoid — changes behavior for the entire file.
option features.field_presence = IMPLICIT;
```

### Reserved Fields

Do not quote reserved field names:

```protobuf
// Good — edition 2023 style.
reserved foo, bar;

// Bad — proto3 style.
reserved "foo", "bar";
```

### Migrating from Proto3

To convert a proto3 file to edition 2023:

1. Replace `syntax = "proto3";` with `edition = "2023";`.
2. Remove every `optional` keyword. Fields have explicit presence by default.
3. Remove quotes from `reserved` field names.
4. Leave everything else as-is. Enums, `repeated`, `map`, `oneof`, and imports work the same way.

Before:

```protobuf
syntax = "proto3";

message SearchRequest {
    string query = 1;
    int32 page_size = 2;
    optional string page_token = 3;
    reserved "old_field";
}
```

After:

```protobuf
edition = "2023";

message SearchRequest {
    string query = 1;
    int32 page_size = 2;
    string page_token = 3;
    reserved old_field;
}
```

Bare proto3 fields (implicit presence) become explicit presence after migration. This means fields that were
previously invisible when set to their zero value now serialize and expose `has_*` methods. This is generally
what you want. If you have existing consumers that depend on zero-valued fields being omitted from the wire,
annotate those fields with `[features.field_presence = IMPLICIT]` to preserve the proto3 behavior.

## Comment Style

Based on standard protobuf conventions.

### Single-Line Comments

Use C++/Java style line comments (`//`) on the line before the element:

```protobuf
// The processor identifier.
string processor_id = 1;

// Partitions assigned to this worker.
repeated int32 partitions = 2;
```

### Multi-Line Comments

Use C-style block comments (`/** ... */`) with a margin of `*` for longer documentation:

```protobuf
/**
 * Maximum parallelism (number of partitions) for this processor.
 *
 * This determines the number of partitions used to distribute state
 * across workers for KEY_SHARED subscriptions. It sets the upper bound
 * on how many workers can process this subscription in parallel.
 *
 * Default: 128 (suitable for most use cases)
 */
int32 max_parallelism = 4;
```

### Comment Content

- End all comments with a period.
- Use complete sentences for documentation.
- Document defaults, valid ranges, and behavior.
- Include examples where helpful.

## Field Naming

Based on [AIP-140: Field Names](https://google.aip.dev/140).

### General Rules

| Rule                                 | Example                                 |
|--------------------------------------|-----------------------------------------|
| Use `lower_snake_case`               | `processor_id`, `create_time`           |
| Use singular for non-repeated fields | `partition` not `partitions`            |
| Use plural for repeated fields       | `repeated int32 partitions`             |
| Avoid prepositions                   | `error_reason` not `reason_for_error`   |
| Place adjectives before nouns        | `collected_items` not `items_collected` |

### Booleans

Omit the `is_` prefix unless it would conflict with a reserved word:

```protobuf
// Good
bool deleted = 1;
bool force = 2;

// Bad
bool is_deleted = 1;
bool is_force = 2;

// Exception: reserved words
bool is_new = 3;  // "new" is reserved in many languages
```

### Abbreviations

Use well-known abbreviations:

| Use      | Instead of      |
|----------|-----------------|
| `config` | `configuration` |
| `id`     | `identifier`    |
| `info`   | `information`   |
| `spec`   | `specification` |
| `stats`  | `statistics`    |

## Time and Duration

Based on [AIP-142: Time and Duration](https://google.aip.dev/142).

### Timestamps

Use `google.protobuf.Timestamp` with the `_time` suffix and **imperative verb form**:

```protobuf
// Good - imperative form
google.protobuf.Timestamp create_time = 1;   // when created
google.protobuf.Timestamp update_time = 2;   // when last updated
google.protobuf.Timestamp delete_time = 3;   // when deleted
google.protobuf.Timestamp connect_time = 4;  // when connected
google.protobuf.Timestamp start_time = 5;    // when started

// Bad - past tense or _at suffix
google.protobuf.Timestamp created_at = 1;
google.protobuf.Timestamp created_time = 2;
google.protobuf.Timestamp last_updated_time = 3;
```

### Durations

Use `google.protobuf.Duration` for time spans:

```protobuf
// Time interval between checkpoints.
google.protobuf.Duration interval = 1;

// Maximum time to wait for a response.
google.protobuf.Duration timeout = 2;

// How long the operation took.
google.protobuf.Duration duration = 3;
```

### Common Timestamp Fields

| Field           | Description                                 |
|-----------------|---------------------------------------------|
| `create_time`   | When the resource was created               |
| `update_time`   | When the resource was last modified         |
| `delete_time`   | When the resource was deleted (soft delete) |
| `start_time`    | When an operation/process started           |
| `complete_time` | When an operation/process completed         |
| `expire_time`   | When something expires                      |

## Quantities and Counts

Based on [AIP-141: Quantities](https://google.aip.dev/141).

### No Unsigned Integers

**Never use unsigned integer types** (`uint32`, `uint64`). Many languages don't support them well:

```protobuf
// Good
int32 max_parallelism = 1;
int32 batch_size = 2;
int64 processed_record_count = 3;

// Bad
uint32 max_parallelism = 1;
uint32 batch_size = 2;
uint64 processed_record_count = 3;
```

### Count Fields

Use the `_count` suffix for quantities of items (not `num_` prefix or `total_` prefix):

```protobuf
// Good
int32 worker_count = 1;
int64 record_count = 2;
int64 checkpoint_count = 3;

// Bad
int32 num_workers = 1;
int64 total_records = 2;
int32 workers = 3;  // ambiguous - is this a count or a list?
```

### Units

Include units in field names when applicable:

```protobuf
// Good - unit is clear
int32 distance_km = 1;
int32 size_bytes = 2;
int32 timeout_seconds = 3;  // or use Duration

// Bad - ambiguous unit
int32 distance = 1;
int32 size = 2;
int32 timeout = 3;
```

## Pagination

Based on [AIP-158: Pagination](https://google.aip.dev/158).

### Standard Fields

Always use these exact field names:

**Request:**
```protobuf
message ListProcessorsRequest {
    // Maximum number of items to return.
    int32 page_size = 1;

    // Page token from a previous request.
    string page_token = 2;
}
```

**Response:**
```protobuf
message ListProcessorsResponse {
    // The results (should be field number 1).
    repeated Processor processors = 1;

    // Token for the next page (empty if no more results).
    string next_page_token = 2;
}
```

### Rules

| Rule                        | Description                          |
|-----------------------------|--------------------------------------|
| `page_size` must be `int32` | Not `uint32`                         |
| `page_size` is optional     | Server uses default if not specified |
| `page_token` is opaque      | Clients must not parse it            |
| Results field is first      | Field number 1 in response           |
| Empty `next_page_token`     | Signals end of results               |

### Don't Use

```protobuf
// Bad - non-standard names
int32 limit = 1;           // use page_size
string cursor = 2;         // use page_token
string next_cursor = 3;    // use next_page_token
uint32 page_size = 1;      // use int32
```

## Resource Identifiers

Based on [AIP-122: Resource Names](https://google.aip.dev/122).

### Identifier Fields

Use `_id` suffix for resource identifiers:

```protobuf
message Processor {
    // The processor identifier.
    string processor_id = 1;

    // Other fields...
}

message GetProcessorRequest {
    // The processor identifier.
    string processor_id = 1;
}
```

### References to Other Resources

When referencing another resource, use its identifier field name:

```protobuf
message Checkpoint {
    // The processor this belongs to.
    string processor_id = 1;

    // The checkpoint identifier.
    int64 checkpoint_id = 2;
}
```

### Alternative: `name` Field

Google AIPs recommend using `name` for the resource's full path. Either approach is acceptable, but be consistent:

```protobuf
// Option A: Using _id (our preference)
message Processor {
    string processor_id = 1;
}

// Option B: Using name (Google's recommendation)
message Processor {
    // Format: processors/{processor_id}
    string name = 1;
}
```

## Enumerations

Based on [AIP-126: Enumerations](https://google.aip.dev/126).

### Naming

- Enum type names: `PascalCase`
- Enum values: `UPPER_SNAKE_CASE`
- Prefix values with the enum type name

```protobuf
enum ProcessorState {
    PROCESSOR_STATE_UNSPECIFIED = 0;
    PROCESSOR_STATE_RUNNING = 1;
    PROCESSOR_STATE_IDLE = 2;
    PROCESSOR_STATE_CHECKPOINTING = 3;
}
```

### First Value

Always have an `UNSPECIFIED` value at position `0`:

```protobuf
enum SubscriptionType {
    SUBSCRIPTION_TYPE_UNSPECIFIED = 0;  // Required first value
    SUBSCRIPTION_TYPE_EXCLUSIVE = 1;
    SUBSCRIPTION_TYPE_SHARED = 2;
    SUBSCRIPTION_TYPE_KEY_SHARED = 3;
}
```

### Comments

Document each value:

```protobuf
enum DisconnectReason {
    DISCONNECT_REASON_UNSPECIFIED = 0;

    // Graceful shutdown requested by worker.
    DISCONNECT_REASON_SHUTDOWN = 1;

    // Worker reported an error.
    DISCONNECT_REASON_ERROR = 2;

    // Worker didn't acknowledge checkpoint in time.
    DISCONNECT_REASON_CHECKPOINT_TIMEOUT = 3;
}
```

## Message Structure

### Field Ordering

1. Resource identifier (`processor_id`, `name`)
2. Required/important fields
3. Optional fields
4. Timestamps (`create_time`, `update_time`)
5. Metadata (`map<string, string> metadata`)

```protobuf
message ProcessorInfo {
    // 1. Identifier
    string processor_id = 1;

    // 2. Important fields
    ProcessorSettings settings = 2;
    ProcessorState state = 3;

    // 3. Related data
    repeated WorkerInfo workers = 4;
    CheckpointInfo last_checkpoint = 5;
    ProcessorStats stats = 6;

    // 4. Timestamps
    google.protobuf.Timestamp create_time = 7;
}
```

### Oneof

Use `oneof` for mutually exclusive fields:

```protobuf
message StartPosition {
    oneof position {
        StartFromBeginning beginning = 1;
        StartFromEnd end = 2;
        int64 log_position = 3;
    }
}
```

### Presence Tracking

All singular fields track presence by default in edition 2023. A field set to its zero value is distinguishable
from an unset field. No keyword is needed:

```protobuf
message Connected {
    repeated int32 partitions = 1;

    // Presence tracked by default — has_checkpoint_id() is available.
    int64 checkpoint_id = 2;
}
```

## References

- [AIP-121: Resource-oriented design](https://google.aip.dev/121)
- [AIP-122: Resource names](https://google.aip.dev/122)
- [AIP-140: Field names](https://google.aip.dev/140)
- [AIP-141: Quantities](https://google.aip.dev/141)
- [AIP-142: Time and duration](https://google.aip.dev/142)
- [AIP-145: Ranges](https://google.aip.dev/145)
- [AIP-148: Standard fields](https://google.aip.dev/148)
- [AIP-158: Pagination](https://google.aip.dev/158)
- [Protocol Buffers Style Guide](https://protobuf.dev/programming-guides/style/)
- [Protocol Buffers Documentation](https://protobuf.dev/)
- [Protocol Buffers GitHub](https://github.com/protocolbuffers/protobuf)

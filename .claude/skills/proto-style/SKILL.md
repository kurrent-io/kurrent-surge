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

End all comments with a period. Use complete sentences. Document defaults, valid ranges, and behavior.

### Top-Level Comments

All comments on services, RPCs, messages, and enums must use `/** */` block style, even for single-line docs.
This establishes a clear visual hierarchy: block comments for top-level elements, line comments for fields within.

```protobuf
/**
 * The processor service.
 */
service Processors {
    /**
     * Get processor information.
     */
    rpc GetProcessor(GetProcessorRequest) returns (GetProcessorResponse);
}

/**
 * Processor settings and configuration.
 */
message Processor {
    string processor_id = 1;
}
```

### Field Comments

Use `//` for short field descriptions. Use `/** */` block comments for fields that need multi-line documentation:

```protobuf
// The processor identifier.
string processor_id = 1;

/**
 * Maximum parallelism (number of partitions) for this processor.
 *
 * This determines the number of partitions used to distribute state
 * across workers for KEY_SHARED subscriptions.
 *
 * Default: 128 (suitable for most use cases)
 */
int32 max_parallelism = 4;
```

### Tables in Comments

Tables within comments must use padded columns with aligned pipes:

```protobuf
/**
 * Field behavior options.
 *
 * | Behavior | Description                 |
 * |----------|-----------------------------|
 * | OPTIONAL | Field is not required.      |
 * | REQUIRED | Field must be present.      |
 * | COMPUTED | Server computes this value. |
 */
message FieldOptions {
    string behavior = 1;
}
```

## Field Naming

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

- Never use unsigned integer types (`uint32`, `uint64`). Many languages don't support them well.
- Use `_count` suffix for quantities (`worker_count` not `num_workers` or `total_workers`).
- Include units in field names when applicable (`size_bytes`, `timeout_seconds`). Or use `Duration`.

```protobuf
// Good
int32 worker_count = 1;
int64 size_bytes = 2;
int32 timeout_seconds = 3;

// Bad
uint32 worker_count = 1;   // no unsigned types
int32 num_workers = 2;     // use _count suffix
int32 timeout = 3;         // ambiguous unit
```

## Pagination

- Use `page_size` (`int32`) for max items, not `limit`.
- Use `page_token`/`next_page_token` for cursors, not `cursor`/`next_cursor`.
- `page_size` is optional; server uses a default if not specified.
- `page_token` is opaque; clients must not parse it.
- Results field must be field number 1 in the response.
- Empty `next_page_token` signals end of results.

```protobuf
message ListProcessorsRequest {
    int32 page_size = 1;
    string page_token = 2;
}

message ListProcessorsResponse {
    repeated Processor processors = 1;
    string next_page_token = 2;
}
```

## Resource Identifiers

Use `_id` suffix for resource identifiers. When referencing another resource, use its identifier field name.
Google AIPs recommend `name` for full resource paths; either approach works, but be consistent.

```protobuf
message Checkpoint {
    // The processor this belongs to.
    string processor_id = 1;
    // The checkpoint identifier.
    int64 checkpoint_id = 2;
}
```

## Enumerations

- Enum type names: `PascalCase`. Enum values: `UPPER_SNAKE_CASE`.
- Prefix values with the enum type name.
- Always start with an `UNSPECIFIED = 0` value.
- Document each value. No blank lines between entries.

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

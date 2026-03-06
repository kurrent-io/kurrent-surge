# KurrentDB SQL Protocol

Need to define a canonical set of fields for records and streams that are available for filtering in KurrentDB. 
This will ensure consistency across different query interfaces (WebUI, secondary indexing, connectors) and provide a clear mapping between the underlying data model and the query syntax.

For this we now have a new ubiquituous language where `Record` refers to what was previsously called an `Event` 
written to the log.

With the schema registry event type becomes schema name, and the schema format is also stored on the record to allow for proper deserialization by consumers.

## Record Schema

Record as the unit of data in KurrentDB, representing an individual event or message. Each record has a set of metadata fields and a data payload. The following table describes the canonical fields available for filtering on records:

### Record Fields

| #  | Concept                | Description                                                                                                                                                                                                                                                                                                                                                                                                 |
|----|------------------------|-------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| 1  | `Record ID`            | The unique identifier for the record, typically a GUID.                                                                                                                                                                                                                                                                                                                                                     |
| 2  | `Stream Name`          | The name of the stream the record belongs to.                                                                                                                                                                                                                                                                                                                                                               |
| 3  | `Category Name`        | The category derived from the stream name (text before the first `-`).                                                                                                                                                                                                                                                                                                                                      |
| 4  | `Schema Name`          | The name of the schema associated with the record, often corresponding to the event type. This is determined at the time the record is written to the log and is immutable. Consumers can use this information to understand the structure of the record's data payload and apply appropriate deserialization logic.                                                                                        |
| 5  | `Schema Format`        | The format of the record's data payload, such as `json`, `protobuf`, `avro`, or `bytes`. This field indicates how the `Data` field should be interpreted and deserialized. It is immutable and determined at the time the record is written to the log. Consumers can use this information to apply the correct deserialization logic when accessing the record's data.                                     |
| 6  | `Schema ID`            | The unique identifier for the schema associated with the record. This is typically a GUID that references the schema definition used to serialize the record's data. It allows consumers to look up the schema for deserialization and validation purposes.                                                                                                                                                 |
| 7  | `Stream Revision`      | The position of the record within its stream, starting at 1 for the first record. This reflects the order of records in the stream and is immutable once assigned. It can be used for efficient range queries and pagination within a stream.                                                                                                                                                               |
| 8  | `Log Position`         | The global position of the record in the log, typically represented as a commit position. This is a monotonically increasing value that reflects the order of records in the log. It is immutable and can be used for efficient range queries and pagination.                                                                                                                                               |
| 9  | `Create Timestamp`     | When the record was created in the log. This is typically the commit timestamp when the record was appended to the log. It is immutable and reflects the actual time of record creation, regardless of when it is read or indexed.                                                                                                                                                                          |
| 10 | `Properties`           | A map of custom key-value pairs associated with the record. This can include any additional metadata that producers attach to the record, such as correlation IDs, user IDs, or other contextual information. The keys and values are both strings, and this field allows for flexible filtering based on custom attributes.                                                                                |
| 11 | `Data`                 | The raw data payload in bytes of the record. For JSON-formatted records, this field contains the JSON object. For other formats (protobuf, avro, bytes), this field may be NULL or contain a binary representation depending on the implementation.                                                                                                                                                         |
| 12 | `Value / Json Payload` | When the data format is deserializable (JSON), this field provides direct access to a JSON payload for convenient filtering. For non-deserializable formats, this field will be NULL.                                                                                                                                                                                                                       |
| 13 | `Is Deleted`           | If the record has been deleted, this field is true. Deleted records have had their data and properties removed, and they are marked as deleted in the log. They may still exist in the log for a period of time (depending on retention settings) but will not be returned in queries unless explicitly included. This allows for accurate historical queries while respecting deletion requests.           |
| 14 | `Is Redacted`          | If the record has been redacted, this field is true. Redacted records have had their data and properties removed, but they still exist in the log with their metadata (stream, schema, timestamps) intact. This allows for accurate historical queries while respecting redaction requests.                                                                                                                 |
| 15 | `Sequence Number`      | A sequential counter that increments with each record in the order they are consumed. This is not a global log position but is specific to the context of the operation (e.g., a read session). It can be used to track the order of records as they are processed, even if they come from different streams or categories. This field is planned for future implementation and is not currently available. |

### Cross-System Name Mapping

Shows where each alias originates and what the corresponding field is in different contexts (WebUI, secondary indexing, connectors). 
This ensures consistency across different query interfaces and helps users understand how to reference these fields in various scenarios.

| #  | Concept                | Record Filters Spec  | WebUI SQL Syntax | Secondary Indexing SQL Table | JObject: User Indexes         | JObject: Connectors  |
|----|------------------------|----------------------|------------------|------------------------------|-------------------------------|----------------------|
| 1  | `Record ID`            | `record_id`          | -                | -                            | `rec.id`                      | `rec.id`             |
| 2  | `Stream Name`          | `stream`             | `stream`         | `stream`                     | `rec.position.stream`         | `rec.stream`         |
| 3  | `Category Name`        | `category` *[3]*     | `category`       | `category`                   | `rec.category` *[3]*          | `rec.category` *[3]* |
| 4  | `Schema Name`          | `schema_name`        | `event_type`     | `event_type`                 | `rec.schema.name`             | `rec.schema.name`    |
| 5  | `Schema Format`        | `schema_format`      | ?                | `schema_format`              | `rec.schema.format`           | `rec.schema.format`  |
| 6  | `Schema ID`            | `schema_id`          | ?                | `schema_id`                  | `rec.schema.id`               | `rec.schema.id`      |
| 7  | `Stream Revision`      | `stream_revision`    | `event_number`   | `event_number`               | `rec.position.streamRevision` | `rec.revision`       |
| 8  | `Log Position`         | `position`           | `log_position`   | `log_position`               | `rec.position.logPosition`    | `rec.position`       |
| 9  | `Create Timestamp`     | `timestamp`          | `created_at`     | `created`                    | `rec.timestamp`               | `rec.timestamp`      |
| 10 | `Properties`           | `properties`         | `metadata`       | `metadata`                   | `rec.properties`              | `rec.properties`     |
| 11 | `Data`                 | `data` *[1]*         | -                | -                            | `rec.value`                   | `rec.value`          |
| 12 | `Value / Json Payload` | `value` *[2]*        | `data`           | `data`                       | `rec.schema.format`           | `rec.schema.format`  |
| 13 | `Is Deleted`           | `rec.deleted` *[2]*  | `is_deleted`     | `is_deleted`                 | -                             | -                    |
| 14 | `Is Redacted`          | `rec.redacted` *[2]* | -                | -                            | -                             | -                    |
| 15 | `Sequence Number`      | `rec.sequence`       | -                | -                            | `rec.sequence`                | `rec.sequence`       |                                                   

[1] — indicates fields that cannot be used for filtering
[2] — indicates fields that are not currently available but are planned for future implementation.
[3] — derived fields computed from other fields

---

## Stream Schema

- FROM metadata sources like `FROM metadata WHERE stream_name` the following additional columns are available for filtering on stream-level metadata.

| # | Concept               | Record Filters Spec    | Type     | Aliases: DotNotation   |
|---|-----------------------|------------------------|----------|------------------------|
| 2 | `Stream Name`         | `stream_name`          | STRING   | `stream.name`          |
| 2 | `Stream Revision`     | `stream_revision`      | INT64    | `stream.revision`      |
| 2 | `Create Timestamp`    | `stream_created_at`    | DATETIME | `stream.created_at`    |
| 5 | `Max Age`             | `stream_max_age`       | INT64    | `stream.max_age`       |
| 6 | `Max Size`            | `stream_max_size`      | INT64    | `stream.max_size`      |
| 1 | `Deleted`             | `stream_deleted`       | BOOL     | `stream.deleted`       |
| 2 | `Delete Timestamp`    | `stream_deleted_at`    | DATETIME | `stream.deleted_at`    |
| 3 | `Tombstoned`          | `stream_tombstoned`    | BOOL     | `stream.tombstoned`    |
| 4 | `Tombstone Timestamp` | `stream_tombstoned_at` | DATETIME | `stream.tombstoned_at` |

*** — indicates fields that are not currently available but are planned for future implementation.


## Virtual Table Name Mapping

Complete mapping between FROM source syntax and underlying stream addressing:

| FROM Source                             | Internal Stream Resolution                      | Notes                            |
|-----------------------------------------|-------------------------------------------------|----------------------------------|
| `FROM stream:'{name}'`                  | Direct read of stream `{name}`                  | Quotes optional for simple names |
| `FROM category:{name}`                  | `StreamFilter.Prefix("$idx-ce-{name}")`         | Built-in secondary index         |
| `FROM index:{name}`                     | `StreamFilter.Prefix("$idx-user-{name}")`       | User-defined index               |
| `FROM index:{name}` + field `= '{val}'` | `StreamFilter.Prefix("$idx-user-{name}:{val}")` | Index field filter               |
| `FROM records`                          | `$all`, no stream filter                        | Literal `$all` reference         |
| `FROM all_events`                       | `$all`, no stream filter                        | All events in the log            |
| `FROM log`                              | `$all`, no stream filter                        | Alias for `all`                  |
| `FROM $all`                             | `$all`, no stream filter                        | Literal `$all` reference         |
| *(no FROM)*                             | Caller-determined context                       | Pure predicate mode              |

### `$` Notation for virtual stream names:

| Pattern                         | Description                      | Example                                 |
|---------------------------------|----------------------------------|-----------------------------------------|
| `$idx-ce-{category}`            | Category secondary index         | `$idx-ce-order`                         |
| `$idx-et-{eventType}`           | Event type secondary index       | `$idx-et-OrderPlaced`                   |
| `$idx-user-{indexName}`         | User-defined index (all records) | `$idx-user-orders-by-country`           |
| `$idx-user-{indexName}:{value}` | User-defined index (field value) | `$idx-user-orders-by-country:Mauritius` |
| `$all`                          | All stream / log                 |                                         |


---

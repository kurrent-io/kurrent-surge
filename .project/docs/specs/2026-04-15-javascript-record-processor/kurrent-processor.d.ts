// =============================================================================
// Kurrent JavaScript Record Processor — Type Declarations
// =============================================================================
//
// These types define the contract between the Kurrent processor runtime and
// user-provided JavaScript processor scripts.
//
// To use: reference this file in your jsconfig.json or tsconfig.json, or add
// a triple-slash directive at the top of your processor script:
//
//   /// <reference path="kurrent-processor.d.ts" />
//
// Implement your processor by providing an object that satisfies the
// RecordProcessor interface. Only processRecord is required.
//
// =============================================================================

// =============================================================================
// Enums
// =============================================================================

/** Data format of the record schema. Maps to C# `SchemaDataFormat`. */
type SchemaDataFormat =
    | "unspecified"
    | "json"
    | "protobuf"
    | "avro"
    | "bytes";

/** What triggered a checkpoint. Maps to C# `CheckpointTrigger`. */
type CheckpointTrigger =
    | "unspecified"
    | "rebalance"
    | "interval"
    | "caught_up"
    | "savepoint"
    | "shutdown"
    | "reset";

// =============================================================================
// Record and related types
// =============================================================================

/** Schema information associated with a record. Maps to C# `RecordSchemaInfo`. */
interface RecordSchemaInfo {
    readonly schemaName: string;
    readonly schemaFormat: SchemaDataFormat;
    readonly schemaVersionId: string;
}

/** Index link associated with a record. Maps to C# `Link`. */
interface Link {
    readonly name: string;
    readonly position: number;
    readonly revision: number;
    readonly stream: string;
}

/** Stream-scoped position. Maps to C# `StreamPosition`. */
interface StreamPosition {
    readonly stream: string;
    readonly revision: number;
}

/** Full position of a record in the log and within its stream. Maps to C# `RecordPosition`. */
interface RecordPosition {
    readonly logPosition: number;
    readonly streamPosition: StreamPosition;
    readonly stream: string;
    readonly revision: number;
    readonly partition: number;
}

/** Key-value metadata collection. Maps to C# `Metadata`. */
interface Metadata {
    readonly [key: string]: unknown;
}

/**
 * A record from a KurrentDB stream.
 * Maps to C# `Kurrent.Client.Streams.Record`.
 */
interface Record {
    /** Unique record identifier. */
    readonly id: string;

    /** Position in the global log and within its stream. */
    readonly position: RecordPosition;

    /** Revision of the index associated with the record. */
    readonly indexRevision: number;

    /** Index link, if this record is a projection link. */
    readonly link: Link;

    /** When the record was created in the database. */
    readonly timestamp: Date;

    /** Key-value metadata. */
    readonly metadata: Metadata;

    /** Sequential order within a read operation. */
    readonly sequence: number;

    /** Raw serialized data bytes. */
    readonly data: Uint8Array;

    /** Schema information (name, format, version). */
    readonly schema: RecordSchemaInfo;

    /** Whether the record's data has been redacted. */
    readonly isRedacted: boolean;

    /** The deserialized value, if decoding succeeded. */
    readonly value: unknown;

    /** Type name of the deserialized value. */
    readonly valueType: string;

    /** Whether the record has been successfully decoded. */
    readonly isDecoded: boolean;

    /** Convenience: stream name from position. */
    readonly stream: string;

    /** Convenience: stream revision from position. */
    readonly revision: number;
}

// =============================================================================
// Progress and metrics
// =============================================================================

/** Throughput and timing metrics. Maps to C# `ThroughputMetrics`. */
interface ThroughputMetrics {
    readonly totalRecords: number;
    readonly recordsPerSecond: number;
    readonly startedAt: Date;
    readonly elapsed: number; // milliseconds
}

/** Checkpoint progress. Maps to C# `CheckpointProgress`. */
interface CheckpointProgress {
    readonly totalCheckpoints: number;
    readonly recordsSinceLastCheckpoint: number;
    readonly lastTrigger: CheckpointTrigger;
    readonly lastCheckpointAt: Date | null;
}

/** Per-partition position progress. Maps to C# `PartitionProgress`. */
interface PartitionProgress {
    readonly lastTrackedPosition: RecordPosition;
    readonly lastFlushedPosition: RecordPosition;
}

/** Combined progress for a processor session. Maps to C# `WorkerProgress`. */
interface WorkerProgress {
    readonly throughput: ThroughputMetrics;
    readonly checkpoint: CheckpointProgress;

    /** Per-partition progress. Keyed by partition number. */
    readonly partitions: ReadonlyMap<number, PartitionProgress>;

    /** Number of partitions currently assigned. */
    readonly partitionCount: number;
}

/** number-keyed record positions. Maps to C# `PartitionPositions`. */
interface PartitionPositions {
    readonly [partition: number]: RecordPosition;
}

// =============================================================================
// Logger
// =============================================================================

/**
 * Host-provided structured logger.
 * Maps to the `ILogger` from C# `WorkerContext.Logger`.
 */
interface Logger {
    debug(message: string, ...args: unknown[]): void;
    info(message: string, ...args: unknown[]): void;
    warn(message: string, ...args: unknown[]): void;
    error(message: string, ...args: unknown[]): void;
}

// =============================================================================
// Context types
// =============================================================================

/** Base context shared by all lifecycle hooks. Maps to C# `WorkerContext`. */
interface WorkerContext {
    readonly processorId: string;
    readonly workerId: string;
    readonly sessionId: string;
    readonly logger: Logger;
}

/**
 * Context for `processRecord`.
 * Maps to C# `ProcessRecordContext`.
 */
interface ProcessRecordContext extends WorkerContext {
    /** The record to process. */
    readonly record: Record;

    /** Current progress snapshot. */
    readonly progress: WorkerProgress;

    /** Signal immediate stop (throws, does not return). */
    stopProcessing(): never;

    /** Signal stop before the record is tracked. */
    stopBeforeTrack(reason?: string): void;

    /** Signal stop after the record is tracked. */
    stopAfterTrack(reason?: string): void;
}

/**
 * Context for `onCheckpoint`.
 * Maps to C# `CheckpointContext`.
 */
interface CheckpointContext extends WorkerContext {
    /** Per-partition positions being checkpointed. */
    readonly positions: PartitionPositions;

    /** Unique checkpoint identifier. */
    readonly checkpointId: string;

    /** What triggered this checkpoint. */
    readonly trigger: CheckpointTrigger;

    /** When the checkpoint was triggered. */
    readonly timestamp: Date;
}

/**
 * Context for `onError`.
 * Maps to C# `ErrorContext`.
 */
interface ErrorContext extends WorkerContext {
    /** Progress snapshot at time of error. */
    readonly progress: WorkerProgress;

    /** The error that occurred. */
    readonly error: Error;
}

/**
 * Context for `onComplete`.
 * Maps to C# `CompleteContext`.
 */
interface CompleteContext extends WorkerContext {
    /** Final progress snapshot. */
    readonly progress: WorkerProgress;
}

// =============================================================================
// Processor contract
// =============================================================================

/**
 * The processor interface. Maps directly to C# `IRecordProcessor`.
 *
 * Only `processRecord` is required — all other methods have default no-op
 * behavior, matching the C# interface default implementations.
 */
interface RecordProcessor {
    /**
     * Called when a record is available for processing.
     * This is the only method that must be implemented.
     */
    processRecord(context: ProcessRecordContext): void | Promise<void>;

    /**
     * Called when a checkpoint is signaled to allow for pre-checkpoint
     * actions like flushing state.
     */
    onCheckpoint?(context: CheckpointContext): void | Promise<void>;

    /**
     * Called when the worker encounters an unrecoverable error.
     * Invoked before the worker transitions to error status.
     * Use for error reporting, metrics, or cleanup.
     */
    onError?(context: ErrorContext): void | Promise<void>;

    /**
     * Called when the worker completes gracefully (including cancellation).
     * Invoked before the worker transitions to stopped status.
     * Use for final reporting or cleanup.
     */
    onComplete?(context: CompleteContext): void | Promise<void>;
}

// =============================================================================
// Host capabilities (DRAFT — API not finalized)
// =============================================================================

// TODO: emit API — append records to streams from JS
// TODO: extensible host services — user-registered .NET services exposed to JS

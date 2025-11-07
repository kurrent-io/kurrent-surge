// using Kurrent.Client.Registry;
//
// namespace Kurrent.Client.Schema;
//
//
//
//
// public static class MessageTypeMapperErrors {
//     public sealed record MessageTypeAlreadyMapped : ResultError {
//         public MessageTypeAlreadyMapped(string schemaName, Type attemptedMessageType, Type registeredMessageType) {
//             SchemaName            = schemaName;
//             AttemptedMessageType  = attemptedMessageType;
//             RegisteredMessageType = registeredMessageType;
//
//             ErrorCode    = "MessageTypeAlreadyMapped";
//             ErrorMessage = $"Message '{AttemptedMessageType.Name}' is already mapped with the name '{SchemaName}' as '{RegisteredMessageType.FullName}'";
//         }
//
//         string SchemaName            { get; }
//         Type   AttemptedMessageType  { get; }
//         Type   RegisteredMessageType { get; }
//     }
//
//     public sealed record SchemaNameMapNotFound : ResultError {
//         public SchemaNameMapNotFound(string schemaName) {
//             SchemaName = schemaName;
//
//             ErrorCode    = "SchemaNameMapNotFound";
//             ErrorMessage = $"Schema '{SchemaName}' not mapped";
//         }
//
//         public string SchemaName { get; }
//     }
//
//     public sealed record MessageTypeMapNotFound : ResultError {
//         public MessageTypeMapNotFound(Type messageType) {
//             MessageType = messageType;
//
//             ErrorCode    = "MessageTypeMapNotFound";
//             ErrorMessage = $"Message '{MessageType.Name}' not mapped";
//         }
//
//         public Type MessageType { get; }
//     }
//
//     public sealed record MessageTypeConflict : ResultError {
//         public MessageTypeConflict(SchemaName schemaName, Type mappedType, Type attemptedType) {
//             SchemaName    = schemaName;
//             MappedType    = mappedType;
//             AttemptedType = attemptedType;
//
//             ErrorCode    = "MessageTypeConflict";
//             ErrorMessage = $"Schema '{SchemaName}' is already mapped with type '{MappedType.FullName}' "
//                          + $"but attempted to use with incompatible type '{AttemptedType.FullName}'";
//         }
//
//         public SchemaName SchemaName    { get; }
//         public Type       MappedType    { get; }
//         public Type       AttemptedType { get; }
//     }
//
//     public sealed record MessageTypeResolution : ResultError {
//         public MessageTypeResolution(SchemaName schemaName) {
//             SchemaName = schemaName;
//
//             ErrorCode    = "MessageTypeResolution";
//             ErrorMessage = $"Schema '{SchemaName}' not mapped and does not match any known type";
//         }
//
//         public SchemaName SchemaName { get; }
//     }
// }

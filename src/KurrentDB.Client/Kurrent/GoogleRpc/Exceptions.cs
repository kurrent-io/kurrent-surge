// ReSharper disable CheckNamespace

using Kurrent.Client;

namespace Google.Rpc.Exceptions;

[KurrentOperationError(Google.Rpc.Code.Cancelled)]
public partial class CancelledException;

[KurrentOperationError(Google.Rpc.Code.Unknown)]
public partial class UnknownException;

[KurrentOperationError(Google.Rpc.Code.InvalidArgument)]
public partial class InvalidArgumentException;

[KurrentOperationError(Google.Rpc.Code.DeadlineExceeded)]
public partial class DeadlineExceededException;

[KurrentOperationError(Google.Rpc.Code.NotFound)]
public partial class NotFoundException;

[KurrentOperationError(Google.Rpc.Code.AlreadyExists)]
public partial class AlreadyExistsException;

[KurrentOperationError(Google.Rpc.Code.PermissionDenied)]
public partial class PermissionDeniedException;

[KurrentOperationError(Google.Rpc.Code.Unauthenticated)]
public partial class UnauthenticatedException;

[KurrentOperationError(Google.Rpc.Code.ResourceExhausted)]
public partial class ResourceExhaustedException;

[KurrentOperationError(Google.Rpc.Code.FailedPrecondition)]
public partial class FailedPreconditionException;

[KurrentOperationError(Google.Rpc.Code.Aborted)]
public partial class AbortedException;

[KurrentOperationError(Google.Rpc.Code.OutOfRange)]
public partial class OutOfRangeException;

[KurrentOperationError(Google.Rpc.Code.Unimplemented)]
public partial class UnimplementedException;

[KurrentOperationError(Google.Rpc.Code.Internal)]
public partial class InternalException;

[KurrentOperationError(Google.Rpc.Code.Unavailable)]
public partial class UnavailableException;

[KurrentOperationError(Google.Rpc.Code.DataLoss)]
public partial class DataLossException;

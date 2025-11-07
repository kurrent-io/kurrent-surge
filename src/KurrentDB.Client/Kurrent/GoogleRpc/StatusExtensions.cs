namespace Kurrent.GoogleRpc;

public static class StatusExtensions {
    extension(Google.Rpc.Status status) {
        public Google.Rpc.Code GetGrpcStatusCode() => (Google.Rpc.Code)status.Code;
    }
}

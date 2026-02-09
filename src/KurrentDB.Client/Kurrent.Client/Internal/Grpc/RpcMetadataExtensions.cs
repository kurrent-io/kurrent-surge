using Grpc.Core;

namespace Kurrent.Client;

static class RpcMetadataExtensions {
    extension(RpcException rex) {
        public RpcException WithTrailer(string key, string value) {
            rex.Trailers.Add(key, value);
            return rex;
        }

        public Metadata TrailingMetadata() =>
            Metadata.Create(rex.Trailers.ToDictionary(x => x.Key, static object? (kvp) => kvp.Value));
    }
}

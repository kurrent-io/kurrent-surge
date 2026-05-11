namespace Kurrent.Testkit.OpenTelemetry;

[PublicAPI]
public static class OtelTestContextExtensions {
    extension(TestContext? context) {
        public void ConfigureOtel(OtelServiceMetadata metadata) {
            ArgumentNullException.ThrowIfNull(context);
            context?.StateBag.Items["OTEL_RESOURCE_ATTRIBUTES"] = metadata.GetResourceAttributes();
            context?.StateBag.Items["OTEL_SERVICE_NAME"]        = metadata.ServiceName; // not really necessary, but follows the convention
        }

        public OtelServiceMetadata GetOtelServiceMetadata() =>
            context is not null && context.StateBag.Items.TryGetValue("OTEL_RESOURCE_ATTRIBUTES", out var value) && value is string resourceAttributes
                ? OtelServiceMetadata.Parse(resourceAttributes)
                : OtelServiceMetadata.None;

        public void SetOtelServiceName(string serviceName) => ConfigureOtel(context, new OtelServiceMetadata(serviceName));
    }
}

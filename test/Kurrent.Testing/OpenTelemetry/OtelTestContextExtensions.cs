namespace Kurrent.Testing.OpenTelemetry;

public static class OtelTestContextExtensions {
    public static void ConfigureOtel(this TestContext context, OtelServiceMetadata metadata) {
        context.StateBag.Items["OTEL_RESOURCE_ATTRIBUTES"] = metadata.GetResourceAttributes();
        context.StateBag.Items["OTEL_SERVICE_NAME"]        = metadata.ServiceName; // not really necessary, but follows the convention
    }

    public static OtelServiceMetadata GetOtelServiceMetadata(this TestContext? context) =>
        context is not null && context.StateBag.Items.TryGetValue("OTEL_RESOURCE_ATTRIBUTES", out var value) && value is string resourceAttributes
            ? OtelServiceMetadata.Parse(resourceAttributes)
            : OtelServiceMetadata.None;

    public static void SetOtelServiceName(this TestContext context, string serviceName) => ConfigureOtel(context, new OtelServiceMetadata(serviceName));
}

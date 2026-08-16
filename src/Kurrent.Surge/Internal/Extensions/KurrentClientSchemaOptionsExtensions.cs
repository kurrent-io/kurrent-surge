namespace Kurrent.Surge.Client;

static class KurrentClientSchemaOptionsExtensions {
	extension(KurrentClientSchemaOptions options) {
		public bool SchemaRegistryRequired =>
			options.RegistrationMode is SchemaRegistrationMode.Auto
			|| options.ValidationMode is SchemaValidationMode.Enabled;
	}
}

namespace Kurrent.Surge.Client.Schema.Governance;

sealed class CatalogRegistrationTask(KurrentClient client) : IClientInitializationTask {
	public ValueTask Execute(IKurrentClient _, CancellationToken ct) {
		client.Governance.StartCatalogRegistration();
		return ValueTask.CompletedTask;
	}
}

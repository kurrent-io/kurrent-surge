// ReSharper disable CheckNamespace

namespace Company.Logistics.Events {
	public record OrderShipped {
		public record Details(string ItemId, int Quantity);
		public record Address(string Street, string City, string ZipCode);
	}
}

/// <summary>
/// A record without a namespace, simulating types defined in top-level statements.
/// </summary>
public record GlobalItemAdded(string Sku, int Quantity);

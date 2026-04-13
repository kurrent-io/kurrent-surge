namespace Registry.OrderManagement.Schemas;

static class SeedSchemas {
    public const string Domain = "orders";
    public const string Team   = "orders-team";

    // v1 — core order fields, matches OrderPlaced CLR type
    public const string OrderPlacedV1 = """
        {
          "type": "object",
          "required": ["orderId", "customerId", "items", "total", "placedAt"],
          "properties": {
            "orderId":    { "type": "string" },
            "customerId": { "type": "string" },
            "items": {
              "type": "array",
              "items": {
                "type": "object",
                "required": ["sku", "quantity", "unitPrice"],
                "properties": {
                  "sku":       { "type": "string" },
                  "quantity":  { "type": "integer", "minimum": 1 },
                  "unitPrice": { "type": "number", "exclusiveMinimum": 0 }
                }
              },
              "minItems": 1
            },
            "total":    { "type": "number", "exclusiveMinimum": 0 },
            "placedAt": { "type": "string", "format": "date-time" }
          }
        }
        """;

    // v2 — added optional shippingAddress and discountCode
    public const string OrderPlacedV2 = """
        {
          "type": "object",
          "required": ["orderId", "customerId", "items", "total", "placedAt"],
          "properties": {
            "orderId":    { "type": "string" },
            "customerId": { "type": "string" },
            "items": {
              "type": "array",
              "items": {
                "type": "object",
                "required": ["sku", "quantity", "unitPrice"],
                "properties": {
                  "sku":       { "type": "string" },
                  "quantity":  { "type": "integer", "minimum": 1 },
                  "unitPrice": { "type": "number", "exclusiveMinimum": 0 }
                }
              },
              "minItems": 1
            },
            "total":           { "type": "number", "exclusiveMinimum": 0 },
            "placedAt":        { "type": "string", "format": "date-time" },
            "shippingAddress": { "type": "string" },
            "discountCode":    { "type": "string" }
          }
        }
        """;

    // Breaking: renames customerId → buyerId (removes a required field)
    public const string OrderPlacedBreaking = """
        {
          "type": "object",
          "required": ["orderId", "buyerId", "items", "total", "placedAt"],
          "properties": {
            "orderId": { "type": "string" },
            "buyerId": { "type": "string" },
            "items": {
              "type": "array",
              "items": {
                "type": "object",
                "required": ["sku", "quantity", "unitPrice"],
                "properties": {
                  "sku":       { "type": "string" },
                  "quantity":  { "type": "integer", "minimum": 1 },
                  "unitPrice": { "type": "number", "exclusiveMinimum": 0 }
                }
              },
              "minItems": 1
            },
            "total":    { "type": "number", "exclusiveMinimum": 0 },
            "placedAt": { "type": "string", "format": "date-time" }
          }
        }
        """;

    public const string OrderShippedV1 = """
        {
          "type": "object",
          "required": ["orderId", "carrier", "trackingNumber", "shippedAt"],
          "properties": {
            "orderId":        { "type": "string" },
            "carrier":        { "type": "string" },
            "trackingNumber": { "type": "string" },
            "shippedAt":      { "type": "string", "format": "date-time" }
          }
        }
        """;
}

using Apache.Arrow.Adbc.Drivers.FlightSql;

namespace Apache.Arrow.Adbc.Client;

public static class FlightSqlDriverExtensions {
    extension(FlightSqlDriver driver) {
        public AdbcConnection CreateConnection(
            string serverAddress,
            Action<Dictionary<string, string>>? configureParameters = null,
            Action<Dictionary<string, string>>? configureOptions = null) {

            ArgumentException.ThrowIfNullOrWhiteSpace(serverAddress);

            // The two dictionaries carry different things: parameters reach the driver as gRPC call
            // metadata and are sent verbatim as headers on every call, while options are read by
            // FlightSqlDatabase.Connect, which looks only for ServerAddress. The address goes in
            // both anyway because AdbcConnection.Open() rejects an empty parameters dictionary
            // ("No connection values are present to connect with"), so it doubles as the guard
            // value and is echoed as a harmless FLIGHT_SQL_SERVER_ADDRESS header.
            var parameters = new Dictionary<string, string> { [FlightSqlParameters.ServerAddress] = serverAddress };
            var options    = new Dictionary<string, string> { [FlightSqlParameters.ServerAddress] = serverAddress };

            configureParameters?.Invoke(parameters);
            configureOptions?.Invoke(options);
            
            return new(driver, parameters, options);
        } 
    }
}
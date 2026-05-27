using Apache.Arrow.Adbc.Drivers.FlightSql;

namespace Apache.Arrow.Adbc.Client;

public static class FlightSqlDriverExtensions {
    extension(FlightSqlDriver driver) {
        public AdbcConnection CreateConnection(
            string connectionString, 
            Action<Dictionary<string, string>>? configureParameters = null, 
            Action<Dictionary<string, string>>? configureOptions = null) {
            
            ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
            
            var parameters = new Dictionary<string, string> { [FlightSqlParameters.ServerAddress] = connectionString };
            var options    = new Dictionary<string, string> { [FlightSqlParameters.ServerAddress] = connectionString };
            
            configureParameters?.Invoke(parameters);
            configureOptions?.Invoke(options);
            
            return new(driver, parameters, options);
        } 
    }
}
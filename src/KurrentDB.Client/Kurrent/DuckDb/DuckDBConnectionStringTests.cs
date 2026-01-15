// namespace Kurrent.DuckDB.Tests;
//
// public class DuckDBConnectionStringTests
// {
//     // ─────────────────────────────────────────────────────────────────────────
//     // Basic parsing
//     // ─────────────────────────────────────────────────────────────────────────
//
//     [Fact]
//     public void Parse_SimpleDataSource_ShouldSucceed()
//     {
//         var result = DuckDBConnectionString.Parse("DataSource=mydb.duckdb");
//
//         Assert.Equal("mydb.duckdb", result.DataSource);
//         Assert.True(result.IsFile);
//         Assert.False(result.IsInMemory);
//         Assert.False(result.IsMotherDuck);
//     }
//
//     [Fact]
//     public void Parse_DataSourceWithSpace_ShouldSucceed()
//     {
//         var result = DuckDBConnectionString.Parse("Data Source=mydb.duckdb");
//
//         Assert.Equal("mydb.duckdb", result.DataSource);
//     }
//
//     [Fact]
//     public void Parse_CaseInsensitive_ShouldSucceed()
//     {
//         var result = DuckDBConnectionString.Parse("DATASOURCE=mydb.duckdb");
//
//         Assert.Equal("mydb.duckdb", result.DataSource);
//     }
//
//     // ─────────────────────────────────────────────────────────────────────────
//     // In-memory databases
//     // ─────────────────────────────────────────────────────────────────────────
//
//     [Fact]
//     public void Parse_InMemory_ShouldSetFlag()
//     {
//         var result = DuckDBConnectionString.Parse("DataSource=:memory:");
//
//         Assert.True(result.IsInMemory);
//         Assert.False(result.IsSharedInMemory);
//         Assert.False(result.IsFile);
//         Assert.False(result.IsMotherDuck);
//     }
//
//     [Fact]
//     public void Parse_SharedInMemory_ShouldSetBothFlags()
//     {
//         var result = DuckDBConnectionString.Parse("DataSource=:memory:?cache=shared");
//
//         Assert.True(result.IsInMemory);
//         Assert.True(result.IsSharedInMemory);
//     }
//
//     // ─────────────────────────────────────────────────────────────────────────
//     // MotherDuck
//     // ─────────────────────────────────────────────────────────────────────────
//
//     [Fact]
//     public void Parse_MotherDuck_ShouldSetFlag()
//     {
//         var result = DuckDBConnectionString.Parse("DataSource=md:my_database");
//
//         Assert.True(result.IsMotherDuck);
//         Assert.False(result.IsInMemory);
//         Assert.False(result.IsFile);
//         Assert.Equal("my_database", result.MotherDuckDatabase);
//     }
//
//     [Fact]
//     public void Parse_MotherDuckWithToken_ShouldExtractDatabaseName()
//     {
//         var result = DuckDBConnectionString.Parse(
//             "DataSource=md:my_database?motherduck_token=eyJ123"
//         );
//
//         Assert.True(result.IsMotherDuck);
//         Assert.Equal("my_database", result.MotherDuckDatabase);
//     }
//
//     [Fact]
//     public void Parse_MotherDuckWithMultipleParams_ShouldExtractDatabaseName()
//     {
//         var result = DuckDBConnectionString.Parse(
//             "DataSource=md:analytics?motherduck_token=token123&saas_mode=true"
//         );
//
//         Assert.True(result.IsMotherDuck);
//         Assert.Equal("analytics", result.MotherDuckDatabase);
//     }
//
//     [Fact]
//     public void Parse_NotMotherDuck_ShouldReturnNullDatabase()
//     {
//         var result = DuckDBConnectionString.Parse("DataSource=:memory:");
//
//         Assert.Null(result.MotherDuckDatabase);
//     }
//
//     // ─────────────────────────────────────────────────────────────────────────
//     // Access mode
//     // ─────────────────────────────────────────────────────────────────────────
//
//     [Theory]
//     [InlineData("READ_ONLY", DuckDBAccessMode.ReadOnly)]
//     [InlineData("READONLY", DuckDBAccessMode.ReadOnly)]
//     [InlineData("READ_WRITE", DuckDBAccessMode.ReadWrite)]
//     [InlineData("READWRITE", DuckDBAccessMode.ReadWrite)]
//     [InlineData("AUTOMATIC", DuckDBAccessMode.Automatic)]
//     [InlineData("AUTO", DuckDBAccessMode.Automatic)]
//     public void Parse_AccessMode_ShouldParseCorrectly(string value, DuckDBAccessMode expected)
//     {
//         var result = DuckDBConnectionString.Parse($"DataSource=test.db;access_mode={value}");
//
//         Assert.Equal(expected, result.AccessMode);
//     }
//
//     [Fact]
//     public void Parse_ReadOnly_ShouldSetIsReadOnlyProperty()
//     {
//         var result = DuckDBConnectionString.Parse("DataSource=test.db;access_mode=READ_ONLY");
//
//         Assert.True(result.IsReadOnly);
//     }
//
//     // ─────────────────────────────────────────────────────────────────────────
//     // Typed properties
//     // ─────────────────────────────────────────────────────────────────────────
//
//     [Fact]
//     public void Parse_Threads_ShouldParseAsInt()
//     {
//         var result = DuckDBConnectionString.Parse("DataSource=:memory:;threads=8");
//
//         Assert.Equal(8, result.Threads);
//     }
//
//     [Fact]
//     public void Parse_MemoryLimit_ShouldParseAsString()
//     {
//         var result = DuckDBConnectionString.Parse("DataSource=:memory:;memory_limit=10GB");
//
//         Assert.Equal("10GB", result.MemoryLimit);
//     }
//
//     // ─────────────────────────────────────────────────────────────────────────
//     // Key aliases
//     // ─────────────────────────────────────────────────────────────────────────
//
//     [Fact]
//     public void Parse_WorkerThreadsAlias_ShouldNormalize()
//     {
//         var result = DuckDBConnectionString.Parse("DataSource=:memory:;worker_threads=4");
//
//         Assert.Equal(4, result.Threads);
//     }
//
//     [Fact]
//     public void Parse_MaxMemoryAlias_ShouldNormalize()
//     {
//         var result = DuckDBConnectionString.Parse("DataSource=:memory:;max_memory=8GB");
//
//         Assert.Equal("8GB", result.MemoryLimit);
//     }
//
//     // ─────────────────────────────────────────────────────────────────────────
//     // Logging
//     // ─────────────────────────────────────────────────────────────────────────
//
//     [Theory]
//     [InlineData("true", true)]
//     [InlineData("1", true)]
//     [InlineData("false", false)]
//     [InlineData("0", false)]
//     public void Parse_EnableLogging_ShouldParseCorrectly(string value, bool expected)
//     {
//         var result = DuckDBConnectionString.Parse($"DataSource=:memory:;enable_logging={value}");
//
//         Assert.Equal(expected, result.EnableLogging);
//     }
//
//     [Theory]
//     [InlineData("ERROR", DuckDBLoggingLevel.Error)]
//     [InlineData("WARN", DuckDBLoggingLevel.Warn)]
//     [InlineData("INFO", DuckDBLoggingLevel.Info)]
//     [InlineData("DEBUG", DuckDBLoggingLevel.Debug)]
//     [InlineData("TRACE", DuckDBLoggingLevel.Trace)]
//     [InlineData("error", DuckDBLoggingLevel.Error)]
//     [InlineData("debug", DuckDBLoggingLevel.Debug)]
//     public void Parse_LoggingLevel_ShouldParseCorrectly(string value, DuckDBLoggingLevel expected)
//     {
//         var result = DuckDBConnectionString.Parse($"DataSource=:memory:;logging_level={value}");
//
//         Assert.Equal(expected, result.LoggingLevel);
//     }
//
//     // ─────────────────────────────────────────────────────────────────────────
//     // Unknown keys (non-strict mode)
//     // ─────────────────────────────────────────────────────────────────────────
//
//     [Fact]
//     public void Parse_UnknownKey_NonStrict_ShouldSucceed()
//     {
//         var result = DuckDBConnectionString.Parse(
//             "DataSource=:memory:;some_future_option=value",
//             strict: false
//         );
//
//         Assert.True(result.Configuration.ContainsKey("some_future_option"));
//         Assert.Equal("value", result.Configuration["some_future_option"]);
//     }
//
//     // ─────────────────────────────────────────────────────────────────────────
//     // Error cases
//     // ─────────────────────────────────────────────────────────────────────────
//
//     [Fact]
//     public void Parse_NullString_ShouldThrow()
//     {
//         Assert.Throws<ArgumentNullException>(() => DuckDBConnectionString.Parse(null!));
//     }
//
//     [Fact]
//     public void Parse_EmptyString_ShouldThrow()
//     {
//         Assert.Throws<ArgumentException>(() => DuckDBConnectionString.Parse(""));
//     }
//
//     [Fact]
//     public void Parse_MissingDataSource_ShouldThrow()
//     {
//         var ex = Assert.Throws<ArgumentException>(() =>
//             DuckDBConnectionString.Parse("threads=4;memory_limit=10GB"));
//
//         Assert.Contains("DataSource", ex.Message);
//     }
//
//     // ─────────────────────────────────────────────────────────────────────────
//     // TryParse
//     // ─────────────────────────────────────────────────────────────────────────
//
//     [Fact]
//     public void TryParse_ValidString_ShouldReturnTrue()
//     {
//         var success = DuckDBConnectionString.TryParse("DataSource=:memory:", out var result);
//
//         Assert.True(success);
//         Assert.True(result.IsInMemory);
//     }
//
//     [Fact]
//     public void TryParse_InvalidString_ShouldReturnFalse()
//     {
//         var success = DuckDBConnectionString.TryParse("invalid", strict: false, out _, out var error);
//
//         Assert.False(success);
//         Assert.NotNull(error);
//     }
//
//     // ─────────────────────────────────────────────────────────────────────────
//     // Conversion
//     // ─────────────────────────────────────────────────────────────────────────
//
//     [Fact]
//     public void ImplicitConversion_ShouldReturnValue()
//     {
//         var result = DuckDBConnectionString.Parse("DataSource=test.db");
//
//         string str = result;
//
//         Assert.Equal("DataSource=test.db", str);
//     }
//
//     // ─────────────────────────────────────────────────────────────────────────
//     // Builder property
//     // ─────────────────────────────────────────────────────────────────────────
//
//     [Fact]
//     public void Builder_ShouldReturnNewBuilder()
//     {
//         var builder1 = DuckDBConnectionString.Builder;
//         var builder2 = DuckDBConnectionString.Builder;
//
//         Assert.NotSame(builder1, builder2);
//     }
//
//     [Fact]
//     public void Builder_ShouldDefaultToInMemory()
//     {
//         var result = DuckDBConnectionString.Builder.Build();
//
//         Assert.True(result.IsInMemory);
//     }
// }
//
// public class DuckDBConnectionStringBuilderTests
// {
//     // ─────────────────────────────────────────────────────────────────────────
//     // Factory methods
//     // ─────────────────────────────────────────────────────────────────────────
//
//     [Fact]
//     public void ForDatabase_ShouldSetDataSource()
//     {
//         var result = DuckDBConnectionStringBuilder
//             .ForDatabase("mydb.duckdb")
//             .Build();
//
//         Assert.Equal("mydb.duckdb", result.DataSource);
//         Assert.True(result.IsFile);
//     }
//
//     [Fact]
//     public void ForInMemory_ShouldSetInMemoryDataSource()
//     {
//         var result = DuckDBConnectionStringBuilder
//             .ForInMemory()
//             .Build();
//
//         Assert.True(result.IsInMemory);
//         Assert.False(result.IsSharedInMemory);
//     }
//
//     [Fact]
//     public void ForInMemory_Shared_ShouldSetSharedDataSource()
//     {
//         var result = DuckDBConnectionStringBuilder
//             .ForInMemory(shared: true)
//             .Build();
//
//         Assert.True(result.IsInMemory);
//         Assert.True(result.IsSharedInMemory);
//     }
//
//     // ─────────────────────────────────────────────────────────────────────────
//     // MotherDuck
//     // ─────────────────────────────────────────────────────────────────────────
//
//     [Fact]
//     public void ForMotherDuck_ShouldSetMotherDuckDataSource()
//     {
//         var result = DuckDBConnectionStringBuilder
//             .ForMotherDuck("my_database")
//             .Build();
//
//         Assert.True(result.IsMotherDuck);
//         Assert.Equal("my_database", result.MotherDuckDatabase);
//     }
//
//     [Fact]
//     public void ForMotherDuck_WithToken_ShouldIncludeToken()
//     {
//         var result = DuckDBConnectionStringBuilder
//             .ForMotherDuck("my_database", token: "eyJ123")
//             .Build();
//
//         Assert.True(result.IsMotherDuck);
//         Assert.Equal("my_database", result.MotherDuckDatabase);
//         Assert.Contains("motherduck_token=eyJ123", result.DataSource);
//     }
//
//     [Fact]
//     public void ForDataSource_ShouldSetCustomDataSource()
//     {
//         var result = DuckDBConnectionStringBuilder
//             .ForDataSource("custom://path/to/db")
//             .Build();
//
//         Assert.Equal("custom://path/to/db", result.DataSource);
//     }
//
//     // ─────────────────────────────────────────────────────────────────────────
//     // Access mode
//     // ─────────────────────────────────────────────────────────────────────────
//
//     [Fact]
//     public void WithAccessMode_ReadOnly_ShouldSet()
//     {
//         var result = DuckDBConnectionStringBuilder
//             .ForDatabase("test.db")
//             .WithAccessMode(DuckDBAccessMode.ReadOnly)
//             .Build();
//
//         Assert.Equal(DuckDBAccessMode.ReadOnly, result.AccessMode);
//         Assert.True(result.IsReadOnly);
//     }
//
//     [Fact]
//     public void AsReadOnly_ShouldSetReadOnlyMode()
//     {
//         var result = DuckDBConnectionStringBuilder
//             .ForDatabase("test.db")
//             .AsReadOnly()
//             .Build();
//
//         Assert.True(result.IsReadOnly);
//     }
//
//     // ─────────────────────────────────────────────────────────────────────────
//     // Threads and memory
//     // ─────────────────────────────────────────────────────────────────────────
//
//     [Fact]
//     public void WithThreads_ShouldSetThreadCount()
//     {
//         var result = DuckDBConnectionStringBuilder
//             .ForInMemory()
//             .WithThreads(16)
//             .Build();
//
//         Assert.Equal(16, result.Threads);
//     }
//
//     [Fact]
//     public void WithMemoryLimit_ShouldSetLimit()
//     {
//         var result = DuckDBConnectionStringBuilder
//             .ForInMemory()
//             .WithMemoryLimit("32GB")
//             .Build();
//
//         Assert.Equal("32GB", result.MemoryLimit);
//     }
//
//     // ─────────────────────────────────────────────────────────────────────────
//     // Logging
//     // ─────────────────────────────────────────────────────────────────────────
//
//     [Fact]
//     public void WithLogging_Enabled_ShouldSetFlagAndLevel()
//     {
//         var result = DuckDBConnectionStringBuilder
//             .ForInMemory()
//             .WithLogging(enabled: true, level: DuckDBLoggingLevel.Debug)
//             .Build();
//
//         Assert.True(result.EnableLogging);
//         Assert.Equal(DuckDBLoggingLevel.Debug, result.LoggingLevel);
//     }
//
//     [Fact]
//     public void WithLogging_Disabled_ShouldSetFlagOnly()
//     {
//         var result = DuckDBConnectionStringBuilder
//             .ForInMemory()
//             .WithLogging(enabled: false)
//             .Build();
//
//         Assert.False(result.EnableLogging);
//     }
//
//     [Fact]
//     public void WithLogging_DefaultLevel_ShouldUseInfo()
//     {
//         var result = DuckDBConnectionStringBuilder
//             .ForInMemory()
//             .WithLogging(enabled: true)
//             .Build();
//
//         Assert.True(result.EnableLogging);
//         Assert.Equal(DuckDBLoggingLevel.Info, result.LoggingLevel);
//     }
//
//     // ─────────────────────────────────────────────────────────────────────────
//     // Generic configuration
//     // ─────────────────────────────────────────────────────────────────────────
//
//     [Fact]
//     public void WithConfiguration_ShouldAddToConfiguration()
//     {
//         var result = DuckDBConnectionStringBuilder
//             .ForInMemory()
//             .WithConfiguration("temp_directory", "/tmp/duck")
//             .Build();
//
//         Assert.True(result.Configuration.ContainsKey("temp_directory"));
//         Assert.Equal("/tmp/duck", result.Configuration["temp_directory"]);
//     }
//
//     [Fact]
//     public void WithoutConfiguration_ShouldRemove()
//     {
//         var result = DuckDBConnectionStringBuilder
//             .ForInMemory()
//             .WithThreads(8)
//             .WithoutConfiguration("threads")
//             .Build();
//
//         Assert.Null(result.Threads);
//     }
//
//     // ─────────────────────────────────────────────────────────────────────────
//     // Fluent chaining
//     // ─────────────────────────────────────────────────────────────────────────
//
//     [Fact]
//     public void FluentChaining_ShouldWork()
//     {
//         var result = DuckDBConnectionStringBuilder
//             .ForInMemory()
//             .WithThreads(8)
//             .WithMemoryLimit("16GB")
//             .AsReadOnly()
//             .Build();
//
//         Assert.True(result.IsInMemory);
//         Assert.Equal(8, result.Threads);
//         Assert.Equal("16GB", result.MemoryLimit);
//         Assert.True(result.IsReadOnly);
//     }
//
//     // ─────────────────────────────────────────────────────────────────────────
//     // Round-trip
//     // ─────────────────────────────────────────────────────────────────────────
//
//     [Fact]
//     public void Build_ShouldBeReparseable()
//     {
//         var original = DuckDBConnectionStringBuilder
//             .ForDatabase("test.db")
//             .WithThreads(8)
//             .WithMemoryLimit("16GB")
//             .AsReadOnly()
//             .Build();
//
//         var reparsed = DuckDBConnectionString.Parse(original.Value);
//
//         Assert.Equal(original.DataSource, reparsed.DataSource);
//         Assert.Equal(original.Threads, reparsed.Threads);
//         Assert.Equal(original.MemoryLimit, reparsed.MemoryLimit);
//         Assert.Equal(original.AccessMode, reparsed.AccessMode);
//     }
//
//     // ─────────────────────────────────────────────────────────────────────────
//     // Implicit conversion
//     // ─────────────────────────────────────────────────────────────────────────
//
//     [Fact]
//     public void ImplicitConversion_ShouldBuildAutomatically()
//     {
//         DuckDBConnectionString result = DuckDBConnectionStringBuilder
//             .ForInMemory()
//             .WithThreads(4);
//
//         Assert.True(result.IsInMemory);
//         Assert.Equal(4, result.Threads);
//     }
//
//     // ─────────────────────────────────────────────────────────────────────────
//     // Using Builder property
//     // ─────────────────────────────────────────────────────────────────────────
//
//     [Fact]
//     public void BuilderProperty_ShouldWorkWithFluent()
//     {
//         var result = DuckDBConnectionString.Builder
//             .WithThreads(8)
//             .WithMemoryLimit("16GB")
//             .Build();
//
//         Assert.True(result.IsInMemory); // Default
//         Assert.Equal(8, result.Threads);
//         Assert.Equal("16GB", result.MemoryLimit);
//     }
//
//     // ─────────────────────────────────────────────────────────────────────────
//     // Real-world scenarios
//     // ─────────────────────────────────────────────────────────────────────────
//
//     [Fact]
//     public void ProductionAnalytics_ShouldBuildCorrectly()
//     {
//         var result = DuckDBConnectionStringBuilder
//             .ForDatabase("/data/analytics.duckdb")
//             .AsReadOnly()
//             .WithThreads(16)
//             .WithMemoryLimit("64GB")
//             .WithConfiguration("temp_directory", "/mnt/fast-ssd/tmp")
//             .Build();
//
//         Assert.Equal("/data/analytics.duckdb", result.DataSource);
//         Assert.True(result.IsReadOnly);
//         Assert.Equal(16, result.Threads);
//         Assert.Equal("64GB", result.MemoryLimit);
//         Assert.Equal("/mnt/fast-ssd/tmp", result.Configuration["temp_directory"]);
//     }
//
//     [Fact]
//     public void MotherDuckProduction_ShouldBuildCorrectly()
//     {
//         var result = DuckDBConnectionStringBuilder
//             .ForMotherDuck("prod_analytics", token: "eyJ123456")
//             .WithThreads(8)
//             .Build();
//
//         Assert.True(result.IsMotherDuck);
//         Assert.Equal("prod_analytics", result.MotherDuckDatabase);
//         Assert.Equal(8, result.Threads);
//     }
//
//     [Fact]
//     public void DebugSession_ShouldEnableLogging()
//     {
//         var result = DuckDBConnectionStringBuilder
//             .ForInMemory()
//             .WithLogging(enabled: true, level: DuckDBLoggingLevel.Debug)
//             .WithThreads(4)
//             .Build();
//
//         Assert.True(result.IsInMemory);
//         Assert.True(result.EnableLogging);
//         Assert.Equal(DuckDBLoggingLevel.Debug, result.LoggingLevel);
//         Assert.Equal(4, result.Threads);
//     }
// }

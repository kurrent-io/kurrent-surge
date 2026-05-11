var builder = DistributedApplication.CreateBuilder(args);
builder.AddKurrentDB();
builder.Build().Run();

public partial class Program;
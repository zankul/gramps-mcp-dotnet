using GrampsMcp.Server.Gramps;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddGrampsClient();

var app = builder.Build();

app.MapGet("/", () => "Hello World!");

app.Run();

/// <summary>Exposed for <c>WebApplicationFactory&lt;Program&gt;</c> in integration tests.</summary>
public partial class Program;

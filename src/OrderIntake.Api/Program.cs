// Composition root for the Order Intake API.
// Registers the clock (TimeProvider), the in-memory repository and OrderService as singletons,
// enum-as-string JSON, RFC 7807 problem details, Swagger (Development only) and a CORS policy
// for the Angular dev server, then maps the /api/orders endpoints.

using System.Text.Json.Serialization;
using OrderIntake.Api;
using OrderIntake.Core.Orders;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IOrderRepository, InMemoryOrderRepository>();
builder.Services.AddSingleton<OrderService>();

builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddProblemDetails();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o => o.SwaggerDoc("v1", new() { Title = "Order Intake API", Version = "v1" }));

const string UiCors = "ui";
builder.Services.AddCors(o => o.AddPolicy(UiCors, p => p
    .WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? ["http://localhost:4200"])
    .AllowAnyHeader()
    .AllowAnyMethod()
    .WithExposedHeaders(OrderEndpoints.ReplayHeader)));

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseCors(UiCors);

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapGet("/health", () => Results.Ok(new { status = "ok" })).ExcludeFromDescription();
app.MapOrderEndpoints();

app.Run();

// Exposed for WebApplicationFactory in the API tests.
public partial class Program;

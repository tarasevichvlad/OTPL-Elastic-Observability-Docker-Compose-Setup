using System.Diagnostics;
using System.Diagnostics.Metrics;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

// Constants
const string serviceName = "Weather.API";
const string serviceVersion = "0.0.1";
const string activitySourceName = $"{serviceName}_ActivitySource";
const string meterName = $"{serviceName}_Metrics";
var opentelmetryEndpoint = new Uri("http://localhost:4317");
var resourceBuilder = ResourceBuilder.CreateEmpty().AddService(serviceName, serviceVersion: serviceVersion).AddEnvironmentVariableDetector();

var builder = WebApplication.CreateBuilder(args);

// Configure metrics
builder.Services.AddOpenTelemetryMetrics(meterProviderBuilder =>
{
    meterProviderBuilder.SetResourceBuilder(resourceBuilder);
    meterProviderBuilder.AddHttpClientInstrumentation();
    meterProviderBuilder.AddAspNetCoreInstrumentation();
    meterProviderBuilder.AddMeter(meterName);
    meterProviderBuilder.AddOtlpExporter(options =>
    {
        options.Protocol = OtlpExportProtocol.Grpc;
        options.Endpoint = opentelmetryEndpoint;
    });
});

// Configure tracing
builder.Services.AddOpenTelemetryTracing(tracerProviderBuilder =>
{
    tracerProviderBuilder.SetResourceBuilder(resourceBuilder);
    tracerProviderBuilder.AddHttpClientInstrumentation();
    tracerProviderBuilder.AddAspNetCoreInstrumentation();
    tracerProviderBuilder.AddSource(activitySourceName);
    tracerProviderBuilder.AddOtlpExporter(options =>
    {
        options.Protocol = OtlpExportProtocol.Grpc;
        options.Endpoint = opentelmetryEndpoint;
    });
});

// Configure logging
builder.Logging.AddOpenTelemetry(openTelemetryLoggerOptions =>
{
    openTelemetryLoggerOptions.SetResourceBuilder(resourceBuilder);
    openTelemetryLoggerOptions.IncludeFormattedMessage = true;
    openTelemetryLoggerOptions.IncludeScopes = true;
    openTelemetryLoggerOptions.ParseStateValues = true;
    openTelemetryLoggerOptions.AddOtlpExporter(options =>
    {
        options.Protocol = OtlpExportProtocol.Grpc;
        options.Endpoint = opentelmetryEndpoint;
    });
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Create a route (GET /) that will make an http call, increment a metric and log a trace
var activitySource = new ActivitySource(activitySourceName);
var meter = new Meter(meterName);

var requestCounter = meter.CreateCounter<int>("compute_requests");
var httpClient = new HttpClient();

// Routes are tracked by AddAspNetCoreInstrumentation
// note: You can add more data to the Activity by using HttpContext.Activity.SetTag("key", "value")
app.MapGet("/", async (ILogger<Program> logger) =>
{
    requestCounter.Add(1);

    using (var activity = activitySource.StartActivity("Get data"))
    {
        // Add data the the activity
        // You can see these data in Zipkin
        activity?.AddTag("sample", "value");

        // Http calls are tracked by AddHttpClientInstrumentation
        var str1 = await httpClient.GetStringAsync("https://example.com");
        var str2 = await httpClient.GetStringAsync("https://www.meziantou.net");

        logger.LogInformation("Response1 length: {Length}", str1.Length);
        logger.LogInformation("Response2 length: {Length}", str2.Length);
    }

    return Results.Ok();
}).WithName("weather");

app.Run();
using System.Diagnostics;
using System.Diagnostics.Metrics;
using NLog;
using NLog.Web;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

static Action<OpenTelemetry.Exporter.OtlpExporterOptions> ConfigureOtlpExporter(
    IConfiguration configuration
)
{
    return otlpOptions =>
    {
        otlpOptions.Endpoint = new Uri(
            configuration["SigNoz:Endpoint"]
                ?? throw new InvalidOperationException("SigNoz:Endpoint configuration is required")
        );
        otlpOptions.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.Grpc;
        var headers = configuration["SigNoz:Headers"];
        if (!string.IsNullOrEmpty(headers))
        {
            otlpOptions.Headers = headers;
        }
    };
}

var logger = LogManager.Setup().LoadConfigurationFromFile("nlog.config").GetCurrentClassLogger();

var tempConfig = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: false)
    .Build();

var serviceName =
    tempConfig["Service:Name"]
    ?? throw new InvalidOperationException("Service:Name configuration is required");
var serviceVersion =
    tempConfig["Service:Version"]
    ?? throw new InvalidOperationException("Service:Version configuration is required");

// Create activity source for custom tracing
using var activitySource = new ActivitySource(name: serviceName, version: serviceVersion);

// Create custom metrics
using var meter = new Meter(name: serviceName, version: serviceVersion);
var requestCounter = meter.CreateCounter<int>(
    "weather_requests",
    "requests",
    "Number of weather forecast requests"
);
var temperatureHistogram = meter.CreateHistogram<int>(
    "weather_temperature",
    "celsius",
    "Temperature values in weather forecasts"
);

try
{
    logger.Info("Starting up the application");

    var builder = WebApplication.CreateBuilder(args);

    // NLog: Setup NLog for Dependency injection
    builder.Logging.ClearProviders();

    var loggingOptions = NLogAspNetCoreOptions.Default;
    loggingOptions.RemoveLoggerFactoryFilter = false;
    builder.Host.UseNLog(loggingOptions);

    // Configure logging to include OpenTelemetry
    builder.Logging.AddOpenTelemetry(options =>
    {
        options.AddOtlpExporter(ConfigureOtlpExporter(builder.Configuration));

        options.IncludeScopes = true;
        options.IncludeFormattedMessage = true;
    });

    // Add OpenTelemetry
    builder
        .Services.AddOpenTelemetry()
        .ConfigureResource(resource =>
            resource.AddService(serviceName: serviceName, serviceVersion: serviceVersion)
        )
        .WithTracing(tracing =>
            tracing
                .AddSource(names: activitySource.Name)
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddOtlpExporter(ConfigureOtlpExporter(builder.Configuration))
        )
        .WithMetrics(metrics =>
            metrics
                .AddMeter(names: meter.Name)
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddOtlpExporter(ConfigureOtlpExporter(builder.Configuration))
        );

    // Add health checks for metrics endpoint
    builder.Services.AddHealthChecks();

    var app = builder.Build();

    app.UseHttpsRedirection();

    // Add health check endpoint
    app.MapHealthChecks("/health");

    var summaries = new[]
    {
        "Freezing",
        "Bracing",
        "Chilly",
        "Cool",
        "Mild",
        "Warm",
        "Balmy",
        "Hot",
        "Sweltering",
        "Scorching",
    };

    app.MapGet(
            "/weatherforecast",
            (ILogger<Program> logger) =>
            {
                using var activity = activitySource.StartActivity("GenerateWeatherForecast");

                logger.LogInformation("WeatherForecast endpoint called");

                // Add custom tags to the activity
                activity?.SetTag("app.operation", "weather-forecast");
                activity?.SetTag("app.forecast.count", "5");

                // Increment some meter
                requestCounter.Add(1);

                var forecast = Enumerable
                    .Range(1, 5)
                    .Select(index =>
                    {
                        // Create a nested activity for forecast generation
                        using var forecastActivity = activitySource.StartActivity(
                            "GenerateForecast"
                        );

                        var temp = Random.Shared.Next(-20, 55);
                        // Record temperature metric
                        temperatureHistogram.Record(temp);

                        return new WeatherForecast(
                            DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
                            temp,
                            summaries[Random.Shared.Next(summaries.Length)]
                        );
                    })
                    .ToArray();

                logger.LogInformation("Generated {Count} weather forecasts", forecast.Length);

                // Add activity event
                activity?.AddEvent(
                    new(
                        "ForecastGenerated",
                        DateTimeOffset.UtcNow,
                        new ActivityTagsCollection
                        {
                            ["forecast.count"] = forecast.Length,
                            ["forecast.avg_temp"] = forecast.Average(f => f.TemperatureC),
                        }
                    )
                );

                return forecast;
            }
        )
        .WithName("GetWeatherForecast");

    logger.Info("Application configured, starting web host");
    app.Run();
}
catch (Exception exception)
{
    logger.Error(exception, "Stopped program because of exception");
    throw;
}
finally
{
    LogManager.Shutdown();
}

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}

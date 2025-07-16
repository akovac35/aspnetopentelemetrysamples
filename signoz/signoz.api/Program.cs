using NLog;
using NLog.Web;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using System.Diagnostics;
using System.Diagnostics.Metrics;

// Early init of NLog to allow startup and exception logging, before host is built
var logger = LogManager.Setup().LoadConfigurationFromFile("nlog.config").GetCurrentClassLogger();

// Create activity source for custom tracing
var activitySource = new ActivitySource("SignozApi");

// Create custom metrics
var meterProvider = new Meter("SignozApi", "1.0.0");
var requestCounter = meterProvider.CreateCounter<int>("weather_requests", "requests", "Number of weather forecast requests");
var temperatureHistogram = meterProvider.CreateHistogram<int>("weather_temperature", "celsius", "Temperature values in weather forecasts");

try
{
    logger.Info("Starting up the application");
    
    var builder = WebApplication.CreateBuilder(args);

    // Add services to the container.
    // Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
    builder.Services.AddOpenApi();

    // NLog: Setup NLog for Dependency injection
    builder.Logging.ClearProviders();
    builder.Host.UseNLog();

    // Configure logging to include OpenTelemetry
    builder.Logging.AddOpenTelemetry(options =>
    {
        options.AddOtlpExporter(otlpOptions =>
        {
            otlpOptions.Endpoint = new Uri(builder.Configuration["SigNoz:Endpoint"] ?? throw new InvalidOperationException());
            otlpOptions.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.Grpc;
            var headers = builder.Configuration["SigNoz:Headers"];
            if (!string.IsNullOrEmpty(headers))
            {
                otlpOptions.Headers = headers;
            }
        });
        
        // Include scopes in logs
        options.IncludeScopes = true;
        options.IncludeFormattedMessage = true;
    });

    // Add OpenTelemetry
    builder.Services.AddOpenTelemetry()
        .ConfigureResource(resource => resource
            .AddService(serviceName: "SignozApi", serviceVersion: "1.0.0"))
        .WithTracing(tracing => tracing
            .AddSource(names: "SignozApi")
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddOtlpExporter(otlpOptions =>
            {
                otlpOptions.Endpoint = new Uri(builder.Configuration["SigNoz:Endpoint"] ?? throw new InvalidOperationException());
                otlpOptions.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.Grpc;
                var headers = builder.Configuration["SigNoz:Headers"];
                if (!string.IsNullOrEmpty(headers))
                {
                    otlpOptions.Headers = headers;
                }
            }))
        .WithMetrics(metrics => metrics
            .AddMeter(names: "SignozApi")
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddOtlpExporter(otlpOptions =>
            {
                otlpOptions.Endpoint = new Uri(builder.Configuration["SigNoz:Endpoint"] ?? throw new InvalidOperationException());
                otlpOptions.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.Grpc;
                var headers = builder.Configuration["SigNoz:Headers"];
                if (!string.IsNullOrEmpty(headers))
                {
                    otlpOptions.Headers = headers;
                }
            }));

    // Add health checks for metrics endpoint
    builder.Services.AddHealthChecks();

    var app = builder.Build();

    // Configure the HTTP request pipeline.
    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi();
    }

    app.UseHttpsRedirection();

    // Add health check endpoint
    app.MapHealthChecks("/health");

    var summaries = new[]
    {
        "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
    };

    app.MapGet("/weatherforecast", (ILogger<Program> logger) =>
    {
        using var activity = activitySource.StartActivity("GenerateWeatherForecast");
        
        logger.LogInformation("WeatherForecast endpoint called");
        
        // Add custom tags to the activity
        activity?.SetTag("operation", "weather-forecast");
        activity?.SetTag("forecast.count", "5");
        
        // Increment request counter
        requestCounter.Add(1);
        
        // Create a nested activity for forecast generation
        using var forecastActivity = activitySource.StartActivity("GenerateForecast");
        
        var forecast = Enumerable.Range(1, 5).Select(index =>
        {
            var temp = Random.Shared.Next(-20, 55);
            // Record temperature metric
            temperatureHistogram.Record(temp);
            
            return new WeatherForecast
            (
                DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
                temp,
                summaries[Random.Shared.Next(summaries.Length)]
            );
        })
        .ToArray();
        
        forecastActivity?.SetTag("forecast.generated", forecast.Length.ToString());
        forecastActivity?.SetTag("forecast.avg_temp", forecast.Average(f => f.TemperatureC).ToString("F1"));
            
        logger.LogInformation("Generated {Count} weather forecasts", forecast.Length);
        
        // Add activity event
        activity?.AddEvent(new("ForecastGenerated", DateTimeOffset.UtcNow, new ActivityTagsCollection
        {
            ["forecast.count"] = forecast.Length,
            ["forecast.avg_temp"] = forecast.Average(f => f.TemperatureC)
        }));
        
        return forecast;
    })
    .WithName("GetWeatherForecast");

    // Add a custom metrics endpoint
    app.MapGet("/metrics/custom", (ILogger<Program> logger) =>
    {
        using var activity = activitySource.StartActivity("CustomMetrics");
        
        logger.LogInformation("Custom metrics endpoint called");
        
        activity?.SetTag("operation", "custom-metrics");
        
        return new
        {
            timestamp = DateTime.UtcNow,
            uptime = DateTime.UtcNow.Subtract(Process.GetCurrentProcess().StartTime),
            memory = GC.GetTotalMemory(false),
            gen0Collections = GC.CollectionCount(0),
            gen1Collections = GC.CollectionCount(1),
            gen2Collections = GC.CollectionCount(2)
        };
    })
    .WithName("GetCustomMetrics");

    logger.Info("Application configured, starting web host");
    app.Run();
}
catch (Exception exception)
{
    // NLog: catch setup errors
    logger.Error(exception, "Stopped program because of exception");
    throw;
}
finally
{
    // Ensure to flush and stop internal timers/threads before application-exit (Avoid segmentation fault on Linux)
    LogManager.Shutdown();
}

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}

using Observability.Logging;
using Observability.Middleware;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Formatting.Compact;
using System.Diagnostics;
using System.Reflection;

namespace Observability.Extensions;

public static class ObservabilityExtensions
{
    /// <summary>
    /// Adds complete Observability stack: Metrics, Tracing, and Logging.
    /// </summary>
    public static WebApplicationBuilder AddObservability(
        this WebApplicationBuilder builder,
        string serviceName,
        string? serviceVersion = null)
    {
        var version = serviceVersion ?? Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "1.0.0";

        // Configure OpenTelemetry (Metrics + Tracing)
        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(
                    serviceName: serviceName,
                    serviceVersion: version,
                    serviceInstanceId: Environment.MachineName)
                .AddAttributes(new Dictionary<string, object>
                {
                    ["deployment.environment"] = builder.Environment.EnvironmentName,
                    ["host.name"] = Environment.MachineName
                }))
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddPrometheusExporter())
            .WithTracing(tracing =>
            {
                tracing
                    .AddAspNetCoreInstrumentation(options =>
                    {
                        options.Filter = httpContext =>
                        {
                            // Don't trace health check endpoint
                            return !httpContext.Request.Path.Equals("/health", StringComparison.OrdinalIgnoreCase)
                                && !httpContext.Request.Path.Equals("/metrics", StringComparison.OrdinalIgnoreCase);
                        };
                    })
                    .AddHttpClientInstrumentation();

                // Add OTLP Exporter для Jaeger
                var jaegerEndpoint = builder.Configuration["Observability:Jaeger:Endpoint"];
                if (!string.IsNullOrEmpty(jaegerEndpoint))
                {
                    tracing.AddOtlpExporter(options =>
                    {
                        options.Endpoint = new Uri(jaegerEndpoint);
                    });
                }
            });

        // Configure Serilog
        Log.Logger = new LoggerConfiguration()
            .ReadFrom.Configuration(builder.Configuration)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Service", serviceName)
            .Enrich.WithProperty("Version", version)
            .Enrich.WithProperty("Environment", builder.Environment.EnvironmentName)
            .Enrich.WithMachineName()
            .Enrich.WithThreadId()
            .Enrich.With<OpenTelemetryEnricher>()
            .WriteTo.Console(new CompactJsonFormatter())
            .CreateLogger();

        builder.Host.UseSerilog();

        return builder;
    }

    /// <summary>
    /// Adds Observability middleware to the request pipeline.
    /// </summary>
    public static WebApplication UseObservability(this WebApplication app)
    {
        // Add CorrelationId middleware early in the pipeline
        app.UseMiddleware<CorrelationIdMiddleware>();

        // Serilog request logging
        app.UseSerilogRequestLogging(options =>
        {
            options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
            {
                diagnosticContext.Set("ClientIP", httpContext.Connection.RemoteIpAddress?.ToString());
                diagnosticContext.Set("UserAgent", httpContext.Request.Headers["User-Agent"].ToString());
                diagnosticContext.Set("CorrelationId", httpContext.Items["CorrelationId"]?.ToString());

                var userId = httpContext.User?.FindFirst("sub")?.Value;
                if (!string.IsNullOrEmpty(userId))
                {
                    diagnosticContext.Set("UserId", userId);
                }

                // Add trace context to logs
                var activity = Activity.Current;
                if (activity != null)
                {
                    diagnosticContext.Set("TraceId", activity.TraceId.ToString());
                    diagnosticContext.Set("SpanId", activity.SpanId.ToString());
                }
            };
        });

        return app;
    }

    /// <summary>
    /// Maps Observability endpoints (/health, /metrics).
    /// </summary>
    public static WebApplication MapObservabilityEndpoints(this WebApplication app)
    {
        // Health check endpoint
        app.MapGet("/health", () => Results.Ok(new
        {
            status = "healthy",
            timestamp = DateTime.UtcNow,
            service = app.Environment.ApplicationName,
            environment = app.Environment.EnvironmentName
        })).WithName("HealthCheck").ExcludeFromDescription();

        // Prometheus metrics endpoint
        app.MapPrometheusScrapingEndpoint();

        return app;
    }
}

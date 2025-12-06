using System.Diagnostics;
using Serilog.Core;
using Serilog.Events;

namespace Observability.Logging;

/// <summary>
/// Enriches Serilog log events with OpenTelemetry trace context (TraceId, SpanId, ParentSpanId).
/// This enables correlation between logs and distributed traces in Jaeger.
/// </summary>
public class OpenTelemetryEnricher : ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        var activity = Activity.Current;
        if (activity is null)
            return;

        // Add TraceId
        if (activity.TraceId != default)
        {
            logEvent.AddPropertyIfAbsent(
                propertyFactory.CreateProperty("TraceId", activity.TraceId.ToString()));
        }

        // Add SpanId
        if (activity.SpanId != default)
        {
            logEvent.AddPropertyIfAbsent(
                propertyFactory.CreateProperty("SpanId", activity.SpanId.ToString()));
        }

        // Add ParentSpanId (если есть)
        if (activity.ParentSpanId != default)
        {
            logEvent.AddPropertyIfAbsent(
                propertyFactory.CreateProperty("ParentSpanId", activity.ParentSpanId.ToString()));
        }

        // Add Baggage items (key-value pairs propagated across services)
        foreach (var baggage in activity.Baggage)
        {
            logEvent.AddPropertyIfAbsent(
                propertyFactory.CreateProperty($"Baggage_{baggage.Key}", baggage.Value ?? string.Empty));
        }

        // Add important tags from activity
        AddTagIfExists(activity, logEvent, propertyFactory, "user.id", "UserId");
        AddTagIfExists(activity, logEvent, propertyFactory, "http.method", "HttpMethod");
        AddTagIfExists(activity, logEvent, propertyFactory, "http.url", "HttpUrl");
    }

    private static void AddTagIfExists(
        Activity activity,
        LogEvent logEvent,
        ILogEventPropertyFactory propertyFactory,
        string tagKey,
        string propertyName)
    {
        var tagValue = activity.GetTagItem(tagKey);
        if (tagValue is not null)
        {
            logEvent.AddPropertyIfAbsent(
                propertyFactory.CreateProperty(propertyName, tagValue));
        }
    }
}

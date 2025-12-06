using Microsoft.AspNetCore.Http;

namespace Observability.Http;

/// <summary>
/// HTTP Message Handler that propagates CorrelationId to outgoing HTTP requests.
/// Ensures the same CorrelationId flows through the entire distributed system.
/// </summary>
public class CorrelationIdDelegatingHandler : DelegatingHandler
{
    private const string CorrelationIdHeader = "X-Correlation-Id";
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CorrelationIdDelegatingHandler(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var correlationId = _httpContextAccessor.HttpContext?.Items["CorrelationId"] as string;

        if (!string.IsNullOrEmpty(correlationId) && !request.Headers.Contains(CorrelationIdHeader))
        {
            request.Headers.Add(CorrelationIdHeader, correlationId);
        }

        return await base.SendAsync(request, cancellationToken);
    }
}

using System.Diagnostics;

var builder = WebApplication.CreateBuilder(args);

builder.AddObservability("gateway-service");

// Add services
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHttpContextAccessor();

// Configure HttpClients с CorrelationId propagation
builder.Services.AddTransient<CorrelationIdDelegatingHandler>();

builder.Services.AddHttpClient("AuthService", client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Services:AuthService:Url"] ?? "http://auth-service:5001");
    client.Timeout = TimeSpan.FromSeconds(30);
}).AddHttpMessageHandler<CorrelationIdDelegatingHandler>();

builder.Services.AddHttpClient("OrderService", client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Services:OrderService:Url"] ?? "http://order-service:5002");
    client.Timeout = TimeSpan.FromSeconds(30);
}).AddHttpMessageHandler<CorrelationIdDelegatingHandler>();

var app = builder.Build();

app.UseObservability();

// Swagger
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapObservabilityEndpoints();


// Root endpoint
app.MapGet("/", () => new
{
    service = "gateway-service",
    version = "1.0.0",
    timestamp = DateTime.UtcNow,
    endpoints = new[]
    {
        "/health",
        "/metrics",
        "/api/orders",
        "/api/auth/login",
        "/api/auth/register"
    }
}).WithName("GetRoot").WithOpenApi();

// Proxy to Auth Service
app.MapPost("/api/auth/login", async (LoginRequest request, IHttpClientFactory httpClientFactory, ILogger<Program> logger) =>
{
    using var activity = Activity.Current;
    activity?.SetTag("operation", "proxy_auth_login");
    activity?.SetTag("email", request.Email);

    var client = httpClientFactory.CreateClient("AuthService");

    try
    {
        logger.LogInformation("Proxying login request for email {Email}", request.Email);

        var response = await client.PostAsJsonAsync("/api/auth/login", request);

        if (response.IsSuccessStatusCode)
        {
            var result = await response.Content.ReadFromJsonAsync<LoginResponse>();
            logger.LogInformation("Login successful for email {Email}", request.Email);
            return Results.Ok(result);
        }

        logger.LogWarning("Login failed for email {Email}. Status: {StatusCode}", request.Email, response.StatusCode);
        return Results.Unauthorized();
    }
    catch (Exception ex)
    {
        activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
        activity?.SetTag("exception.type", ex.GetType().FullName);
        activity?.SetTag("exception.message", ex.Message);
        logger.LogError(ex, "Error proxying login request for email {Email}", request.Email);
        return Results.Problem("Failed to process login request");
    }
}).WithName("Login").WithOpenApi();

// Proxy to Order Service
app.MapGet("/api/orders", async (IHttpClientFactory httpClientFactory, ILogger<Program> logger) =>
{
    using var activity = Activity.Current;
    activity?.SetTag("operation", "proxy_get_orders");

    var client = httpClientFactory.CreateClient("OrderService");

    try
    {
        logger.LogInformation("Proxying request to get orders");

        var response = await client.GetAsync("/api/orders");

        if (response.IsSuccessStatusCode)
        {
            var orders = await response.Content.ReadFromJsonAsync<List<Order>>();
            logger.LogInformation("Retrieved {OrderCount} orders", orders?.Count ?? 0);
            return Results.Ok(orders);
        }

        logger.LogWarning("Failed to get orders. Status: {StatusCode}", response.StatusCode);
        return Results.StatusCode((int)response.StatusCode);
    }
    catch (Exception ex)
    {
        activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
        activity?.SetTag("exception.type", ex.GetType().FullName);
        activity?.SetTag("exception.message", ex.Message);
        logger.LogError(ex, "Error proxying get orders request");
        return Results.Problem("Failed to retrieve orders");
    }
}).WithName("GetOrders").WithOpenApi();

app.MapPost("/api/orders", async (CreateOrderRequest request, IHttpClientFactory httpClientFactory, ILogger<Program> logger) =>
{
    using var activity = Activity.Current;
    activity?.SetTag("operation", "proxy_create_order");
    activity?.SetTag("order.items_count", request.Items.Count);

    var client = httpClientFactory.CreateClient("OrderService");

    try
    {
        logger.LogInformation("Proxying create order request with {ItemCount} items", request.Items.Count);

        var response = await client.PostAsJsonAsync("/api/orders", request);

        if (response.IsSuccessStatusCode)
        {
            var order = await response.Content.ReadFromJsonAsync<Order>();
            logger.LogInformation("Order created successfully. OrderId: {OrderId}", order?.Id);
            return Results.Ok(order);
        }

        logger.LogWarning("Failed to create order. Status: {StatusCode}", response.StatusCode);
        return Results.StatusCode((int)response.StatusCode);
    }
    catch (Exception ex)
    {
        activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
        activity?.SetTag("exception.type", ex.GetType().FullName);
        activity?.SetTag("exception.message", ex.Message);
        logger.LogError(ex, "Error proxying create order request");
        return Results.Problem("Failed to create order");
    }
}).WithName("CreateOrder").WithOpenApi();

// Run application
try
{
    Log.Information("Starting Gateway Service on {Environment}", app.Environment.EnvironmentName);
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Gateway Service failed to start");
    throw;
}
finally
{
    Log.CloseAndFlush();
}

record LoginRequest(string Email, string Password);
record LoginResponse(string Token, string RefreshToken, DateTime ExpiresAt);

record CreateOrderRequest(List<OrderItem> Items);
record OrderItem(string ProductId, int Quantity, decimal Price);
record Order(string Id, List<OrderItem> Items, decimal TotalAmount, string Status, DateTime CreatedAt);

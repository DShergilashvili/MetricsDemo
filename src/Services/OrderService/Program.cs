using System.Diagnostics;

var builder = WebApplication.CreateBuilder(args);

builder.AddObservability("order-service");

// Add services
builder.Services.AddEndpointsApiExplorer();

// In-memory storage (for demo purposes)
var orders = new List<Order>();
var _totalAmount = 0m;

// Custom metrics
var meter = new System.Diagnostics.Metrics.Meter("OrderService", "1.0.0");
var ordersCreatedCounter = meter.CreateCounter<long>("orders_created_total");
var ordersCompletedCounter = meter.CreateCounter<long>("orders_completed_total");
var ordersCancelledCounter = meter.CreateCounter<long>("orders_cancelled_total");
var ordersTotalAmountGauge = meter.CreateObservableGauge("orders_total_amount", () => _totalAmount);

var app = builder.Build();

app.UseObservability();

app.MapObservabilityEndpoints();


app.MapGet("/", () => new
{
    service = "order-service",
    version = "1.0.0",
    timestamp = DateTime.UtcNow
}).WithName("GetRoot");

// Get all orders
app.MapGet("/api/orders", (ILogger<Program> logger) =>
{
    using var activity = Activity.Current;
    activity?.SetTag("operation", "get_orders");
    activity?.SetTag("count", orders.Count);

    logger.LogInformation("Retrieving {Count} orders", orders.Count);

    return Results.Ok(orders);
}).WithName("GetOrders");

// Create order
app.MapPost("/api/orders", (CreateOrderRequest request, ILogger<Program> logger) =>
{
    using var activity = Activity.Current;
    activity?.SetTag("operation", "create_order");
    activity?.SetTag("items_count", request.Items.Count);

    var orderId = Guid.NewGuid().ToString();
    var totalAmount = request.Items.Sum(i => i.Price * i.Quantity);

    var order = new Order(
        Id: orderId,
        Items: request.Items,
        TotalAmount: totalAmount,
        Status: "pending",
        CreatedAt: DateTime.UtcNow
    );

    orders.Add(order);
    _totalAmount += totalAmount;

    // Record metrics
    ordersCreatedCounter.Add(1, new KeyValuePair<string, object?>("status", "pending"));

    activity?.SetTag("order.id", orderId);
    activity?.SetTag("order.amount", totalAmount);

    logger.LogInformation("Order {OrderId} created successfully. Amount: {Amount}, Items: {ItemCount}",
        orderId, totalAmount, request.Items.Count);

    // Simulate async processing (mark as completed after random delay)
    _ = Task.Run(async () =>
    {
        await Task.Delay(Random.Shared.Next(1000, 5000));

        var existingOrder = orders.FirstOrDefault(o => o.Id == orderId);
        if (existingOrder != null)
        {
            var index = orders.IndexOf(existingOrder);
            orders[index] = existingOrder with { Status = "completed" };

            ordersCompletedCounter.Add(1, new KeyValuePair<string, object?>("payment_method", "card"));

            logger.LogInformation("Order {OrderId} completed", orderId);
        }
    });

    return Results.Ok(order);
}).WithName("CreateOrder");

// Get order by ID
app.MapGet("/api/orders/{id}", (string id, ILogger<Program> logger) =>
{
    var order = orders.FirstOrDefault(o => o.Id == id);

    if (order == null)
    {
        logger.LogWarning("Order {OrderId} not found", id);
        return Results.NotFound();
    }

    logger.LogInformation("Retrieved order {OrderId}", id);
    return Results.Ok(order);
}).WithName("GetOrderById");

// Cancel order
app.MapDelete("/api/orders/{id}", (string id, ILogger<Program> logger) =>
{
    var order = orders.FirstOrDefault(o => o.Id == id);

    if (order == null)
    {
        logger.LogWarning("Order {OrderId} not found for cancellation", id);
        return Results.NotFound();
    }

    if (order.Status == "completed")
    {
        logger.LogWarning("Cannot cancel completed order {OrderId}", id);
        return Results.BadRequest(new { error = "Cannot cancel completed order" });
    }

    var index = orders.IndexOf(order);
    orders[index] = order with { Status = "cancelled" };

    _totalAmount -= order.TotalAmount;

    ordersCancelledCounter.Add(1, new KeyValuePair<string, object?>("reason", "user_request"));

    logger.LogInformation("Order {OrderId} cancelled", id);

    return Results.Ok(orders[index]);
}).WithName("CancelOrder");

// Run application
try
{
    Log.Information("Starting Order Service on {Environment}", app.Environment.EnvironmentName);
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Order Service failed to start");
    throw;
}
finally
{
    Log.CloseAndFlush();
}

record CreateOrderRequest(List<OrderItem> Items);
record OrderItem(string ProductId, int Quantity, decimal Price);
record Order(string Id, List<OrderItem> Items, decimal TotalAmount, string Status, DateTime CreatedAt);

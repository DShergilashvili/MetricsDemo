using System.Diagnostics;

var builder = WebApplication.CreateBuilder(args);

builder.AddObservability("auth-service");

// Add services
builder.Services.AddEndpointsApiExplorer();

// Custom metrics
var meter = new System.Diagnostics.Metrics.Meter("AuthService", "1.0.0");
var loginAttemptsCounter = meter.CreateCounter<long>("auth_login_attempts_total");
var registrationsCounter = meter.CreateCounter<long>("auth_registrations_total");

var app = builder.Build();

app.UseObservability();

app.MapObservabilityEndpoints();


app.MapGet("/", () => new
{
    service = "auth-service",
    version = "1.0.0",
    timestamp = DateTime.UtcNow
}).WithName("GetRoot");

// Mock login endpoint
app.MapPost("/api/auth/login", (LoginRequest request, ILogger<Program> logger) =>
{
    using var activity = Activity.Current;
    activity?.SetTag("operation", "login");
    activity?.SetTag("email", request.Email);

    // Simulate authentication (80% success rate)
    var isSuccess = Random.Shared.Next(100) < 80;

    if (isSuccess)
    {
        loginAttemptsCounter.Add(1,
            new KeyValuePair<string, object>("status", "success"));

        logger.LogInformation("Login successful for email {Email}", request.Email);

        var token = Convert.ToBase64String(Guid.NewGuid().ToByteArray());
        var refreshToken = Convert.ToBase64String(Guid.NewGuid().ToByteArray());

        return Results.Ok(new LoginResponse(
            Token: token,
            RefreshToken: refreshToken,
            ExpiresAt: DateTime.UtcNow.AddHours(1)
        ));
    }
    else
    {
        loginAttemptsCounter.Add(1,
            new KeyValuePair<string, object>("status", "failed"),
            new KeyValuePair<string, object>("reason", "invalid_credentials"));

        logger.LogWarning("Login failed for email {Email}. Invalid credentials.", request.Email);

        return Results.Unauthorized();
    }
}).WithName("Login");

// Mock register endpoint
app.MapPost("/api/auth/register", (RegisterRequest request, ILogger<Program> logger) =>
{
    using var activity = Activity.Current;
    activity?.SetTag("operation", "register");
    activity?.SetTag("email", request.Email);
    activity?.SetTag("name", request.Name);

    registrationsCounter.Add(1);

    logger.LogInformation("User registered successfully. Email: {Email}, Name: {Name}", request.Email, request.Name);

    var userId = Guid.NewGuid().ToString();

    return Results.Ok(new RegisterResponse(
        UserId: userId,
        Email: request.Email,
        Name: request.Name
    ));
}).WithName("Register");

// Mock verify endpoint
app.MapPost("/api/auth/verify", (ILogger<Program> logger) =>
{
    logger.LogInformation("Token verified successfully");
    return Results.Ok(new { valid = true });
}).WithName("VerifyToken");

// Run application
try
{
    Log.Information("Starting Auth Service on {Environment}", app.Environment.EnvironmentName);
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Auth Service failed to start");
    throw;
}
finally
{
    Log.CloseAndFlush();
}

record LoginRequest(string Email, string Password);
record LoginResponse(string Token, string RefreshToken, DateTime ExpiresAt);
record RegisterRequest(string Email, string Password, string Name);
record RegisterResponse(string UserId, string Email, string Name);

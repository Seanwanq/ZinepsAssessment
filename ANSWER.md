# For the answers:

## 1. Target framework & build setup

I targeted .NET 10.0 because .NET 10 was officially released on November 11, 2025, about two weeks before the assessment.

Why I chose .NET 10:
- It was the latest stable release at the time (also is it LTS)
- For a new project/assessment, I wanted to demonstrate awareness of current technology
- The assessment didn't specify a particular .NET version

However, I realize teams often have good reasons to stay on earlier versions. If Zineps is currently on .NET 8 or 9, I'm happy to retarget the project. The code doesn't use any .NET 10-specific features, so it's just a project file change.

## 2. Dependency Injection & composition root

I used "new" in Blazor pages due to time pressure and focusing on core logic first. Not production-ready.

Proper approach:
```csharp
// Program.cs
builder.Services.AddScoped<ICarrierIntegration, SpeedShipIntegrationService>();
builder.Services.AddScoped<InvoiceReconciliationService>();
builder.Services.AddSingleton(configuration);

builder.Services.AddHttpClient<ICarrierIntegration>()
    .AddPolicyHandler(GetRetryPolicy());
```

```csharp
// Component
@inject ICarrierIntegration CarrierService
// Use directly, no "new"
```

My mental model: Composition root (Program.cs) wires up the object graph once. Everything else receives dependencies via constructor injection. Enables testability and centralized configuration.

## 3. Carrier integration design & patterns

Adding UPS/DPD:

```csharp
// 1. Create adapters
public class UPSIntegrationService : ICarrierIntegration { }
public class DPDIntegrationService : ICarrierIntegration { }

// 2. Factory
public class CarrierFactory : ICarrierFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly Dictionary<string, Type> _carrierMap = new()
    {
        { "SPEEDSHIP", typeof(SpeedShipIntegrationService) },
        { "UPS", typeof(UPSIntegrationService) }
    };
    
    public ICarrierIntegration CreateCarrier(string code)
    {
        var type = _carrierMap[code.ToUpper()];
        return (ICarrierIntegration)_serviceProvider.GetRequiredService(type);
    }
}

// 3. Register
builder.Services.AddScoped<SpeedShipIntegrationService>();
builder.Services.AddScoped<ICarrierFactory, CarrierFactory>();
```

Cross-cutting concerns via Decorator:
```csharp
public class LoggingCarrierDecorator : ICarrierIntegration
{
    private readonly ICarrierIntegration _inner;
    private readonly ILogger _logger;
    
    public async Task<ShipmentResult> CreateShipmentAsync(...)
    {
        _logger.LogInformation("Creating shipment");
        return await _inner.CreateShipmentAsync(...);
    }
}

builder.Services.Decorate<ICarrierIntegration, LoggingCarrierDecorator>();
```

Keeps carrier classes focused on API logic.

## 4. Error handling, retries, resiliency

Current retry logic: 3 attempts with exponential backoff. Problem: No 
circuit breaker, could hammer failing service.

Better with Polly:
```csharp
var retryPolicy = Policy
    .Handle<HttpRequestException>()
    .WaitAndRetryAsync(3, 
        attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)) + 
                   TimeSpan.FromMilliseconds(Random.Shared.Next(0, 1000))); // Jitter

var circuitBreaker = Policy
    .Handle<HttpRequestException>()
    .CircuitBreakerAsync(5, TimeSpan.FromMinutes(1));

var bulkhead = Policy.BulkheadAsync(10, 100); // Max 10 concurrent

builder.Services.AddHttpClient<ICarrierIntegration>()
    .AddPolicyHandler(retryPolicy)
    .AddPolicyHandler(circuitBreaker)
    .AddPolicyHandler(bulkhead);
```

Prevents thundering herd: Circuit breaker stops calls when failing, bulkhead limits concurrent requests, jitter spreads retries.

## 5. HTTP integration vs stubs

For real integration, I'd structure it like this:

Project Structure:
```text
Zineps.Infrastructure/Carriers/SpeedShip/
==> SpeedShipClient.cs           // HTTP wrapper
==> SpeedShipIntegrationService.cs
==> DTOs/                         // Carrier-specific
==> Mappers/SpeedShipMapper.cs   // DTO ↔ Domain
```

HttpClient Setup (Program.cs):
```csharp
builder.Services.AddHttpClient<ISpeedShipClient, SpeedShipClient>(client =>
{
    client.BaseAddress = new Uri("https://api.speedship.com");
    client.Timeout = TimeSpan.FromSeconds(30);
})
.ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
{
    PooledConnectionLifetime = TimeSpan.FromMinutes(5)
})
.AddPolicyHandler(GetRetryPolicy())
.AddPolicyHandler(GetCircuitBreakerPolicy());
```

HTTP Client (SpeedShipClient.cs):
```csharp
public class SpeedShipClient
{
    private readonly HttpClient _httpClient;
    
    public async Task<SpeedShipAuthResponseDto> AuthenticateAsync(
        SpeedShipAuthRequestDto request)
    {
        var response = await _httpClient.PostAsJsonAsync("/auth/token", request);
        response.EnsureSuccessStatusCode();
        
        var dto = await response.Content
            .ReadFromJsonAsync<SpeedShipAuthResponseDto>();
        
        // Validate response
        if (dto == null || string.IsNullOrEmpty(dto.Token))
            throw new SpeedShipApiException("Invalid auth response");
        
        return dto;
    }
}
```

DTOs (match carrier API exactly):
```csharp
public record SpeedShipAuthRequestDto
{
    [JsonPropertyName("api_key")]
    public string ApiKey { get; init; }
}

public record SpeedShipAuthResponseDto
{
    [JsonPropertyName("access_token")]
    public string Token { get; init; }
    
    [JsonPropertyName("expires_in")]
    public int ExpiresInSeconds { get; init; }
}
```

Mapper (separate concerns):
```csharp
public static class SpeedShipMapper
{
    public static ShipmentResult ToDomain(SpeedShipShipmentResponseDto dto)
    {
        return new ShipmentResult
        {
            TrackingNumber = dto.TrackingId,  // Note: different field names
            Amount = dto.Cost.Total
        };
    }
}
```

Key decisions:
- HttpClient lifetime: Typed client via DI (proper pooling)
- DTOs: In Infrastructure layer, match carrier schema exactly
- Mapping: Separate mapper class (not in DTOs or domain)
- Validation: At HTTP layer (check status, parse errors, validate structure)
- Resiliency: Polly for retry/circuit breaker, configured on HttpClient


## 6. Invoice reconciliation & scalability

For 1M+ records: Use ReconcileInvoicesInBatchesAsync (already in code).
- Processes in 10K batches → manageable memory
- Uses IAsyncEnumerable → streams data
- Can be cancelled

Parallelization:
```csharp
// Option 1: Partition by date
await Parallel.ForEachAsync(dateGroups, async (group, ct) =>
{
    await reconciliationService.ReconcileInvoicesAsync(group, ct);
});

// Option 2: Azure Function queue
[FunctionName("Process")]
public async Task Process([QueueTrigger("reconciliation")] Batch batch)
{
    await reconciliationService.ReconcileInvoicesAsync(batch.Data);
}
```

If timing out, first checks:
1. Database query plans (indexes on tracking_number?)
2. Memory pressure (batch size too large?)
3. Algorithm efficiency (O(N) with dictionary lookup?)


## 7. Testing strategy

Decided on: Happy path, each discrepancy type separately, edge cases (unmatched records), severity thresholds.

Missing edge cases:
- Token expiration mid-operation
- Concurrent authentication (race condition)
- Rate limiting (429 responses)
- Negative amounts (refunds)
- Duplicate tracking numbers
- Performance: 100K records timing

Integration tests for production:
```csharp
// Mock HTTP with WireMock
var mock = WireMockServer.Start();
mock.Given(Request.Create().WithPath("/auth"))
    .RespondWith(Response.Create().WithBodyAsJson(authDto));

var service = new SpeedShipIntegrationService(config);
var result = await service.AuthenticateAsync();
```

E2E: Full flow (create shipment → get label), load tests (1000 concurrent), chaos tests (kill dependencies mid-request).


## 8. Blazor UI & UX

Top 3 for production:

1. Validation:
```csharp
<EditForm Model="@request" OnValidSubmit="Submit">
    <DataAnnotationsValidator />
    <ValidationSummary />
</EditForm>

public class ShipmentRequest
{
    [Required, MaxLength(100)]
    public string RecipientName { get; set; }
}
```

2. State management:
```csharp
public class CarrierStateService
{
    public event Action? OnChange;
    public async Task AuthenticateAsync() 
    { 
        /* ... */ 
        OnChange?.Invoke(); 
    }
}
```

3. Error boundaries & accessibility:
```csharp
<ErrorBoundary>
    <ChildContent>@Body</ChildContent>
    <ErrorContent>Error occurred</ErrorContent>
</ErrorBoundary>
```

Refactor into components: CarrierAuthPanel.razor, ShipmentForm.razor, LoadingButton.razor.

Testing: Use bUnit library for component testing.


## 9. From document to implementation

Carrier Integration microservice structure:
```
Zineps.CarrierIntegration/
==> Api/               # ASP.NET Web API
==> Core/              # Business logic (ICarrierIntegration)
==> Infrastructure/    # HTTP clients, DB
==> Tests/
```

Key classes:
```csharp
[ApiController]
public class CarrierController
{
    [HttpPost("{code}/shipments")]
    public async Task<ShipmentResult> Create(string code, ShipmentRequest req)
    {
        var carrier = _factory.CreateCarrier(code);
        return await carrier.CreateShipmentAsync(req);
    }
}
```

Where NOT to use CQRS/Event Sourcing:
- Simple CRUD (carrier config)
- Read-heavy, low complexity (rate lookups)
- When eventual consistency not acceptable (real-time tracking display)

Use it when: Read/write models differ significantly, need full audit trail (shipment lifecycle: created → labeled → delivered).


## 10. Operating at scale

Bottlenecks at 50M+ shipments/year:

1. Database writes (10K/hour peak)
   - Solution: Partition by date, Cosmos DB for high-write scenarios, write-behind cache

2. Carrier API rate limits
   - Solution: Rate limiter per carrier (token bucket), queue buffer, circuit breaker

3. Label storage (5TB)
   - Solution: Partition by year/month, Cool tier for old data, CDN

Observability:

// Metrics
carrier_request_duration_seconds{carrier, operation, status}
carrier_error_rate
circuit_breaker_state

// Alerts
- Error rate > 5% for 5min → Page on-call
- Circuit breaker open → Slack
- High severity discrepancies > 100 → Email finance


// Dashboards: 
Carrier health (success rate, latency), shipment funnel, financial impact.


## 11. Attention to detail & review process

Comments I'd leave on my code:
- net10.0 doesn't exist → change to net9.0  (WAIT - it does exist now!)
- Using "new" in Blazor → refactor to DI
- Extract HTTP logic to separate client
- Missing edge case tests
- Duplicated loading state logic

How I normally catch these:
- Run dotnet build locally
- Use code formatter
- Quick manual review

Under time pressure, I prioritized functionality over polish - but you're right, basic validation matters.

Production checklist:
- Code compiles, tests pass
- No typos, follows conventions
- Error handling, logging
- Security (input validation)
- Documentation updated


## 12. Working style & culture

First-principles example:

In my quantum thesis, everyone used a standard coupling mechanism. By questioning assumptions and breaking it down to physics fundamentals (energy levels, coupling strength), I realized a different geometry could work better. Result: 45% efficiency improvement.

In software: "Why microservices here? What problem does it solve?" vs. cargo-culting.

Failure in production:

My QuTech automation pipeline worked in tests but failed after 2 weeks in production - I'd tested with clean synthetic data, but real data had edge cases (missing timestamps).

Changed process:
1. Test with real, messy data
2. Add defensive checks and graceful degradation
3. Monitor assumptions (data quality metrics)

Lesson: Design for reality - data will be messy, APIs will fail, users will do unexpected things.


## Overall reflection:

This exercise highlighted gaps in practical .NET experience (DI wiring, HttpClient management, Blazor patterns) that I'm eager to close. However, I'm confident in systematic thinking and learning quickly - skills from complex quantum systems research.

I'd welcome discussing these in detail, especially where my understanding could deepen.
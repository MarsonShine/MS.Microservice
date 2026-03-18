# Execution Transcript

## Eval Prompt

Review this ASP.NET Core 10 minimal API diff like a senior C# reviewer. Use a checklist-based PR review, focus on meaningful issues only, and provide severity, evidence, and suggested fixes.

```diff
+ app.MapPost("/orders", async (CreateOrderRequest request, OrdersService service, CustomerApi customerApi, ILogger<Program> logger) =>
+ {
+     try
+     {
+         var customer = customerApi.GetAsync(request.CustomerId).Result;
+         var order = await service.CreateAsync(request, default);
+         logger.LogInformation($"Created order {order.Id} for {request.CustomerId}");
+         return Results.Ok(order);
+     }
+     catch (Exception ex)
+     {
+         return Results.BadRequest(ex.ToString());
+     }
+ });
```

## Final Output

# PR Review

## Findings

### 1. High — Sync-over-async call inside the request pipeline
**Evidence:**  
```csharp
var customer = customerApi.GetAsync(request.CustomerId).Result;
```

**Why it matters:**  
Blocking on `.Result` inside an async ASP.NET Core handler can tie up request threads, reduce throughput, and wrap failures in less useful exceptions. It also makes cancellation handling inconsistent.

**Suggested fix:**  
Await the call instead of blocking:

```csharp
var customer = await customerApi.GetAsync(request.CustomerId, cancellationToken);
```

---

### 2. High — Exception details are returned directly to the client
**Evidence:**  
```csharp
catch (Exception ex)
{
    return Results.BadRequest(ex.ToString());
}
```

**Why it matters:**  
`ex.ToString()` can expose stack traces and internal implementation details. Also, mapping every exception to `400 Bad Request` is incorrect for transient downstream failures, bugs, or server-side errors.

**Suggested fix:**  
Only catch expected validation/business exceptions and return safe client messages. Let unexpected exceptions flow to global exception handling, or return a sanitized `Problem(...)` response and log the error.

```csharp
catch (KnownValidationException ex)
{
    logger.LogWarning(ex, "Invalid order request for customer {CustomerId}", request.CustomerId);
    return Results.BadRequest(new { error = ex.Message });
}
```

---

### 3. Medium — Request cancellation is ignored
**Evidence:**  
```csharp
var order = await service.CreateAsync(request, default);
```

**Why it matters:**  
Passing `default` discards the request’s cancellation token, so work may continue after the client disconnects or the request is aborted.

**Suggested fix:**  
Accept a `CancellationToken` in the endpoint and pass it through to all async dependencies:

```csharp
app.MapPost("/orders", async (
    CreateOrderRequest request,
    OrdersService service,
    CustomerApi customerApi,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
{
    var customer = await customerApi.GetAsync(request.CustomerId, cancellationToken);
    var order = await service.CreateAsync(request, cancellationToken);
    ...
});
```

---

### 4. Medium — Successful create returns `200 OK` instead of `201 Created`
**Evidence:**  
```csharp
return Results.Ok(order);
```

**Why it matters:**  
For a POST that creates a new resource, `201 Created` is the more accurate contract and allows returning the new resource location.

**Suggested fix:**  
Return `Created`/`CreatedAtRoute` if the order is newly created:

```csharp
return Results.Created($"/orders/{order.Id}", order);
```

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

## Checklist
- ❌ Async usage and request flow
- ❌ Error handling / API contract
- ❌ Cancellation propagation
- ⚠️ Resource validation
- ⚠️ HTTP semantics

## Findings

### 1) High — Sync-over-async in request pipeline
**Evidence:** `customerApi.GetAsync(request.CustomerId).Result`  
Blocking on `.Result` inside an async endpoint can stall request threads and hurts scalability; it also wraps failures differently than `await`.  
**Suggested fix:** Make the call fully async and propagate cancellation:
```csharp
var customer = await customerApi.GetAsync(request.CustomerId, cancellationToken);
```

### 2) High — Broad catch leaks internals and misclassifies failures
**Evidence:** `catch (Exception ex) { return Results.BadRequest(ex.ToString()); }`  
This turns every failure into `400 Bad Request`, including server/service faults, and exposes stack traces/internal details to clients.  
**Suggested fix:** Catch only known client-caused exceptions and return sanitized `ProblemDetails`; let unexpected exceptions flow to centralized exception handling.

### 3) Medium — Cancellation is ignored
**Evidence:** `await service.CreateAsync(request, default);`  
Passing `default` prevents request aborts from cancelling downstream work.  
**Suggested fix:** Add `CancellationToken cancellationToken` to the handler and pass it to all async calls.

### 4) Medium — Customer lookup result is unused / validation unclear
**Evidence:** `var customer = ...;` is never used.  
If the lookup is meant to validate existence, that intent is implicit and fragile; if `GetAsync` can return null, orders may still be created incorrectly.  
**Suggested fix:** Explicitly validate the result and return `404`/`400` as appropriate.

### 5) Low — Create endpoint returns `200 OK`
**Evidence:** `return Results.Ok(order);`  
For a successful POST create, `201 Created` is usually the correct contract.  
**Suggested fix:** Return `Results.Created($"/orders/{order.Id}", order)`.

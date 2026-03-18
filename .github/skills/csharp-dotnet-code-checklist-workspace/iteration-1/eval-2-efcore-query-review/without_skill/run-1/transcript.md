# Execution Transcript

## Eval Prompt

Review this EF Core repository diff with a .NET 10 best-practices checklist. Focus on correctness, data access quality, and production impact.

```diff
 public sealed class OrderRepository(AppDbContext db) : IOrderRepository
 {
     public async Task<IReadOnlyList<Order>> GetOpenOrdersAsync(Guid customerId)
     {
-        return await db.Orders.Where(o => o.CustomerId == customerId && o.Status == OrderStatus.Open).ToListAsync();
+        var orders = await db.Orders.Include(o => o.Items).ToListAsync();
+        return orders.Where(o => o.CustomerId == customerId && o.Status == OrderStatus.Open).ToList();
     }
 
     public async Task CancelAsync(Guid id)
     {
         var order = await db.Orders.FirstAsync(o => o.Id == id);
         order.Status = OrderStatus.Cancelled;
         await db.SaveChangesAsync();
         await auditWriter.WriteAsync($"cancelled {id}");
     }
 }
```

## Final Output

## Findings

### High — `GetOpenOrdersAsync` now loads the entire orders table (and all items) before filtering
```csharp
var orders = await db.Orders.Include(o => o.Items).ToListAsync();
return orders.Where(o => o.CustomerId == customerId && o.Status == OrderStatus.Open).ToList();
```

This moves the `customerId` / `Status == Open` filter from SQL to in-memory LINQ. In production that means:

- full-table read of `Orders`
- eager load of **all** `Order.Items` for every order
- much higher DB/network/memory usage
- longer latency and possible timeouts under load
- other customers’ orders are materialized in process memory unnecessarily

This is a significant EF Core anti-pattern for repository reads. Keep filtering in the database, then include related data only for the filtered set, e.g. compose `Where(...)` before `ToListAsync()` (and likely `AsNoTracking()` for this read path).

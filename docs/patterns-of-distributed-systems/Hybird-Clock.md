

# 混合时钟（Hybrid Clock）

使用**系统时间戳和逻辑时间戳的组合来将日期-时间作为版本**，可以对其进行排序

## 问题

当 [Lamport Clock](Lamport-Clock.md) 被用作 [Versioned Value](Versioned-Value.md) 中的一个 version 时，客户端并不知道存储特定版本的实际日期-时间。对于客户端来说，使用日期-时间（如 01-01-2020）来访问 version，而不是使用整数（如1、2、3）是很有用的。

## 解决方案

[混合逻辑时钟(Hybrid Logical Clock)](https://cse.buffalo.edu/tech-reports/2014-04.pdf) 提供了一种方式，它有一个 version 是单调地增加，就像一个简单的整数，但是它还和实际的日期时间有关联。[mongodb](https://www.mongodb.com/blog/post/transactions-background-part-4-the-global-logical-clock) 或 [cockroachdb](https://www.cockroachlabs.com/docs/stable/architecture/transaction-layer.html) 等数据库在实践中使用了混合时钟。

一个混合逻辑时钟的实现方法如下：

```java
class HybridClock… 
  public class HybridClock {
      private final SystemClock systemClock;
      private HybridTimestamp latestTime;
      public HybridClock(SystemClock systemClock) {
          this.systemClock = systemClock;
          this.latestTime = new HybridTimestamp(systemClock.currentTimeMillis(), 0);
      }
```


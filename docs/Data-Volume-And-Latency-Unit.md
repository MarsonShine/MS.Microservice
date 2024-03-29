# 数据卷大小单位

| 幂   | 名称       | 简称 |
| ---- | ---------- | ---- |
| 10   | 千字节     | 1KB  |
| 20   | 兆字节     | 1MB  |
| 30   | 十亿字节   | 1GB  |
| 40   | 万亿字节   | 1TB  |
| 50   | 千万亿字节 | 1PB  |

# 延迟数单位

| 操作名称                                        | 时间                    |
| ----------------------------------------------- | ----------------------- |
| L1 缓存引用                                     | 0.5 ns                  |
| 分支错误预测                                    | 5 ns                    |
| L2 缓存引用                                     | 7 ns                    |
| 互斥锁/解锁                                     | 100 ns                  |
| 主存引用                                        | 100 ns                  |
| 通过 Zippy 压缩 1K 字节                         | 10,000 ns = 10 μs       |
| 在 1Gbps 的带宽下发送 2K 字节                   | 20,000 ns = 20 μs       |
| 从内存中顺序读取 1MB                            | 250,000 ns = 250 μs     |
| 同一数据中心内的往返                            | 500,000 ns = 500 μs     |
| 磁盘扫描                                        | 10,000,000 ns = 10 ms   |
| 从网络有序读取 1MB                              | 10,000,000 ns = 10 ms   |
| 从磁盘有序读取 1MB                              | 30,000,000 ns = 30 ms   |
| 跨网络分区发送数据包（CA -> Netherlands -> CA） | 150,000,000 ns = 150 ms |

$1 ns = 10^-9 s$
$1 µs= 10^-6 s = 1,000 ns$
$1 ms = 10^-3 s = 1,000 µs = 1,000,000 ns$

# 服务水平协议（SLA）

高可用性是指系统在可期望的长时间内保持连续运行的能力。高可用性通常用百分比来衡量，100% 表示服务没有任何停机时间。大多数服务的可用性在 99% 到 100% 之间。 服务水平协议（Service Level Agrement, SLA）是服务提供商常用的术语。这是您（服务提供商）和客户之间的协议，正式定义了您的服务将提供的正常运行时间水平。云服务提供商Amazon [4]、Google [5]和Microsoft [6]将其 SLA 设置在 99.9% 或以上。通常使用 "9" 的个数来衡量系统的可用性。9 的个数越多，系统可用性越好。如表2-3所示，九的个数与预期的系统停机时间相关。

| 可用性 % | 每天宕机的时间 | 每年宕机的时间 |
| -------- | -------------- | -------------- |
| 99%      | 14.40 分钟     | 3.65 天        |
| 99.9%    | 1.44 分钟      | 8.77 小时      |
| 99.99%   | 8.64 秒        | 52.60 分钟     |
| 99.999%  | 864 毫秒       | 5.26 分钟      |
| 99.9999% | 86.40 毫秒     | 31.56 秒       |

## 举例：估算 Twitter 的每秒查询量(QPS)和存储需求

请注意，以下数字仅用于此练习，不是来自 Twitter 的真实数字。

假设条件：

- 每月活跃用户 3 亿。 
- 50% 的用户每天使用 Twitter。 
- 用户平均发布 2 条推文。 
- 10% 的推文包含媒体。 
- 数据存储 5 年。

估算结果：

查询每秒估计值（QPS）：

- 每日活跃用户(DAU) = 3亿 * 50% = 1.5亿 
- 推文 QPS = 1.5亿 * 2条推文/ 24小时/ 3600秒 = ~3500 
- 峰值 QPS = 2 * QPS = ~7000

这里只估算媒体存储：

- 平均推文大小： 
- 推文ID 64 字节 
- 文本 140 字节 
- 媒体 1 MB 
- 媒体存储：1.5亿 * 2 * 10% * 1 MB = 每天 30 TB
- 5 年媒体存储：30 TB * 365 * 5 = ~55 PB
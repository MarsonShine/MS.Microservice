# 如何使用分布式锁

原文链接：https://martin.kleppmann.com/2016/02/08/how-to-do-distributed-locking.html

我在 [Redis](http://redis.io/) 网站上偶然的发现了一个被称为 [Redlock](http://redis.io/topics/distlock) 的算法。这个算法在 Redis 专题上宣称实现了可容错的分布式锁（或者又叫[租赁](https://pdfs.semanticscholar.org/a25e/ee836dbd2a5ae680f835309a484c9f39ae4e.pdf)），并在向正在使用分布式系统的用户请求反馈。这个算法本能地在我的脑海里敲起了警钟，所以我花时间想了一段时间并记了下来。

由于他们已经有了[超过 10 个关于 Redlock 的依赖实现](http://redis.io/topics/distlock)，我不知道谁准备好依赖这个算法，我认为这是值得公开分享我的笔记。我不会讲 Redis 的其他方面，其他一些方面在[其他地方](https://aphyr.com/tags/Redis)早就讨论过了。

在我深入 Redlock 细节之前，我要说下我是很喜欢 Redis 的，并且我过去曾成功的将它用于生产。我认为它能很好的适合某些场景，如果你想共享一些瞬时的，近似的，服务于服务之间的数据快速变化等。如果你因为一些原因丢失了相关数据也没什么大问题。举例来说，一个好的使用案例是维护每个 IP 地址的请求计数（为了限速目的）以及为设置每个用户 ID 不同的 IP 地址（为了检测滥用）。

然而，Redis 最近开始进军数据管理区域，它对强一致性以及持久性的期望越来越高 — — 这让我很担心，因为 Redis 并不是为此设计的。可论证的，分布式锁是这些领域的其中之一。让我们更仔细的研究细节吧。

## 你使用分布式锁是为了什么？

锁的目的就是在一系列的节点，它们可能尝试去做相同的工作，锁确保了最终只会执行一个（至少是同一时刻只执行一个）。这个工作可能会写一些数据到共享存储系统中，并执行一些计算，调用外部 API 等等。在高层，这里有两个理由来解释为什么在分布式系统中你可能会想要锁：[高效或正确性](http://research.google.com/archive/chubby.html)。为了区分这两个情况，你可以回答如果锁失败将会发生什么：

- 高效：用锁来避免一些不必要的多次做相同的工作（例如一些昂贵的计算开销）。如果锁失败了，那么两个节点最后就会做相同的工作，结果就是要略提高了开销（你最后要比其他方式多花费 5 美分给到 AWS）或者略微麻烦（比如一个用户最后会受到相同的通知两次）。
- 正确性：用锁来防止并发进程互相干扰，并且会破坏你的系统的当前状态。如果锁失败了，那么两节点间就会并发的工作在相同的数据上，结果就是破坏的文件，数据丢失，永久的不一致，就好比给病人用药量不对，或是其他严重的问题。

这两个情况都需要锁，但是你必须非常清晰这两个，哪一个是你要处理的。

我同意如果你为了提高效率为目的而正在使用锁，那它是不必要，用 Redlock 带来的开销和复杂性，有 5 个 Redis 服务器正在运行并且检查是否有多个人占用了你的锁。你最好的选择是只使用单个 Redis 实例，主服务器崩溃了就会使用异步复制到备份实例。

如果你使用了单个 Redis 实例，如果你的 Redis 节点突然断电，当然会释放一些锁，或会发生其他错误的事情。但是如果你只使用锁来当作一个效率的优化方案，并且不经常发生断电，那么这都不是什么大问题。这里说的 “不是大问题” 的场景恰恰是 Redis 的闪光点。至少如果你正在依赖单独的 Redis 实例，它是非常清楚对每个人来说系统的锁看起来都是近似的，仅用于非关键用途。

在另一方面，Redlock 算法，它使用了 5 个备份和多数投票，咋眼一看，它是非常适合你的锁对于正确性是非常重要的。我在下面几节中同意它是不适合这个目的的。文章剩下的部分，我将假设你的锁对于正确性来讲是非常重要为前提，如果两个节点之间并发，它们都会占有相同的锁，这是很严重的 bug。

## 使用锁保护资源

我们暂时先把 Redlock 的细节放在一边，来讨论下如何在通用情况下使用分布式锁（依赖于使用的锁算法细节）。要记住在分布式系统中的锁与多线程应用程序中的锁不同，这是很重要的。这是一个更复杂的问题，因为不同的节点和网络能以各种方式失败。

举个例子，假设你有一个应用程序，一个客户端要在共享存储系统中更新一个文件（如 HDFS 或 S3）。这个客户端首先会占有锁，然后读取文件，并做一些改变，写回到被修改的文件，最终释放搜索。这个锁会防止在并发执行读-修改-写回 这个周期的两个客户端，其中一个会丢失更新。代码看起来就像这样：

```js
// 坏代码
function writeData(filename, data) {
    var lock = lockService.acquireLock(filename);
    if(!lock) {
        throw 'Failed to acquire lock';
    }
    
    try {
        var file = storage.readFile(filename);
        var updated = updateContents(file, data);
        storage.writeFile(filename, updated);
    } finally {
        lock.release();
    }
}
```

很不辛，即使你有一个完美的锁住服务，上面的代码还是坏的。下面的图标展示了你最后的数据是怎么被破坏的：

![](asserts/unsafe-lock.png)

在这个，这个正在占据锁的客户端会因为其他因素而暂停一段时间 — — 比如发生了 GC。这时锁超时了（所以它是租赁），一般情况下这是好的方式（否则当发生崩溃的时候，这个占有锁的客户端就永远不会释放锁而发生死锁）。但是，如果 GC 暂停的时间要比租赁超期时间要长，那么客户端就不会意识到它的锁已经超时了，它就会继续往下执行并做一些不安全的改变。

这个问题不是理论上的：HBase 就曾有过[这个问题](http://www.slideshare.net/enissoz/hbase-and-hdfs-understanding-filesystem-usage)。通常，GC 发生引起的暂停是很短暂的，但是 “停止整个世界” 的 GC 有时候会要几分钟 — — 只要比超期时间长久足够了。甚至是被称为 “并发” 垃圾回收器（如 HotSpot JVM CMS）就不会在应用程序代码里面完全并行 — — 甚至是有时候不得不停止整个运行。

你不能通过在回写存储之前插入一个锁超期检查来试图修复这个问题。要记住，GC 能在任何时刻停止一个正在运行的线程，包括在你极不方便的时刻（在你检查和写操作之间）。

如果你觉得你的编程语言在运行时不会有长时间的 GC 而感到沾沾自喜，那么这里有很多其他的理由可能会让你的进程停歇。可能你的进程尝试读取一个地址，这个地址没有加载到内存中，所以它会得到一个页面错误并等待页面从磁盘加载。也许你的磁盘最终是 EBS，所以在 Amazon 拥挤的网络中读取一个变量，会不知觉转换成同步网络。这其中可能还会有很多其他进程会争抢 CPU，并且你会在[你的调度树中会碰见黑节点](https://twitter.com/aphyr/status/682077908953792512)。可能有一些会不小心地向进程发送 SIGSTOP。不管怎样，你的进程都将会停歇。

如果你仍然不相信我关于进程暂停的讨论，那么你可以想像一下在网络中，一个文件写请求在到达存储服务之前可能会发生延迟。如以太网和IP这样的网络包可以任意的延迟数据包，并且[的确这么做了](https://queue.acm.org/detail.cfm?id=2655736)：在 Github 上的一个[著名事件](https://github.com/blog/1364-downtime-last-saturday)中，网络中的数据包被延迟了接近 90 秒。这就是以为这一个应用程序进程可能会发送一个写请求，并且它可能会在当租赁时间已经过期之后的时候的一分钟里到达存储服务器。

甚至是在管理良好的网络中，这种情况也会发生。你不能简单的对于这种情况作出假设，这就是为什么上面的代码基本上是不安全的，无论是否你使用了锁服务。

## 使用栅栏（fencing）能让锁更安全

修复这个问题实际上有个很简单的方案：你需要对每个到存储服务的写请求都包含一个栅栏密钥。这样的话，一个栅栏密钥可以是简单的数字自增即可（如由锁服务自增）每次客户端占有锁时会每次就会自增栅栏令牌。下面的图标进行了分析：

![](asserts/fencing-tokens.png)

客户端1占据锁租赁并获得数字 33，但是它随即进入了长时间停歇的状态并超期。这个时候客户端 2 占据锁并获得数字 34（数字总是自增的），然后发送写操作到存储服务，包括令牌数字 34。之后，客户端 1 出现在周期内并同样包括令牌数字 33 发送写操作给存储服务器。但是存储服务器记住了它已经被更高的令牌数字（34）的进程修改过，所以它会取消这个 33 令牌的请求。

要注意，这要求存储服务器在检查令牌并拒绝之前的令牌任何写操作要扮演主动角色。一旦你知道窍门了，就不会显得特别困难。锁服务提供生成严格单一的自增长令牌，这会让锁安全。举个例子，如果你正在使用像 ZooKeeper 这样的锁服务，你可以使用像 `zxid` 或 znode 版本号这样的栅栏令牌，那么你的状态就是很好的。

现在让我们回到使用 Redlock 的第一个大问题：它没有任何生成栅栏令牌的功能。这个算法不会生成任何数字，保证每次客户端占有锁时自增长。这也就是说如果这个算法一旦不是完美，那么它使用起来就不是安全的，因为你不能在客户端之间，尤其是其中客户端发生停歇或网络包延迟阻止竞争。

它在我看来，怎么改变其中的 Redlock 算法来开始生成栅栏令牌是不容易的。它使用唯一的随机数没有提供要求的单调性。在一个 Redis 节点上简单的保持一个计数器还不够，因为这个节点有可能会失败。在一些节点上保持计数器就是说他们将要失去同步。它就像你将需要一个一致的算法生成栅栏令牌。（如果只是简单的增加计数器就好了）

## 利用时间来解决一致

在依赖于锁的正确性的情况下，Redlock 不能生成栅栏令牌事实上早就有足够的理由不再使用它。但还有一些其他问题也值得讨论。

在学术文献中，对于这种算法最实用的系统模型是[不可靠故障检测器的异步模型](http://courses.csail.mit.edu/6.852/08/papers/CT96-JACM.pdf)。进一步解释就是说，这个算法对时间没有任何假设：进程也能会暂停任意长度时间、在网络中数据包也可能会任意地延迟、时钟也可能会任意出错 — — 这个算法永远不会期望会去做对的事。根据上面我们讨论的，这些都是非常合理的假设。

这个算法的唯一目的是使用时钟来生成超时，来避免如果某个节点出错无止境等待。但是超时不一定要精确：因为只是一个请求超时了，不意味着其他节点同样会出错 — — 也可能是网络中有很大的延迟，或者是你本地始终是错的。当使用故障检测时，超时只是猜测有事情是错的。（如果他们可以，分布式算法 完全不需要时钟，但是这样就[不能达成一致了](https://www.cs.princeton.edu/courses/archive/fall07/cos518/papers/flp.pdf)）。占据锁就像是 "比较-赋值" 操作，它是[要求一致](https://cs.brown.edu/~mph/Herlihy91/p124-herlihy.pdf)的。

要注意 Redis 使用了 [gettimeofday](https://github.com/antirez/redis/blob/edd4d555df57dc84265fdfb4ef59a4678832f6da/src/server.c#L390-L404) ，而不是 [monotonic 时钟](https://linux.die.net/man/2/clock_gettime)来执行[键的超期](https://github.com/antirez/redis/blob/f0b168e8944af41c4161249040f01ece227cfc0c/src/db.c#L933-L959)。`gettimeofday`的手册[明确说](https://linux.die.net/man/2/gettimeofday)这个返回的时间受制于系统时间的不间断的跳跃 — — 那就是说，它可能在几分钟只有突然的跳到面前，或甚至是在某个时间内跳回来（如果时钟是通过 NTP 步进的，因为不同于 NTP 服务器，差异很大，或者时钟是由管理员手动调整的）。因此，如果系统时间正在做太多的事，那它在 Redis 中就很容易发生键要比预期很快过期的情况。

在异步模型中，这个算法并不是大问题：这些算法通常不需要做出任何假设，来确保他们的安全属性。只有活性属性依赖于超时或一些其他故障检测器。进一步解释就是只有当系统时间到处都是时（进程停歇、网络延迟、时钟来回跳跃）这个算法的性能也许会下降，但是这个算法绝不做不正确的事情。

然而，Redlock 不是这样的。它的安全性依赖于很多的时间假设：它假设所有的 Redis 节点在超期之前都近似的占有密钥；网络延迟与失效时间相比很少；进程停歇要远比超期时间短。

## 用糟糕的时间来破坏 Redlock

我们看到 Redlock 依赖于时间假设的一些例子。系统有 5 个 Redis 节点（A，B，C，D 和 E）、两个客户端（1 和2）。如果其中一个 Redis 节点的时钟向前跳跃会发生什么呢？

1. 客户端 1 在节点 A，B，C 上占有锁，由于网络问题，D 和 E 不能如期到达。
2. 在节点 C 的时钟向前跳跃，导致锁超期。
3. 客户端 2 在节点 C，D，E 获取锁。由于网络问题，A 和 B 不能如期到达
4. 客户端 1 和 2 现在都相信他们已经占据锁

如果 C 节点在持久化时钟到磁盘之前停机了，并且立即重启，也会发生类似的问题。因此，Redlock 文档[建议延迟重启](http://redis.io/topics/distlock#performance-crash-recovery-and-fsync)崩溃的节点的时间至少要达到最长锁的生成时间。但是这样延迟重启就再次依赖于合理计算出这个时间，并且在如果时钟跳跃，就会失败。

OK，也许你认为时钟跳跃是不切实际的，因为你对配置 NTP 非常自信，他只会让时钟不停的转动。在这个例子中，我们来这么一个例子，进程停歇如何导致算法失败：

1. 客户端 1 在节点 A，B，C，D，E 请求占有锁
2. 当客户端 1 正在运行中，它进入了停止一切的 GC
3. 锁在所有的 Redis 节点超期了
4. 客户端 2 在节点 A，B，C，D，E 请求占有锁
5. 客户端 1 完成 GC 并接受从 Redis 节点的表示成功占有锁的响应（当进程发生停歇的时候，它们保存在客户端 1 的内核网络缓冲区）
6. 客户端 1 和 2 现在都相信它们自己占有了锁

注意，尽管通过 Redis 使用C语言编写的，是不会发生 GC 的，这对我们没有帮助：在客户端的任何系统可能因 GC 暂停，都有这个问题。只有防止客户端1 在客户端 2 已经获取锁之后执行任何操作才能使它安全，就像上面使用栅栏方法的例子一样。

网时间的网络延迟能导致进程暂停相同的效果。它也许依赖于你的 TCP 用户超时 — — 如果你能设置超时要比 Redis TTL 要短，这样网络延迟的数据也许可以忽略，但是我们必须查看 TCP 的实现细节才能确定。因此，随着超时，我们又再次的回来计算时间的合理的准确性上来了。

## Redlock 的同步假设

这些例子展示了只有在你假设是一个同步系统模型， Redlock 才会正确工作 — — 那就是说，一个系统必须要一下属性：

- 边界网络延迟（你能保证数据包延迟时间内总是到达的）
- 边界进程暂停（换句话说，强实时约束，通常就是你只能在汽车安全气囊内找得到）
- 边界时钟错误（祈祷（cross）你不会从出错的 NTP 服务器获得你的时间）

注意，一个同步模型并不意味着完全同步时钟：意思是说你可以假设在一个已知的、修复了上限的网络延迟、暂停和时钟漂移。Redlock 假设延迟、暂停和漂移都是相对与锁的生存来说很小。如果时间问题变得和生存时间一样大，那么这个算法就会失败。

在一个行为良好的数据中心环境里，时间假设在大多数时间里都是令人满意的 — — 这在通常的同步系统中是已知的。但是这样就足够好了么？只要时间假设错了，Redlock 就会违反安全属性，例如在一个客户端的租赁到期之前给另一个客户端。如果你依赖于锁的正确性，“大多数时候” 这种是完全不够的 — — 你需要它永远都是堆的。

这里有大量的证据证明它在大多数实际的系统中采用同步系统模型是不安全的。你要牢记 Github 的[90秒包延迟事件](https://github.com/blog/1364-downtime-last-saturday)。Redlock 不太像 [Jepsen](https://aphyr.com/tags/jepsen) 那样好测试。

在另一方面，通常在一个同步系统模型（或者带故障检测的异步模型）设计一个一致的算法实际上有工作的机会。 Raft, Viewstamped Replication, Zab 和 Paxos 都属于这个类。只要算法脱离所有的时间假设。这很难：人们很容易就去假定网络、进程和时钟都是非常可靠的。但是在分布式系统中这个可靠性是混乱的，你必须要非常小心你的假设。

## 总结

我认为 Redlock 算法是个糟糕的选择，因为它“既不是鱼也不是鸟”：它为了有效的优化锁是没必要的，它是重量级和昂贵的，但是对于依赖锁的正确性的情况来看，它不是够安全的。

通常，关于对时间和系统时钟做出假设的算法是很危险的（本质上就是假设一个同步系统用边界网络延迟和操作执行的边界时间），如果它不满足这些假设，它就违反了安全特性。此外，它（Redis）还缺乏生成栅栏令牌的功能（令牌保护了在长时间的网络延迟或进程进入停歇时候保护系统二次执行）。

如果你只需要在最大努力的基础上使用锁（作为一个有效优化，而不是正确性），我建议对 Redis 使用[简单的单节点锁定算法](http://redis.io/commands/set)（条件是如果不存在才会赋值，即获得一个锁，这是原子操作，如果值匹配存在即删除，这就是释放锁），文档很清晰，在你的代码中的锁是只是近似的，也有可能会失败。不要费心设置一个 5 个 Redis 节点的集群。

另一方面，如果你是为了正确性需要锁，那么请不要使用 Redlock。而是应该使用更合适的一致系统如 [Zookeeper](https://zookeeper.apache.org/)，也许是通过一个 [Curator recipes](https://curator.apache.org/curator-recipes/index.html) 来实现一个锁。（最起码，使用具有[合理事务保证的数据库](https://www.postgresql.org/)）以及请在锁下所有的资源请强制使用栅栏令牌。

如果你要了解更多，这个专题在[我书的第八章和第九章](http://dataintensive.net/)有我更详细的解释，现在可以从图灵的早期版本中获得。（上面的图标就是取自我的书）对于如何使用 Zookeeper，我推荐   [Junqueira and Reed’s book](http://shop.oreilly.com/product/0636920028901.do) 。为了更好的介绍分布式系统理论，我推荐 [Cachin, Guerraoui and Rodrigues’ textbook](http://www.distributedprogramming.net/) 。

 感谢[Kyle Kingsbury](https://aphyr.com/)、[Camille Fournier](https://twitter.com/skamille)、[Flavio Junqueira](https://twitter.com/fpjunqueira)和[Salvatore Sanfilippo](http://antirez.com/)审阅本文的草稿 。当然，任何错误都是我的。

 **2016年2月9日更新:**Redlock 的原作者[Salvatore](http://antirez.com/)对本文提出了反驳(参见[HN讨论](https://news.ycombinator.com/item?id=11065933))。他说了一些好观点，但我坚持我的结论。如果我有时间，我可能会在后续的文章中详细阐述，但请形成您自己的观点——并请参考下面的参考文献，其中许多都经过了严格的学术同行评审(不像我们的博客文章)。 

## 参考资料

[1] Cary G Gray and David R Cheriton: “[Leases: An Efficient Fault-Tolerant Mechanism for Distributed File Cache Consistency](https://pdfs.semanticscholar.org/a25e/ee836dbd2a5ae680f835309a484c9f39ae4e.pdf),” at *12th ACM Symposium on Operating Systems Principles* (SOSP), December 1989. [doi:10.1145/74850.74870](https://dx.doi.org/10.1145/74850.74870)

[2] Mike Burrows: “[The Chubby lock service for loosely-coupled distributed systems](http://research.google.com/archive/chubby.html),” at *7th USENIX Symposium on Operating System Design and Implementation* (OSDI), November 2006.

[3] Flavio P Junqueira and Benjamin Reed: [*ZooKeeper: Distributed Process Coordination*](http://shop.oreilly.com/product/0636920028901.do). O’Reilly Media, November 2013. ISBN: 978-1-4493-6130-3

[4] Enis Söztutar: “[HBase and HDFS: Understanding filesystem usage in HBase](http://www.slideshare.net/enissoz/hbase-and-hdfs-understanding-filesystem-usage),” at *HBaseCon*, June 2013.

[5] Todd Lipcon: “[Avoiding Full GCs in Apache HBase with MemStore-Local Allocation Buffers: Part 1](http://blog.cloudera.com/blog/2011/02/avoiding-full-gcs-in-hbase-with-memstore-local-allocation-buffers-part-1/),” blog.cloudera.com, 24 February 2011.

[6] Martin Thompson: “[Java Garbage Collection Distilled](https://mechanical-sympathy.blogspot.co.uk/2013/07/java-garbage-collection-distilled.html),” mechanical-sympathy.blogspot.co.uk, 16 July 2013.

[7] Peter Bailis and Kyle Kingsbury: “[The Network is Reliable](https://queue.acm.org/detail.cfm?id=2655736),” *ACM Queue*, volume 12, number 7, July 2014. [doi:10.1145/2639988.2639988](https://dx.doi.org/10.1145/2639988.2639988)

[8] Mark Imbriaco: “[Downtime last Saturday](https://github.com/blog/1364-downtime-last-saturday),” github.com, 26 December 2012.

[9] Tushar Deepak Chandra and Sam Toueg: “[Unreliable Failure Detectors for Reliable Distributed Systems](http://courses.csail.mit.edu/6.852/08/papers/CT96-JACM.pdf),” *Journal of the ACM*, volume 43, number 2, pages 225–267, March 1996. [doi:10.1145/226643.226647](https://dx.doi.org/10.1145/226643.226647)

[10] Michael J Fischer, Nancy Lynch, and Michael S Paterson: “[Impossibility of Distributed Consensus with One Faulty Process](https://www.cs.princeton.edu/courses/archive/fall07/cos518/papers/flp.pdf),” *Journal of the ACM*, volume 32, number 2, pages 374–382, April 1985. [doi:10.1145/3149.214121](https://dx.doi.org/10.1145/3149.214121)

[11] Maurice P Herlihy: “[Wait-Free Synchronization](https://cs.brown.edu/~mph/Herlihy91/p124-herlihy.pdf),” *ACM Transactions on Programming Languages and Systems*, volume 13, number 1, pages 124–149, January 1991.[doi:10.1145/114005.102808](https://dx.doi.org/10.1145/114005.102808)

[12] Cynthia Dwork, Nancy Lynch, and Larry Stockmeyer: “[Consensus in the Presence of Partial Synchrony](http://www.net.t-labs.tu-berlin.de/~petr/ADC-07/papers/DLS88.pdf),” *Journal of the ACM*, volume 35, number 2, pages 288–323, April 1988. [doi:10.1145/42282.42283](https://dx.doi.org/10.1145/42282.42283)

[13] Christian Cachin, Rachid Guerraoui, and Luís Rodrigues: [*Introduction to Reliable and Secure Distributed Programming*](http://www.distributedprogramming.net/), Second Edition. Springer, February 2011. ISBN: 978-3-642-15259-7, [doi:10.1007/978-3-642-15260-3](https://dx.doi.org/10.1007/978-3-642-15260-3)







在线中文书籍：https://vonng.gitbooks.io/ddia-cn/content/
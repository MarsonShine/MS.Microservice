# 最终一致性

最终一致性是一个一致性模版，使用在分布式计算中，来完成高可用的目的，如果给定一个数据项没有改变，最终关于它的访问都会返回最新的值。最终一致性也被称为乐观复制（optimistic replication），被广泛应用在分布式系统中，并且它起源于早期移动计算项目。一个系统要想达到最终一致性或乐观复制，那么聚合（converged）这个词尝尝被提起。最终一致性是一个弱保证 —— 最强大的模型，像[线性一致性](https://en.wikipedia.org/wiki/Linearizability)是最终一致性的，但是一个系统仅仅只是最终一致性是无法完全满足那些强约束的。

与传统的 ACID（原子性（Atomicity），一致性（consistency），隔离性（Isolation），持久性（Durability）） 相比，最终一致性的服务经常被归类位 BASE（基本可用（Basically Available），软状态（Soft state），最终一致性（Eventual consistency））语义。在化学中，碱是酸的反义词，这能帮助记忆。根据一些资料，这些是每个术语的基本定义：

- 基本可用性（Basically Available）：基本的读和写操作尽可能是可用的（使用所有数据库集群节点），但是不保证任何的一致性（在解决冲突之后写操作可能不会持续，读操作则可能不会获取最新写的值）
- 软状态（Soft state）：在没有一致性的要求下，过一段时间，我们只能知道一些状态的概率，因为它可能还没聚集。
- 最终一致性（Eventual consistency）：如果系统正常运行，我们在给定一组输入后长时间等待，我们最终将会知道数据库的状态是什么，因此任何读操作将会是与我们期望是一致的。

最终一致性有时候被批评给分布式软件应用程序增加了复杂性。部分原因是最终一致性是纯粹的活性保证（读操作最终会返回相同值）以及它不是安全的保证：一个最终一致性系统在它聚集之前是能返回任何值的。

## 冲入解决

为了确保副本聚集，系统必须协调分布式数据之间副本的差异。这里有两个原因：

- 在服务器之间版本交换或数据更新（经常被称为 anti-entropy（反熵））
- 当发生并发更新时，选择一个合适的最终状态，这称为和解（reconciliation）

最合适的方法是和解应用程序的依赖。通用的方法是 “最新的写胜出”。另一个方法是调用用户指定冲突处理程序。时间戳和时钟向量经常用在保护并发更新之间。一些人使用 “第一个写胜出” 这个场景下 “最后写胜出” 这个是不能被接受的。

并发写的和解必须要下一次读之前共同发生，并且能被调度在不同的时刻中：

- 读修复：当一个读操作发现非一致性问题进行过更正。这回降低读操作的速度。
- 写修复：当一个写操作期间，如果发现了非一致性问题，会进行修正，这同样会降低写操作速度。
- 异步修复：这个修复不是读或写操作的一部分

最终强一致性

然而，最终一致性只是一个活性保证（更新最终会被知道），最终强一致性（SEC）添加了安全保证，在两节点间接受了一组相同更新，状态相同。此外，如果系统是无变化的，应用程序将不会回滚。无冲突复制数据类型是确保最终强一致性的通用方法。

> 熵是衡量某个体系中事物混乱程度的一个指标，是从热力学第二定律借鉴过来的。

[最终一致性]: https://en.wikipedia.org/wiki/Eventual_consistency


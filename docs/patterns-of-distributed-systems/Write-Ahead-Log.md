# Write-Ahead Log

描述：提供持久化的保证，通过将每个状态更改作为命令追加式持久化到日志中，而不需要将存储数据结构刷新到磁盘。

## 问题

即使是服务器机器存储数据失败的情况下也需要持久化的强力保证。一旦服务器同意执行一个操作，那它就应该这么做，甚至是在它失败以及重启丢失了所有内存状态。

## 解决方案

![](../asserts/wal.png)

​																	图1：Write-Ahead Log

存储每个状态作为命令以文件的形式存储在硬盘中。为按顺序追加的每个服务器进程维护一个日志。单个日志它是有序追加的，它简化了在重启和随后的在线操作的日志处理（当日志以新命令追加时）。每个日志条目都给定一个唯一标识符。这个唯一的日志标识有助于在一些其它日志像[分段日志](https://martinfowler.com/articles/patterns-of-distributed-systems/log-segmentation.html)操作或使用[低水位线标记](https://martinfowler.com/articles/patterns-of-distributed-systems/low-watermark.html)的日志清除。日志更新能通过使用[单更新队列](https://martinfowler.com/articles/patterns-of-distributed-systems/singular-update-queue.html)实现。

指定的日志条目数据结构如下面代码

```java
public WALEntry...
  private final Long entryId;
  private final byte[] data;
  private final EntryType entryType;
  private long timeStamp;
```

这个文件能在每次重启时读取以及通过重播所有的日志条目来状态恢复。

思考下面一个简单的内存式 K/V 存储例子：

```java
class KVStore… 
  private Map<String, String> kv = new HashMap<>();

  public String get(String key) {
      return kv.get(key);
  }

  public void put(String key, String value) {
      appendLog(key, value);
      kv.put(key, value);
  }

  private Long appendLog(String key, String value) {
      return wal.writeEntry(new SetValueCommand(key, value).serialize());
  }
```

put 操作是以[命令](http://www.amazon.com/exec/obidos/tg/detail/-/0201633612)的形式体现的，在更新内存中的 hashmap 之前，它被序列化以及存储到日志。

```java
class SetValueCommand… 
  final String key;
  final String value;

  public SetValueCommand(String key, String value) {
      this.key = key;
      this.value = value;
  }

  @Override
  public byte[] serialize() {
      try {
          var baos = new ByteArrayOutputStream();
          var dataInputStream = new DataOutputStream(baos);
          dataInputStream.writeInt(Command.SetValueType);
          dataInputStream.writeUTF(key);
          dataInputStream.writeUTF(value);
          return baos.toByteArray();

      } catch (IOException e) {
          throw new RuntimeException(e);
      }
  }

  public static SetValueCommand deserialize(InputStream is) {
      try {
          DataInputStream dataInputStream = new DataInputStream(is);
          return new SetValueCommand(dataInputStream.readUTF(), dataInputStream.readUTF());
      } catch (IOException e) {
          throw new RuntimeException(e);
      }
  }
```

这确保了一旦 put 方法成功返回，甚至是进程占有的 KVStore 崩溃了，它的状态也能通过在启动时读取日志文件恢复。

```java
class KVStore… 
  public KVStore(Config config) {
      this.config = config;
      this.wal = WriteAheadLog.openWAL(config);
      this.applyLog();
  }

  public void applyLog() {
      List<WALEntry> walEntries = wal.readAll();
      applyEntries(walEntries);
  }

  private void applyEntries(List<WALEntry> walEntries) {
      for (WALEntry walEntry : walEntries) {
          Command command = deserialize(walEntry);
          if (command instanceof SetValueCommand) {
              SetValueCommand setValueCommand = (SetValueCommand)command;
              kv.put(setValueCommand.key, setValueCommand.value);
          }
      }
  }

  public void initialiseFromSnapshot(SnapShot snapShot) {
      kv.putAll(snapShot.deserializeState());
  }
```

## 一致性实现

在实现日志时，有一些重要的考虑事项。确保日志条目写道日志文件，最终是持久化到物理媒介（磁盘）上。在所有编程语言里提供的文件处理库都提供了一个机制，来强制操作系统将文件更改 'flush' 到物理媒介。当使用 flush 机制时，这里有一些因素要注意。

刷新每个日志写入到磁盘给了一个强有力的持久化保证（这是将日志放置首位的主要目的），但是这个服务限制了性能，并且会很快成为性能瓶颈点。如果刷新过程时缓慢的或者是异步完成的，它能提高性能但是这里就会存在丢失日志条目的风险，如果条目被刷新之前服务器崩溃的话。大多数实现技术方式是像批量（Batching）的操作来限制刷新操作的影响。

其它的要考虑的地方就是确保在读取日志时，能检测到损坏的日志。为了处理这种，日志条目通常会与 CRC 记录一起写入，然后它能在读取文件时验证。

单个日志文件会成为很难管理以及很快会消耗掉所有内存。为此，像[分段日志](https://martinfowler.com/articles/patterns-of-distributed-systems/log-segmentation.html)和[低水位线标记](https://martinfowler.com/articles/patterns-of-distributed-systems/low-watermark.html)技术就由此运用。

write ahead log 是只追加日志。正因为这种行为，在客户端通信失败和重试情况下，日志可以包含重复条目。当应用日志条目时，它需要确保要忽略重复。如果最终的状态类似 HashMap，它更新了相同的 key 的行为是幂等的，则不需要特别的机制。如果不是幂等的，那么这里就还需要一些机制来实现标记每个请求是否是重复的（如唯一请求标识符）。

## 例子

- 所有一致性算法的日志实现如 [Zookeeper](https://github.com/apache/zookeeper/blob/master/zookeeper-server/src/main/java/org/apache/zookeeper/server/persistence/FileTxnLog.java) 和 [RAFT](https://github.com/etcd-io/etcd/blob/master/wal/wal.go) 写日志都是类似的。
- 在 Kafka 中的存储实现就是遵循类似于数据库中的日志提交。
- 所有的数据库，包括 nosql 数据如 Cassandra 使用的 [write ahead log 技术](https://github.com/facebookarchive/cassandra/blob/master/src/org/apache/cassandra/db/CommitLog.java)保证了持久化



原文：https://martinfowler.com/articles/patterns-of-distributed-systems/wal.html
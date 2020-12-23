# 低水位标记(Low-Water Mark)

一个 write-ahead log 的索引，它代表的是日志的哪一部分能丢弃

## 问题

write ahead log 每天更新维护到持久化存储。他可以随着时间无线增长。[分段日志](https://martinfowler.com/articles/patterns-of-distributed-systems/log-segmentation.html)可以一次处理很小的文件，但是如果不检查的话，总磁盘内存也会无限增长。

## 解决方案

有一个机制告诉日志哪一部分可以安全的丢弃掉。这个机制给定一个最低偏移量或低水位标记，它指出了哪里可以丢弃。在后台作业有一个任务运行，在单独的线程里，它会一直检查日志可以被丢弃的部分以及根据条件在磁盘删除它。

```java
this.logCleaner = newLogCleaner(config);
this.logCleaner.startup();
```

日志清理器可以作为一个调度任务实现

```java
public void startup() {
    scheduleLogCleaning();
}

private void scheduleLogCleaning() {
    singleThreadedExecutor.schedule(() -> {
        cleanLogs();
    }, config.getCleanTaskIntervalMs(), TimeUnit.MILLISECONDS);
}
```

## 基于快照的 Low-Water Mark 

像 Zookeeper 或 etcd（定义在 RAFT） 这种大多数一致性实现中，实现了快照机制。在这个实现里，存储引擎会定期的获取快照。除了快照，它还会存储它成功应用的日志索引。参照（referring to）Write-Ahead Log 模式中的简单的键值存储实现中，就像下面：

```java
public SnapShot takeSnapshot() {
    Long snapShotTakenAtLogIndex = wal.getLastLogEntryId();
    return new SnapShot(serializeState(kv), snapShotTakenAtLogIndex);
}
```

一旦快照成功的持久化到了磁盘，日志管理器就会给定一个 low water mark 来表明丢弃的旧日志。

```java
List<WALSegment> getSegmentsBefore(Long snapshotIndex) {
    List<WALSegment> markedForDeletion = new ArrayList<>();
    List<WALSegment> sortedSavedSegments = wal.sortedSavedSegments;
    for (WALSegment sortedSavedSegment : sortedSavedSegments) {
        if (sortedSavedSegment.getLastLogEntryId() < snapshotIndex) {
            markedForDeletion.add(sortedSavedSegment);
        }
    }
    return markedForDeletion;
}
```

## 基于时间的 Low-Water Mark 

在有些系统中，使用日志来记录更新系统的状态是不必要的，日志可以在给定一个时间窗口后丢弃掉，不需要等待其它的子系统共享它能删除的最低日志索引。例如，在像 Kafka 的系统中，日志主要维护 7 周；所有的日志一旦超过了 7 周，就会被丢弃。对于这种实现，每个日志条目都包含了创建日志时的时间戳。日志清理器就能检查每个日志分段的最后日志条目，并把那些超过（老）配置的时间窗口的日志片段丢弃掉。

```java
private List<WALSegment> getSegmentsPast(Long logMaxDurationMs) {
    long now = System.currentTimeMillis();
    List<WALSegment> markedForDeletion = new ArrayList<>();
    List<WALSegment> sortedSavedSegments = wal.sortedSavedSegments;
    for (WALSegment sortedSavedSegment : sortedSavedSegments) {
        if (timeElaspedSince(now, sortedSavedSegment.getLastLogEntryTimestamp()) > logMaxDurationMs) {
            markedForDeletion.add(sortedSavedSegment);
        }
    }
    return markedForDeletion;
}

private long timeElaspedSince(long now, long lastLogEntryTimestamp) {
    return now - lastLogEntryTimestamp;
}
```

## 例子

- 这种日志算法的在所有的一致性算法中像 [Zookeeper](https://github.com/apache/zookeeper/blob/master/zookeeper-server/src/main/java/org/apache/zookeeper/server/persistence/FileTxnLog.java) 和 [RAFT](https://github.com/etcd-io/etcd/blob/master/wal/wal.go) 实现了基于快照的日志清理
- 在 [Kafka](https://github.com/axbaretto/kafka/blob/master/core/src/main/scala/kafka/log/Log.scala) 的存储实现就是遵于基于时间的日志清理
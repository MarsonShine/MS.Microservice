# 分段日志(Segmented Log)

描述：分割日志文件为小的文件替代，而不是为了简单只用一个单个大日志文件。

## 问题

当在启动时读日志的时候，单个日志文件随着时间会越来越大，成为性能瓶颈。旧日志要定时清理，要在单个大日志文件中清楚处理是非常困难的。

## 解决方案

把单个大日志文件拆分为多个小文件。日志文件在指定的大小操作

```java
public Long writeEntry(WALEntry entry) {
    maybeRoll();
    return openSegment.writeEntry(entry);
}

private void maybeRoll() {
    if (openSegment.size() >= config.getMaxLogSize()) {
        openSegment.flush();
        sortedSavedSegments.add(openSegment);
        long lastId = openSegment.getLastLogEntryId();
        openSegment = WALSegment.open(lastId, config.getWalDir());
    }
}
```

使用日志分段，这里需要有个容易的方式来将日志逻辑偏移（或者日志序列号）映射到日志分段文件。下面两种方式可以实现：

- 通过相同的前缀和偏移量（或是日志序列号）生成每个分段日志名称。
- 每个日志序列号分隔为两个部分，文件的文件和事务偏移量

```java
public static String createFileName(Long startIndex) {
    return logPrefix + "_" + startIndex + logSuffix;
}

public static Long getBaseOffsetFromFileName(String fileName) {
    String[] nameAndSuffix = fileName.split(logSuffix);
    String[] prefixAndOffset = nameAndSuffix[0].split("_");
    if (prefixAndOffset[0].equals(logPrefix))
        return Long.parseLong(prefixAndOffset[1]);

    return -1l;
}
```

通过这些信息，一个读操作分为两步。给定一个偏移量（或是事务 id），标识分段日志以及都能从随后的分段日志中读取所有的日志记录。

```java
public List<WALEntry> readFrom(Long startIndex) {
    List<WALSegment> segments = getAllSegmentsContainingLogGreaterThan(startIndex);
    return readWalEntriesFrom(startIndex, segments);
}
private List<WALSegment> getAllSegmentsContainingLogGreaterThan(Long startIndex) {
    List<WALSegment> segments = new ArrayList<>();
    //Start from the last segment to the first segment with starting offset less than startIndex
    //This will get all the segments which have log entries more than the startIndex
    for (int i = sortedSavedSegments.size() - 1; i >= 0; i--) {
        WALSegment walSegment = sortedSavedSegments.get(i);
        segments.add(walSegment);

        if (walSegment.getBaseOffset() <= startIndex) {
            break; // break for the first segment with baseoffset less than startIndex
        }
    }

    if (openSegment.getBaseOffset() <= startIndex) {
        segments.add(openSegment);
    }

    return segments;
}
```



## 例子

- 所有的一致性算法如 Raft 和 Zookeeper 的实现中就用到了分段日志。
- Kafka 的存储实现就按照分段日志
- 所有的数据库，包括 nosql 如 Cassandra 使用基于每个配置好的日志尺寸策略的 roll 

原文连接：https://martinfowler.com/articles/patterns-of-distributed-systems/log-segmentation.html


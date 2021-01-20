# 状态监听（State Watch）

当服务器上某个特定的值发生改变时会通知客户端。

## 问题

客户端对服务器上某个特定的值的变化很感兴趣。对于客户端结构逻辑来说，如果它们要不断循环拉取服务器上的这个值的变化无疑是较难的。如果客户端为了监控这个值的变化而打开了太多的连接，那它会导致服务器过载。

## 解决方案

允许客户端在服务器上注册它们自己感兴趣的值状态的变化。服务器在改变值得时候通知那些感兴趣的客户端。客户端与服务器维护了一个[单 Socket 通道](Single-Socket-Channel.md)。服务器会发送一个状态变化通知到这个通道上。客户端可能对多个值有兴趣，但是每一个监听都要一个连接那就会使服务器过载。所以客户端可以用[请求管道](Request-Pipeline.md)。

考虑一个在[一致性核心](Consisten-Core.md)中使用简单的键值存储的例子：当一个特定的键值发生改变或移除的时候客户端对此感兴趣。这里有两部分要实现，一个是客户端实现，另一个是在服务端实现。

## 客户端实现

客户端接收一个键的函数，并且这个函数能在它从服务器获得监听事件时触发调用。客户端存储函数对象以供后面调用。然后它会发送请求注册监听到服务器上。

```java
ConcurrentHashMap<String, Consumer<WatchEvent>> watches = new ConcurrentHashMap<>();

public void watch(String key, Consumer<WatchEvent> consumer) {
    watches.put(key, consumer);
    sendWatchRequest(key);
}

private void sendWatchRequest(String key) {
    requestSendingQueue.submit(new RequestOrResponse(RequestId.WatchRequest.getId(),
            JsonSerDes.serialize(new WatchRequest(key)),
            correlationId.getAndIncrement()));
}
```

当监听事件在连接上接收了，将调用相关的消费者

```java
this.pipelinedConnection = new PipelinedConnection(address, requestTimeoutMs, (r) -> {
    logger.info("Received response on the pipelined connection " + r);
    if (r.getRequestId() == RequestId.WatchRequest.getId()) {
        WatchEvent watchEvent = JsonSerDes.deserialize(r.getMessageBodyJson(), WatchEvent.class);
        Consumer<WatchEvent> watchEventConsumer = getConsumer(watchEvent.getKey());
        watchEventConsumer.accept(watchEvent);
        lastWatchedEventIndex = watchEvent.getIndex(); //capture last watched index, in case of connection failure.
    }
    completeRequestFutures(r);
});
```

## 服务端实现

当服务器接收到一个监听注册请求时，它将保留接收请求的管道连接映射和键。

```java
private Map<String, ClientConnection> watches = new HashMap<>();
private Map<ClientConnection, List<String>> connection2WatchKeys = new HashMap<>();

public void watch(String key, ClientConnection clientConnection) {
    logger.info("Setting watch for " + key);
    addWatch(key, clientConnection);
}

private synchronized void addWatch(String key, ClientConnection clientConnection) {
    mapWatchKey2Connection(key, clientConnection);
    watches.put(key, clientConnection);
}

private void mapWatchKey2Connection(String key, ClientConnection clientConnection) {
    List<String> keys = connection2WatchKeys.get(clientConnection);
    if (keys == null) {
        keys = new ArrayList<>();
        connection2WatchKeys.put(clientConnection, keys);
    }
    keys.add(key);
}
```

ClientConnection 封装了客户端 socket 连接。它有如下结构。这个结构保留了服务器阻塞-IO 和服务器非阻塞-IO 两者相同的部分。

```java
public interface ClientConnection {
    void write(RequestOrResponse response);
    void close();
}
```

这里能在单个连接上多次注册监听。所以它存储键监听列表的连接映射是非常重要的。当客户端连接关闭时也时去移除所有跟监听相关的值也是必要的：

```java
public void close(ClientConnection connection) {
    removeWatches(connection);
}

private synchronized void removeWatches(ClientConnection clientConnection) {
    List<String> watchedKeys = connection2WatchKeys.remove(clientConnection);
    if (watchedKeys == null) {
        return;
    }
    for (String key : watchedKeys) {
        watches.remove(key);
    }
}
```

当服务器发生特定事件如像设置键值，服务器通过构造一个相关的 WatchEvent 通知所有已经注册的客户端。

```java
private synchronized void notifyWatchers(SetValueCommand setValueCommand, Long entryId) {
    if (!hasWatchesFor(setValueCommand.getKey())) {
        return;
    }
    String watchedKey = setValueCommand.getKey();
    WatchEvent watchEvent = new WatchEvent(watchedKey,
                                setValueCommand.getValue(),
                                EventType.KEY_ADDED, entryId);
    notify(watchEvent, watchedKey);
}

private void notify(WatchEvent watchEvent, String watchedKey) {
    List<ClientConnection> watches = getAllWatchersFor(watchedKey);
    for (ClientConnection pipelinedClientConnection : watches) {
        try {
            String serializedEvent = JsonSerDes.serialize(watchEvent);
            getLogger().trace("Notifying watcher of event "
                    + watchEvent +
                    " from "
                    + server.getServerId());
            pipelinedClientConnection
                    .write(new RequestOrResponse(RequestId.WatchRequest.getId(),
                            serializedEvent));
        } catch (NetworkException e) {
            removeWatches(pipelinedClientConnection); //remove watch if network connection fails.
        }
    }
}
```

其中一个关键的事情就是要注意状态相关监听是能从客户端请求处理代码和客户端连接处理代码到关闭连接并发访问的。所以所有的方法访问这个状态监听都需要通过锁来保护。

## 分级存储监听

[一致性核心](Consisten-Core.md)大部分都支持分级存储的。能在父节点或键的前缀设置监听。任何变化都是触发在父节点上的子节点的监听。对于每个事件，一致性核心要遍历路径检查如果这些在父路径上设置了监听，就要给这些监听发送事件。

```java
List<ClientConnection> getAllWatchersFor(String key) {
    List<ClientConnection> affectedWatches = new ArrayList<>();
    String[] paths = key.split("/");
    String currentPath = paths[0];
    addWatch(currentPath, affectedWatches);
    for (int i = 1; i < paths.length; i++) {
        currentPath = currentPath + "/" + paths[i];
        addWatch(currentPath, affectedWatches);
    }
    return affectedWatches;
}

private void addWatch(String currentPath, List<ClientConnection> affectedWatches) {
    ClientConnection clientConnection = watches.get(currentPath);
    if (clientConnection != null) {
        affectedWatches.add(clientConnection);
    }
}
```

这允许在键前缀上设置一个监听（如 “servers”）。任何键通过使用这个前缀创建键（如 “servers/1”，“servers/2”）都会触发这个监听。

因为要调用的函数映射存储在键前缀中，它要遍历所有层来查找这个函数给接收到事件的客户端调用是很重要的。另一种方法是将事件触发的路径与事件一起发送，这样客户端就知道是哪个监听事件被发送。

## 处理连接异常

客户端和服务端之间的连接能在任何期间断开。对于有些场景是有问题的，因为客户端可能会连接断开期间丢失一些事件。例如，一个集群控制器要感知一些节点的失败，这可以通过一些移除事件和一些键来表明。客户端需要告诉服务器它最近接受到的事件。客户端当重启监听的时候就会发送最近接受到的事件号。服务器会预期的发送它从该事件号开始记录的所有事件。

在一致性核心客户端，让客户端重新与 leader 建立连接时，它就能这么做。

```java
private void connectToLeader(List<InetAddressAndPort> servers) {
    while (isDisconnected()) {
        logger.info("Trying to connect to next server");
        waitForPossibleLeaderElection();
        establishConnectionToLeader(servers);
    }
    setWatchesOnNewLeader();
}

private void setWatchesOnNewLeader() {
    for (String watchKey : watches.keySet()) {
        sendWatchResetRequest(watchKey);
    }
}

private void sendWatchResetRequest(String key) {
    pipelinedConnection.send(new RequestOrResponse(RequestId.SetWatchRequest.getId(),
            JsonSerDes.serialize(new SetWatchRequest(key, lastWatchedEventIndex)), correlationId.getAndIncrement()));
}
```

**服务器对每个发生的事件都进行了编号。**例如，如果服务器是[一致性核心](Consisten-Core.md)的，它会以严格的顺序存储所有的状态变化以及每个变化都会在前面提到的[预写日志](Write-Ahead-Log.md)的日志索引对其进行编号，这样客户端就能从请求特定索引开始的事件。

### 从键值存储派生事件

如果它也能在每次发生变化进行编号，并且每次存储这个编号，就可以根据键值存储的键的当前状态生成事件。

当客户端重新建立对服务器的连接，它能再次设置监听并且发送最近的变更编号。服务器就会将它与存储的值比较，如果它比客户端发送的要大，它就会重新发送事件给客户端。从键值存储区派生事件可能有点尴尬，因为需要猜测事件。它可能会丢失一些事件。举个例子，如果这个键被创建然后删除，正当这个时候客户端断开连接了，那么这个创建的事件就会丢失。

```java
private synchronized void eventsFromStoreState(String key, long stateChangesSince) {
    List<StoredValue> values = getValuesForKeyPrefix(key);
    for (StoredValue value : values) {
        if (values == null) {
            //the key was probably deleted send deleted event
            notify(new WatchEvent(key, EventType.KEY_DELETED), key);
        } else if (value.index > stateChangesSince) {
            //the key/value was created/updated after the last event client knows about
            notify(new WatchEvent(key, value.getValue(), EventType.KEY_ADDED, value.getIndex()), key);
        }
    }
}
```

[zookeeper](https://zookeeper.apache.org/) 使用这种方法。zookeeper 里的监听默认是一次性的。一旦事件被触发，客户端需要再次设置监听，如果它们想要一起接收事件的话。在再次设置监听之前会有一些事件会丢失，所以客户端需要确保读取他们最近的状态，以至他们不会丢失任何更新。

### 存储事件历史

保留过去事件的历史记录以及从事件历史中回复客户端是很容易的。使用这个方法的问题是事件历史需要限制，比如 1000 个事件。如果客户端长时间断开连接，它可能丢失超过 1000 个事件窗口的事件。

一个简单的实现就是使用 google 的 EvictingQueue：

```java
public class EventHistory implements Logging {
    Queue<WatchEvent> events = EvictingQueue.create(1000);
    public void addEvent(WatchEvent e) {
        getLogger().info("Adding " + e);
        events.add(e);
    }

    public List<WatchEvent> getEvents(String key, Long stateChangesSince) {
        return this.events.stream()
                .filter(e -> e.getIndex() > stateChangesSince && e.getKey().equals(key))
                .collect(Collectors.toList());
    }
}
```

当客户端重新建立连接以及重启监听时，这些事件就会从历史中发送

```java
private void sendEventsFromHistory(String key, long stateChangesSince) {
    List<WatchEvent> events = eventHistory.getEvents(key, stateChangesSince);
    for (WatchEvent event : events) {
        notify(event, event.getKey());
    }
}
```

### 使用多版本存储（multi-version storage）

为了能追踪所有变更，还可能使用多版本存储。她保留了追踪的每个键的所有版本，所以很容易就能从版本请求中获取所有的变更。

[etcd](https://etcd.io/) 在版本 3 上面就使用了次方法。

## 例子

[zookeeper](https://zookeeper.apache.org/) 在节点上能设置监听。这用在了产品上（[kafka](https://kafka.apache.org/)）的成员关系组以及集群编号的失败保护。

[etcd](https://etcd.io/) 也有监听实现，它被严重使用在通过 [kubernets](https://kubernetes.io/) 的资源[监控](https://kubernetes.io/docs/reference/using-api/api-concepts/)实现。

# 原文地址

https://martinfowler.com/articles/patterns-of-distributed-systems/state-watch.html
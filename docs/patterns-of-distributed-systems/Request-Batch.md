# 批量请求

以最佳方式利用网络，将多个请求合并。

## 问题

当请求发送到集群节点时，如果存在发送的数据包很小，但请求次数很多，那么势必会增加网络延迟和请求处理时间（包括在服务器端对请求进行序列化、反序列化）。

例如，网络限制在1gbps，那么它的延迟和请求处理时间就是100微妙，如果客户端同时发送数百个请求--每个请求只有几字节--如果每个请求需要100微妙才能完成，那么这将大大限制了整个吞吐量。

## 解决方案

将多个请求合并到单个请求批量处理中。该批请求将被发送到集群节点进行处理。每个请求的处理方式与单个请求完全相同。然后，它将用该批响应进行响应。

举个例子，考虑一个分布式键值存储，客户端发送请求以在服务器上存储多个键值。**当客户端收到发送的请求时，它不会立即通过网络发送请求；相反，它保持一个待发送请求的队列。**

```java
class Client {
  ...
  LinkedBlockingQueue<RequestEntry> requests = new LinkedBlockingQueue<>();

  public CompletableFuture send(SetValueRequest setValueRequest) {
      int requestId = enqueueRequest(setValueRequest);
      CompletableFuture responseFuture = trackPendingRequest(requestId);
      return responseFuture;
  }

  private int enqueueRequest(SetValueRequest setValueRequest) {
      int requestId = nextRequestId();
      byte[] requestBytes = serialize(setValueRequest, requestId);
      requests.add(new RequestEntry(requestBytes, clock.nanoTime()));
      return requestId;
  }
  private int nextRequestId() {
      return requestNumber++;
  }
}
```

查询请求的时间会被追踪；这稍后会用于决定请求是否可以作为批处理的一部分发送。

```java
class RequestEntry {
  class RequestEntry {
      byte[] serializedRequest;
      long createdTime;
  
      public RequestEntry(byte[] serializedRequest, long createdTime) {
          this.serializedRequest = serializedRequest;
          this.createdTime = createdTime;
      }
      ...
  }
}
```

然后，它跟踪待处理的请求，以便在收到响应时完成。每个请求将被分配一个唯一的请求编号，可用于映射响应和完成请求。

```java
class Client {
  ...
  Map<Integer, CompletableFuture> pendingRequests = new ConcurrentHashMap<>();

  private CompletableFuture trackPendingRequest(Integer correlationId) {
      CompletableFuture responseFuture = new CompletableFuture();
      pendingRequests.put(correlationId, responseFuture);
      return responseFuture;
  }
}
```

客户端启动一个单独的任务，持续跟踪排队的请求

```java
class Client {
  ...
  public Client(Config config, InetAddressAndPort serverAddress, SystemClock clock) {
      this.clock = clock;
      this.sender = new Sender(config, serverAddress, clock);
      this.sender.start();
  }
}

class Sender {
  ...
  public void run() {
      while (isRunning) {
          boolean maxWaitTimeElapsed = requestsWaitedFor(config.getMaxBatchWaitTime());
          boolean maxBatchSizeReached = maxBatchSizeReached(requests);
          if (maxWaitTimeElapsed || maxBatchSizeReached) {
              RequestBatch batch = createBatch(requests);
              try {
                  BatchResponse batchResponse = sendBatchRequest(batch, address);
                  handleResponse(batchResponse);

              } catch (IOException e) {
                  batch.getPackedRequests().stream().forEach(r -> {
                      pendingRequests.get(r.getCorrelationId()).completeExceptionally(e);
                  });
              }
          }
      }
  }
    
  private RequestBatch createBatch(LinkedBlockingQueue<RequestEntry> requests) {
      RequestBatch batch = new RequestBatch(MAX_BATCH_SIZE_BYTES);
      RequestEntry entry = requests.peek();
      while (entry != null && batch.hasSpaceFor(entry.getRequest())) {
          batch.add(entry.getRequest());
          requests.remove(entry);
          entry = requests.peek();
      }
      return batch;
  }
  
  class RequestBatch {
    public boolean hasSpaceFor(byte[] requestBytes) {
      	return batchSize() + requestBytes.length <= maxSize;
    }
    private int batchSize() {
      	return requests.stream().map(r->r.length).reduce(0, Integer::sum);
    }   
  }
}
```

通常有两种检查。

- 如果积累了足够多的请求，使批处理达到配置的最大大小。

  ```java
  class Sender {
    ...
    private boolean maxBatchSizeReached(Queue<RequestEntry> requests) {
        return accumulatedRequestSize(requests) > MAX_BATCH_SIZE_BYTES;
    }
  
    private int accumulatedRequestSize(Queue<RequestEntry> requests) {
        return requests.stream().map(re -> re.size()).reduce((r1, r2) -> r1 + r2).orElse(0);
    }
  }
  ```

- 因为我们不可能永远等待批处理被填入，我们可以配置最大等待时间。发送者任务等待，然后检查请求是否在最大等待时间之前被加入。

  ```java
  class Sender {
    ...
    private boolean requestsWaitedFor(long batchingWindowInMs) {
        RequestEntry oldestPendingRequest = requests.peek();
        if (oldestPendingRequest == null) {
            return false;
        }
        long oldestEntryWaitTime = clock.nanoTime() - oldestPendingRequest.createdTime;
        return oldestEntryWaitTime > batchingWindowInMs;
    }
  }
  ```

一旦满足这两个检查的任意一个，批量请求就会发送给服务器。服务器会解压该批请求并处理这每一个当个请求

```java
class Server {
  ...
  private void handleBatchRequest(RequestOrResponse batchRequest, ClientConnection clientConnection) {
      RequestBatch batch = JsonSerDes.deserialize(batchRequest.getMessageBodyJson(), RequestBatch.class);
      List<RequestOrResponse> requests = batch.getPackedRequests();
      List<RequestOrResponse> responses = new ArrayList<>();
      for (RequestOrResponse request : requests) {
          RequestOrResponse response = handleSetValueRequest(request);
          responses.add(response);
      }
      sendResponse(batchRequest, clientConnection, new BatchResponse(responses));
  }

  private RequestOrResponse handleSetValueRequest(RequestOrResponse request) {
      SetValueRequest setValueRequest = JsonSerDes.deserialize(request.getMessageBodyJson(), SetValueRequest.class);
      kv.put(setValueRequest.getKey(), setValueRequest.getValue());
      RequestOrResponse response = new RequestOrResponse(RequestId.SetValueResponse.getId(), "Success".getBytes(), request.getCorrelationId());
      return response;
  }
}
```

客户端接收批处理响应并完成所有挂起的请求。

```java
class Client{
  ...
  private void handleResponse(BatchResponse batchResponse) {
      List<RequestOrResponse> responseList = batchResponse.getResponseList();
      logger.debug("Completing requests from " + responseList.get(0).getCorrelationId() + " to " + responseList.get(responseList.size() - 1).getCorrelationId());
      responseList.stream().forEach(r -> {
          CompletableFuture completableFuture = pendingRequests.remove(r.getCorrelationId());
          if (completableFuture != null) {
              completableFuture.complete(r);
          } else {
              logger.error("no pending request for " + r.getCorrelationId());
          }
      });
  }
}
```

## 技术考虑

批量大小的选择应基于单个消息的大小和可用的网络带宽，以及基于实际负载观察到的延迟和吞吐量来改进。这些配置的值都被配置为认为是合理的默认值，并假设这是单个消息最佳大小和服务端处理的最合适的批次大小。例如，[Kafka](https://kafka.apache.org/)有一个默认的批处理大小为16Kb。它还有一个叫做 "linger.ms "的配置参数，默认值为0。但是，如果消息的大小更大，则更大的批处理大小可能会更好。

批量大小太大，可能只会带来负面作用。例如，以MB为单位的批处理大小会进一步增加处理方面的开销。这就是为什么批量大小参数通常是根据性能测试的观察结果来调整的。

请求批处理通常与[请求管道](Request-Pipeline.md)一起使用，以提高总体吞吐量和延迟。

当`retry-backoff`策略被用于向集群节点发送请求时，整个批处理请求将被重试。集群节点可能已经处理了部分批处理；所以为了确保重试工作没有任何问题，你应该实现[幂等请求](Idempotent-Receiver.md)。

## 案例

[Kafka](https://kafka.apache.org/)支持生产者请求的批量处理。

在将数据保存到磁盘时也使用批处理。例如[bookkeeper](https://bookkeeper.apache.org/)以类似的方式实现批处理，将日志刷新到磁盘。

在TCP中使用[Nagel算法](https://en.wikipedia.org/wiki/Nagle%27s_algorithm)来批量处理多个较小的数据包，以提高整体网络吞吐量。


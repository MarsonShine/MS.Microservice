# gRPC

gRPC是现代开源的基于 HTTP2.0 高性能 RPC 框架，它能运行在任何环境。它能高效地连接数据中心以及内部的服务，支持可插件化负载均衡，跟踪，健康检查以及授权。它也适用于分布式计算的连接设备，移动应用程序和浏览器后端服务。

主要包含如下特性

- 简单的服务定义，按照定义好的 Protobuf 格式协议（protobuf 是一个强大的二进制序列化工具集和语言）
- 快速启动，伸缩性好
- 跨语言和平台，根据定义的服务自动生成各个语言的客户端，服务端代码
- 双向流与授权

gRPC可以使用协议缓冲区作为它的接口定义语言(IDL)和它的底层消息交换格式。

在 gRPC 中，客户端应用程序可以直接调用不同机器上的服务方法，就像本地对象调用方法一样，这样能够让你更加容易的创建分布式应用程序于服务。在众多 RPC 系统里，gRPC 是围绕定义好的服务运行的，它指定一个远程调用的方法，这个方法能传参数也能返回对象类型。在服务端，运行一个 gRPC 服务来实现合格接口并处理客户端的请求。在客户端会有一个相同方法的存根作为服务提供。

gRPC 客户端和服务端能在各种不同的环境彼此交互通信。比如你可以使用 Java 语言作为一个 gRPC 服务器，而客户端你可以用 Go，Python 或者是 Ruby。此外还有谷歌等对 gRPC 有些功能提供支持，你可以直接使用。

# 协议缓冲区（Protocol Buffers）

gRPC 默认使用 Protocol Buffers，是谷歌开源的序列化结构数据（也可以使用其它数据结构，比如 JSON）。下面讲一下是如何工作的

第一步就是要定义你想要序列化的数据结构，放置在以 `.proto` 结尾的格式文件（称之为 proto 文件）。Protocol buffer 数据被结构化为消息，每条消息都是一个小的逻辑条目，它包含了一系列 name-value 对被调用的字段信息。如

```protobuf
message Person {
	string name = 1;
	int32 id = 2;
  bool has_ponycopter = 3;
}
```

一旦你定义好了这种数据结构，那么 protocol buffer 编译器就会根据你定义的内容编译这个文件自动生成在你所引用的语言（如 C#）的数据访问类。以上面的例子来说就会生成一些字段访问器，像 `GetName()`、`SetName()`，以及从原始字节序列化/解析整个结构的方法。

你可以直接 `.proto` 文件中定义 gRPC 服务方法，可以指定请求参数与返回类型

```protobuf
service Greeter {
  // Sends a greeting
  rpc SayHello (HelloRequest) returns (HelloReply) {}
}

// The request message containing the user's name.
message HelloRequest {
  string name = 1;
}

// The response message containing the greetings
message HelloReply {
  string message = 1;
}
```

gRPC 使用特殊的插件根据的文件内容来生成代码：会生成客户端以及服务端代码，以及用于填充、序列化和检索消息类型的常规协议缓冲区代码。

# 使用方式

## 服务定义

gRPC 可以定义四种服务方式：

- 一元 RPC，客户端向服务发送单个请求，并获得单个响应，就像调用普通方法一样。

  ```protobuf
  rpc SayHello(HelloRequest) returns (HelloResponse);
  ```

- 流服务 RPC，客户端向服务发送请求并获取服务的响应流。如果不是服务没有消息数据，客户端会一直读取服务响应返回的流。gRPC 保证单独的 RPC 调用消息都是有序的。

  ```protobuf
  rpc LotsOfReplies(HelloRequest) returns (stream HelloResponse);
  ```

- 客户端流 RPC，客户端会向服务发送流请求。一旦客户端完成流输入消息，它就会一直等待服务端响应并返回。再次重生，gRPC 能保证了单个消息的排序

  ```protobuf
  rpc LotsOfGreetings(stream HelloRequest) returns (HelloResponse);
  ```

- 双向绑定流 RPC，双端使用读写流来发送响应请求。两个端流都是独立的，所以客户端和服务端都能正确读写流：例如，服务端在响应客户端请求之前不会向客户端回写返回体。每个流的消息顺序都有保留的。

  ```protobuf
  rpc BidiHello(stream HelloRequest) returns (stream HelloResponse);
  ```

## 生命周期

### 一元 RPC

### 服务流RPC

### 客户端流RPC

### 双向流

### DeadLine 与超时

### RPC 结束

### 取消 RPC

### 元数据

### 通道（Channel）

























# 参考资料

- https://www.grpc.io/
- https://www.grpc.io/docs/what-is-grpc/introduction/
- https://grpc.io/docs/what-is-grpc/core-concepts/


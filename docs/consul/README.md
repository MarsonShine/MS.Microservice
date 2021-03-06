# Consul

Consul 是一套 Service Mesh 的解决方案，它提供完整的服务网格控制面板的功能，包括服务发现，配置，功能分割。这些特性都能根据需要单独使用，也能全部使用来构成一个完整的服务网格。Consul 内置代理，所以可以开箱即用，也支持集成三方代理，如 Envoy。

下面来聊关于 Consul 支持的主要特性：

- 服务发现：Consul 客户端能注册服务，比如 `api` 或 `mysql`，以及其它 Consul 客户端发现服务提供者。使用 DNS 或 HTTP，应用很容易的发现他们所依赖的上游服务。
- 健康检查：Consul 提供服务紧密相关任意的健康检查（服务是否返回 200 状态），或者通过本地结点关联（是内存利用率低于90%）。操作者利用这些信息来监控集群的健康状态，服务发现组件也用它来将那些不健康的请求转移出去。
- KV 存储：应用程序能用 Consul 级别的键/值存储，比如在动态配置上，特征标识，协作，领导人选举等等。这些简单的 HTTP API 使它更容易使用。
- 安全服务通信：通过给不同的服务建立分发 TLS 凭证连接
- 多数据中心：Consul 提供开箱即用的多数据中心，这样就不用额外添加一个抽象层来拓展多个区域。

# Consul 基本架构

Consul 是分布式、高可用的系统。Consul 的每个提供服务的结点都运行在一个 Consul 代理。这在服务发现或给键值赋值取值的时候是没必要运行这个代理的。这个代理的主要职责就是健康检查，在结点上是否正常。

这个代理可以与一个或多个 Consul 服务对话。这个 Consul 服务是数据存储和赋值的地方。建议 Consul 服务运行在3到5台，这样能有效避免因故障数据丢失的场景。建议为每个 Consul 服务集群配置一个数据中心。

服务维护一个 `catalog`，它由代理提交的聚合信息组成。这个 `catelog` 维护集群高级视图，包括哪个服务是可用的，哪些结点运行在服务上，健康信息等等。

需要发现其它服务或结点的基础设施层组件能够发现其它 Consul 服务或任意的 Consul 代理。这个代理可以将查询自动转发给服务器。

每个数据中心运行一个 Consul 服务器集群。当一个跨数据中心的服务发现或配置请求生成时，本地 Consul 服务器会转发请求到远程数据中心并返回结果。

# Consul 实践

**在生产环境中，要以服务器或客户端模式运行每个代理。每个 Consul 数据中心必须至少有一个服务器，负责维护 Consul 的状态。**

非服务器代理在客户机模式下运行。客户机是一个轻量级流程，用于注册服务、运行健康检查和将查询转发到服务器。客户机必须在运行服务的 Consul 数据中心的每个节点上运行，因为客户机是关于服务运行状况的真相的来源。

下载并安装 Consul

```shell
yum -y install consul
consul --version
```

开启开发模式

```shell
consul agent -dev
# 查找数据中心
consul members
```

> 注意，开发模式只能在开发环境开启这个选项，这个选项是不安全的，不具伸缩性的。

通过 DNS 接口发现结点，DNS 接口会发送你的请求转到的 Consul 服务器，如果你没有开启缓存的话。DNS 查询默认在端口 8600 运行查询

```shell
dig @127.0.0.1 -p 8600 95527e8116d6.node.consul	# 95527e8116d6 这个值是通过 consul members 查询结点的出来的
```

停止代理

```shell
consul leave
# 输出 Graceful leave complete 即为正确退出
```

## 使用 Consul 服务发现注册服务

就像上面说到了，Consul 提供一个 DNS 接口给下游服务使用来查询上游依赖的 IP 地址。Consul 知道这些服务的位置，因为每个服务都向本地 Consul 客户端注册。操作员也能手动注册服务，配置管理工具也能在部署服务时注册服务，或者容器编排平台可以通过集成自动注册服务。

### 定义服义

1. 创建 consul 配置文件 `/etc/consul.d`，这样 consul 就会把这个目录的配置文件加载进来

   ```shell
   mkdir ./etc/consul.d
   ```

2. 编写一个服务定义配置文件吗，命名为 `web.json`

   ```shell
   vi ./consul.d/web.json
   {
     "service": {
       "name": "web",
       "tags": [
         "rails"
       ],
       "port": 80
     }
   }
   ```

   假设有一个服务运行在 80 端口，上面的内容定义了服务名称，端口以及一个可选项 `tags`，你可以使用稍后使用这个查询服务。

   *注意：在某些配置中启用脚本检查可能会引入被恶意软件锁定的远程执行漏洞。在生产环境中，我们强烈建议改为 `-enable-local-script-checks`*

3. 启动代理，使用命令行标志指定配置目录，并在代理上启用脚本检查。

   ```shell
   consul agent -dev -enable-script-checks -config-dir=./consul.d
   ```

在多代理 Consul 数据中心中，每个服务都将通过本地 Consul 客户端注册，并且客户端会转发注册到 Consul 服务器，这些服务器维护服务的 catalog。

如果要注册多服务，那么只需要在 Consul 配置文件目录中创建多个服务定义配置文件即可（第一步）

### 查询服务

一旦代理添加服务到 Consul 的服务 catalog，你就可以使用 DNS 接口或 HTTP API 查询服务了。

#### DNS 接口

就像前面提到的一样的，可以通过 DNS 接口查询服务。服务注册的 DNS 名称是 Consul 的 `NAME.service.consul`，其中的 `NAME` 就是你注册服务的名字（在这个例子中，就是 `web`）。默认情况下，所有的 DNS 名称命名空间都在 `consul` ，可以通过[配置](https://www.consul.io/docs/agent/options.html#domain)修改。

```shell
dig @127.0.0.1 -p 8600 web.service.consul
# 输入如下
; <<>> DiG 9.11.4-P2-RedHat-9.11.4-16.P2.el7_8.6 <<>> @127.0.0.1 -p 8600 web.service.consul
; (1 server found)
;; global options: +cmd
;; Got answer:
;; ->>HEADER<<- opcode: QUERY, status: NOERROR, id: 41554
;; flags: qr aa rd; QUERY: 1, ANSWER: 1, AUTHORITY: 0, ADDITIONAL: 1
;; WARNING: recursion requested but not available

;; OPT PSEUDOSECTION:
; EDNS: version: 0, flags:; udp: 4096
;; QUESTION SECTION:
;web.service.consul.            IN      A

;; ANSWER SECTION:
web.service.consul.     0       IN      A       127.0.0.1

;; Query time: 0 msec
;; SERVER: 127.0.0.1#8600(127.0.0.1)
;; WHEN: Mon Oct 26 09:11:18 UTC 2020
;; MSG SIZE  rcvd: 63
```

也可以使用整个 IP/port 作为一个 SRV 记录查询

```shell
dig @127.0.0.1 -p 8600 web.service.consul SRV
```

也可以通过 `tags` 过滤查询

```shell
dig @127.0.0.1 -p 8600 rails.web.service.consul
```

#### HTTP API

也可以通过 HTTP API 查询

```shell
curl http://localhost:8500/v1/catalog/service/web
```

结果会返回给定服务的所有结点信息。也可以在 API 中指定过滤查询运行情况良好的服务，DNS 在幕后自动执行。

```shell
curl 'http://localhost:8500/v1/health/service/web?passing'
```

### 更新服务

当遇到更新服务配置的时候，可以通过发送信号 `SIGNUP` 给代理或运行 `consul reload` 在不用关闭的情况下更新。除此之外，还可以使用 HTTP API 实现增，删以及动态更改服务

1. 编辑 consul 配置文件

   ```shell
   echo '{
     "service": {
       "name": "web",
       "tags": [
         "rails"
       ],
       "port": 80,
       "check": {
         "args": [
           "curl",
           "localhost"
         ],
         "interval": "10s"
       }
     }
   }' > ./consul.d/web.json
   ```

   增加了 `check` 节点，它会基于脚本的运行健康检查，该检查会通过 curl 每 10 秒连接到 web 服务。这个脚本作为 Consul 启动的相同用户下运行。

   如果命令以退出代码>= 2退出，则检查将失败，Consul 将认为服务不健康。退出码为1将被视为警告状态。

2. 重载配置文件

   ```shell
   consul reload
   # 运行成功则会每 10 秒输出如下信息
   Check is now critical: check=service:web
   ```

通过 DNS 查询健康状态下的服务

```shell
dig @127.0.0.1 -p 8600 web.service.consul
# 如果有服务处于不健康状态下，则不会显示，同样通过 HTTP 也是如此（上面已提到）
```

## 通过 Consul Service Mesh 连接服务

consul 可以通过 sidecar 代理将服务相互连接起来，sidecar 代理是在本地与每个服务实例一起部署的。这种部署方式，本地 sidecar 代理能够在服务实例之间控制网络流量，这是一个 service mesh（服务网格的 sidecar 模式）。关于 sidecar 模式详见 https://docs.microsoft.com/en-us/azure/architecture/patterns/sidecar

consul 服务网格允许你安全并观察你服务之间的通讯，在不修改代码的前提下。相反，consul 将 sidecar 代理配置在你的服务之间建立相互的 TLS，并根据根据它们注册的名称允许或拒绝它们之间的通讯。因为 sidecar 代理控制着所有服务之间的流量，它们可以收集（gather）相关的指标，并将其到处到第三方聚合器，比如 Prometheus。

您也可以原生的集成将应用程序与 Consul 连接，来获取更优的性能和安全。

使用服务网格注册服务跟一般服务注册是类似的：

- 开启一个服务
- 正常注册它，但是增加一个 `connect` 节点
- 注册第二个代理来与服务通信
- 开始一个 sidecar 代理
- 通过创建一个 intention 来练习阻塞与服务的连接

> 注意，这个步骤只适用于开发环境，为安全起见此部署方法不建议部署到生产方法，具体生产部署详见于[生产环境 Consul 连接服务网格](https://learn.hashicorp.com/tutorials/consul/service-mesh-production-checklist)。

### 开启一个未知 Consul 服务

您可以使用 `socat` 来开始一个基础的 echo 服务，它在这个例子中作为上游服务。在生产环境中就是数据库、后端服务或其他服务依赖的任何服务。

Socat 是一个已有几十年历史的Unix实用程序，它缺乏加密或TLS协议的概念。您可以使用它作为 Consul 服务网格解决了什么问题的例子。如果您的机器上没有 socat，那么可以直接在包管理中下载安装。

```shell
socat -v tcp-l:8181,fork exec:"/bin/cat"
# 开启一个 socat 服务，指定监听的 TCP 端口是 8181
```

之后可以通过使用命令 `nc` 来验证是否正常工作（在正确的端口上执行 echo 服务）。一旦你输入一些字符并按 `回车`。那么你输入的文本就会原本输出回来

```shell
nc 127.0.0.1 8181
# 输入
hello
# 输出
hello
# 输入
how are you
# 输出
how are you
```

### Consul 注册服务和代理

这一步来注册，通过 consul 编写新的服务定义，就像前面提过的[服务定义](#定义服务)。这次将注册中包含一个 connect 节点，将注册一个 sidecar 代理来处理后端服务实力的流量。

添加一个 `socat.json` 服务定义配置文件到 `/etc/consul.d` 目录中，命令内容如下

```shell
echo '{
  "service": {
    "name": "socat",
    "port": 8181,
    "connect": {
      "sidecar_service": {}
    }
  }
}' > ./consul.d/socat.json
```

更新 consul

```shell
consul reload
```

回头看新增的 `connect` 节点，这个空配置通知 consul 在一个动态分配的端口上为这个进程注册一个 sidecar 代理。consul 带有用于测试目的的L4代理，以及对Envoy的一流支持，您应该将其用于生产部署和第7层流量管理

现在开始连接服务

```shell
consul connet proxy -sidecar-for socat
```

输出如下信息即为成功

```shell
==> Consul Connect proxy starting...
    Configuration mode: Agent API
        Sidecar for ID: socat
              Proxy ID: socat-sidecar-proxy

==> Log data will now stream in as it occurs:

    2020-10-26T16:05:12.943Z [INFO]  proxy: Proxy loaded config and ready to serve
    2020-10-26T16:05:12.943Z [INFO]  proxy: Parsed TLS identity: uri=spiffe://b712d23c-34b7-a743-88e0-9e2b9b972ba7.consul/ns/default/dc/dc1/svc/socat roots=[pri-xyucbjtb.consul.ca.b712d23c.consul]
    2020-10-26T16:05:12.943Z [INFO]  proxy: Starting listener: listener="public listener" bind_addr=0.0.0.0:21000
```

### 注册一个依赖服务和代理

接下来注册一个下游服务，命名为 “web”。就像 socat 定义的服务一样，web 的配置文件还包含一个指定 sidecar 的 `connect` 结点，但是又不像上面一样是个空的，而是在 socat 上指定一个 web 上游依赖，代理将监听这个 9191 端口。

```shell
echo '{
  "service": {
    "name": "web",
    "connect": {
      "sidecar_service": {
        "proxy": {
          "upstreams": [
            {
              "destination_name": "socat",
              "local_bind_port": 9191
            }
          ]
        }
      }
    }
  }
}' > ./consul.d/web.json
```

如果我们运行一个真正的web服务，它将在一个回路地址上与它的代理通信。代理会加密通信（traffic）并发送到 sidecar 代理上的 socat 服务。Socat 服务将会解密通信并发送给本地端口号 8181 的回路地址的 socat。因为这里没有服务运行，您将通过详装是web服务与我们指定的端口(9191)上的web服务的代理进行通信。

在开始代理处理之前，你要确认不能连接 9191 socat 服务。

现在通过使用自 sidecar 注册的配置文件开始 web 代理。

```shell
consul connect proxy -sidecar-for web
```

出现一下信息即成功开启代理

```shell
==> Consul Connect proxy starting...
    Configuration mode: Agent API
        Sidecar for ID: web
              Proxy ID: web-sidecar-proxy

==> Log data will now stream in as it occurs:

    2020-10-27T02:07:41.123Z [INFO]  proxy: Starting listener: listener=127.0.0.1:9191->service:default/socat bind_addr=127.0.0.1:9191
    2020-10-27T02:07:41.131Z [INFO]  proxy: Proxy loaded config and ready to serve
    2020-10-27T02:07:41.132Z [INFO]  proxy: Parsed TLS identity: uri=spiffe://f747f347-a603-96a1-6c36-cbf0d65cbe47.consul/ns/default/dc/dc1/svc/web roots=[pri-r5m71lq.consul.ca.f747f347.consul]
    2020-10-27T02:07:41.132Z [INFO]  proxy: Starting listener: listener="public listener" bind_addr=0.0.0.0:21001
```

当输入 `request from port 9191` 时，会实时输出 `requst from port 9191`。但是您会发生，在之前监听的端口 8181，也会输出 `request from port 9191`。这是因为从 9191 的请求转发到了目标上游服务 `socat`。

## Consul 数据存储

Consul 有两种方式存储 Key/Value 数据：Consul CLI 以及 UI。先说 CLI

### 添加数据

使用命名 `consul kv put` 插入一个数据到 K/V 存储器。第一个参数就是要存储的条目键名，第二个就是键值

```shell
consul kv put redis/config/minconns 1 # key=redis/config/minconns;value=1
```

输出如下信息几位成功

```
Success! Data written to: redis/config/minconns
```

注意下面输入的 `redis/config/users/admin`，设置了一个标识 `flags` 值为 42。键支持设置 64 位的整数值，这在 Consul 内部是没有使用的。

```
consul kv put -flags=42 redis/config/users/admin abcd1234
```

### 查询数据

```
consul kv get redis/config/minconns
# 输出
1
```

Consul 还保留了键值对的一些元数据信息。可以使用 `-detailed` 检索元数据

```shell
consul kv get -detailed redis/config/users/admin
# 输出
CreateIndex      32
Flags            42
Key              redis/config/users/admin
LockIndex        0
ModifyIndex      32
Session          -
Value            abcd1234
```

查询所有键值信息，结果按字典顺序返回。

```shell
consul kv get -recurse
# 输出
redis/config/minconns:1
redis/config/users/admin:abcd1234
```

### 删除数据

使用关键字 `delete`

```
consul kv delete redis/config/minconns
# 输出
Success! Deleted key: redis/config/minconns
```

注意，如果删除一个键不存在的值，会默认返回成功结果。

删除所有的键值对

```
consul kv delete -recurse redis # 删除所有前缀为 redis 的键值
# 输出
Success! Deleted keys with prefix: redis
```

### 修改数据

修改以存在的值

```
consul kv put foo bar	# 存储数据
consul kv get foo	    # 查询数据
consul kv put foo zip	# 修改数据
consul kv get foo	    # 查询数据
```

关于 consul 执行的更新数据存储操作是 CAS（check-and-set）是原子操作，此外还包括一些其他的相关的复杂操作。可以输入 `consul kv put -h` 获取更多。

## 数据中心

当 consul 开启代理时，彼此之间是不知道的。特别是只有一个成员的数据中心。代理要知道的话有两种方式。一种是在已有的数据中心添加新的代理，你要在这个数据中心提供任何其他代理的 IP 地址（无论客户端还是服务器）。它会导致新的代理加入这个数据中心。一旦这个代理成为新数据中心的成员，它就会通过管道（gossip）知道了其他代理。

那么如何将两个代理互相连接起来，创建两个成员的数据中心呢？

### 设置环境变量

**docker 中无法实现此主题**

官网：https://www.consul.io/




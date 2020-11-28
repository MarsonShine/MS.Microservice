# 集群指引

## 集群构造

### 构成集群的方法

RabbitMQ 可以通过以下几种方式构成集群：

- 在[配置文件](https://www.rabbitmq.com/configure.html)中的集群节点列表申明
- 基于 DNS 发现的申请
- 通过插件使用 [AWS(EC2) 实例发现](https://github.com/rabbitmq/rabbitmq-peer-discovery-aws)申明
- 通过插件使用 [K8S 发现](https://github.com/rabbitmq/rabbitmq-peer-discovery-k8s)申明
- 通过插件使用[基于 Consul 发现](https://github.com/rabbitmq/rabbitmq-peer-discovery-consul)申明
- 通过插件使用[基于 etcd 发现](https://github.com/rabbitmq/rabbitmq-peer-discovery-etcd)申请
- 通过 `rabbitmqctl` 手动开启

集群的组成可以动态的概念。所有的 RabbitMQ broker 都在一个节点上运行。这些都能被加入到集群当中，随后又变回独立的 broker。

## 节点命名（标识符）

RabbitMQ 是通过节点名字来识别的。一个节点名称有两个部分组成，一个**前缀**（通常是 `rabbit`）以及 hostname。例如 `rabbit@node1.messaging.svc.local` 这个节点名称使用了 `rabbit` 作为前缀，使用 `node1.messaging.svc.local` 主机名。

集群中的节点名称必须是唯一的。如果在指定的集群中运行多个节点，必须使用不同的前缀名，如 `rabbit1@hostname` 和 `rabbit2@hostname`。

在集群中，每个节点都使用对应的节点名称来联系。这就意味着每个节点的名称的主机部分都要能够[解析](https://www.rabbitmq.com/clustering.html#hostname-resolution-requirement)。[CLI 工具](https://www.rabbitmq.com/cli.html)能辨别和处理节点使用的名称。

当一个节点开始运行，它会检查有没有被分配一个节点名，这是通过[环境变量](https://www.rabbitmq.com/configure.html#supported-environment-variables) `RABBITMQ_NODENAME` 实现的。如果没有显式的赋值，那么它就会自行解析主机名称和加上 `rabbit` 前缀作为它的节点名称。

如果系统使用完全限定域名（FQDNs）作为主机名，RabbitMQ 必须要配置使用长节点名称。在服务端通过设置 `RABBITMQ_USE_LONGNAME` 环境变量为 `true`。

对于 CLI 工具，设置 `RABBITMQ_USE_LONGNAME ` 或者 `--longnames` 指定可选项即可。

## 集群基本要求

### 主机名解析

RabbitMQ 节点使用域名处理每个节点，或者是短域名或完全限定域名。因此所有集训的成员的主机名必须是能够解析的，以及可能使用 rabbitmqctl 等命令行工具的机器。

主机名解析可以使用标准的操作系统提供的方法：

- DNS 记录
- 本地 host 文件（如 `/etc/hosts`）

在更严格的环境里，存放 DNS 记录和 hosts 的地方，要修改是受限制的。[Erlang 虚拟机能配置使用其它主机名解析方法](http://erlang.org/doc/apps/erts/inet_cfg.html)，例如使用其它的 DNS 服务器，本地文件以及非标准的 hosts 文件，或是这些方法的混合。这些方法可以与标准的 OS 主机名解析方法协同工作。

为了使用 FQDNs，在配置文件一节的 `RABBITMQ_USE_LONGNAME`。以及[节点命名](#节点命名（标识符）)

### 端口访问

RabbitMQ 节点绑定到端口(打开服务器TCP套接字)以接受客户端和CLI工具连接。其它处理工具如 SELinux 可能阻止 RabbitMQ 绑定到端口。当发生这种情况的时候，节点就会开启失败。

CLI 工具，客户端库以及 RabbitMQ 节点都能打开连接（客户端 TCP 连接）。防火墙会阻止节点以及命令行工具彼此通讯。要确保下面的端口是能够访问的：

- 4369：[epmd](https://www.rabbitmq.com/networking.html#epmd)，这是由 RabbitMQ 节点以及命令行工具使用的后台发现守护进程
- 5672，5671：被没有或有 TLS 的 AMQP 0-9-1 和 1.0 客户端使用
- 25672：供给内节点以及命令行工具通讯（Erlang 分布式服务端口）以及分配一个动态范围(默认限于单个端口，范围限制为 AMQP 端口 + 20000)。除非外部连接在这些端口上非常由必要（如集群使用子网外机器上的[联合](https://www.rabbitmq.com/federation.html)或命令行工具），这些端口都不应该暴露出来。具体详见[网络指引](https://www.rabbitmq.com/networking.html)
- 35672-35682：给命令行使用的（Erlang 分布式客户端端口），为和节点通讯还有分配一个动态范围（服务端分布式端口 + 10000 到服务端分布式端口 + 10010）。
- 15672：[HTTP API 客户端](https://www.rabbitmq.com/management.html)，[管理 UI](https://www.rabbitmq.com/management.html) 以及 [rabbitmq 管理员](https://www.rabbitmq.com/management-cli.html)（只有在开启[管理插件](https://www.rabbitmq.com/management.html)才能使用）
- 61613, 61614：STOMP 客户端（只有开启 [STOMP 插件](https://www.rabbitmq.com/stomp.html)才能使用）
- 1883, 8883：[MQTT 客户端](https://stomp.github.io/stomp-specification-1.2.html)（只有开启 [MQTT 插件](https://www.rabbitmq.com/stomp.html)才能使用）
- 15674：STOMP-over-WebSockets 客户端（只有启用 [Web STOMP 插件](https://www.rabbitmq.com/web-stomp.html)才能使用）
- 15675：MQTT-over-WebSockets 客户端（只有启用  [Web MQTT 插件](https://www.rabbitmq.com/web-mqtt.html)才能使用）
- 15692：**Prometheus 监控度量**（只有在启用 Prometheus 插件才能使用）

可以配置 RabbitMQ 使用不同的端口和特定的网络接口。

## 集群中的节点

### 什么是备份

所有数据/状态要求 RabbitMQ broker 的备份操作要支持整个节点。除了消息队列，默认情况下，消息队列驻留在一个节点上，尽管它们是可见的，并且可以从所有节点访问到。为了备份集群中的所有结点队列，使用一个支持备份的队列类型。这节涉及到了[选举队列](https://www.rabbitmq.com/quorum-queues.html)和[经典镜像队列](https://www.rabbitmq.com/ha.html)的知识。

### 结点是对等的

一些分布式系统有 leader 和 follower 结点。这对于 RabbitMQ 来说不全然如此。在 RabbitMQ 集群中的所有的结点都是对等的（equal peers）：这里没有特殊的结点。当考虑队列镜像和插件时，这节会变得更加微妙，但是对于大多数意图和目的，所有集群节点都应该被认为是相同的。

大多数命令行工具的操作都能被任意的结点执行。HTTP API 客户端可以针对任何集群节点。

单个插件可以指定(选择)某些节点在一段时间内是“特殊的”。[联合链接](https://www.rabbitmq.com/federation.html)在特定的集群节点上同步（colocated）。如果该节点失败，链接将在另一个节点上重新启动。

在 3.6.7 之前的版本使用使用的转有结点收集状态和聚合。

### CLI 工具如何认证节点(以及节点之间的认证): Erlang Cookie

RabbitMQ 节点和 CLI 工具(例如 rabbitmqctl) 使用 cookie 来确定是否允许它们彼此通信。两个结点之间通信就必须共享相同的密钥，这个密钥称为 Erlang Cookie。这个 cookie 是一个最多到 255 大小的字母数字字符串。它通常存储在本地文件中。这个文件必须只能被自己访问（如拥有 `600` 权限的 UNIX 或类似的）。每个集群结点必须有相同的 cookie。

如果这个文件不存在，Erlang 虚拟机会随机生成一个值，当 RabbitMQ 服务器启动的时候。使用这种生成的 cookie 文件只适用于开发环境。由于每个节点将独立生成自己的值，因此这种策略在集群环境中并不可行。

Erlang cookie 应该在集群部署阶段生成，理想情况下使用自动化和编配工具。

### Cookies 文件位置

#### Linux，MacOS，*BSD

在 UNIX 系统，cookie 文件在 `/var/lib/rabbitmq/.erlang.cookie`（服务端使用的）以及 `$HOME/.erlang.cookie`（CLI 工具使用的）。要注意 `$HOME` 是因用户的不同变化的，因此有必要为每个将使用 CLI 工具的用户放置一个 cookie 文件的副本。这适用于非特权用户和 `root` 用户

RabbitMQ 节点将在引导早期记录有效用户的主目录位置。

#### 社区 Docker 镜像和 Kubernetes

[Docker 社区镜像](https://github.com/docker-library/rabbitmq/)使用 `RABBITMQ_ERLANG_COOKIE` 环境变量设置 cookie 文件。

使用此镜像的配置管理和容器编排工具必须确保集群中的每个 RabbitMQ 节点容器使用相同的值。

在 Kubernetes 的上下文中，该值必须在有状态集的 pod 模板规范中指定。




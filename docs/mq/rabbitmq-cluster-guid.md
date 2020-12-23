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

在 Kubernetes 的上下文中，该值必须在有状态集的 pod 模板规范中指定。可以详见这个[例子](https://github.com/rabbitmq/diy-kubernetes-examples)。

#### Windows

在 windows，cookie 位置依赖于下面几点：

- Erlang 版本：20.2 之前的版本还是 20.2 以上版本
- 是否设置 `HOMEDRIVE` 和 `HOMEPATH` 这两个环境变量

##### Erlang 20.2 或以上

从 20.2 版本开始，cookie 文件位置就在：

- `%HOMEDRIVE%%HOMEPATH%\.erlang.cookie`（通常是 `C:\Users\%USERNAME%\.erlang.cookie`），如果 `HOMEDRIVE` 和 `HOMEPATH` 这两个环境变量赋值了。
- `%USERPROFILE%\.erlang.cookie`（通常在 `C:\Users\%USERNAME%\.erlang.cookie`），如果 `HOMEDRIVE` 和 `HOMEPATH` 都没有赋值。
- 针对 RabbitMQ 的 windows 服务：`%USERPROFILE%\.erlang.cookie`（通常 `C:\WINDOWS\system32\config\systemprofile`）

如果使用了 Windows 服务，cookie 应该从 `C:\Windows\system32\config\systemprofile\.erlang.cookie` 拷贝到用户运行命令行的（如 `rabbitmqctl.bat`） 预期的位置。

##### Erlang 19.3 到 20.2

对于截止到 20.2 的版本，cookie 文件位置在：

- 如果 `HOMEDRIVER` 和 `HOMEPATH` 都赋值了，那么就在 `%HOMEDRIVE%%HOMEPATH%\.erlang.cookie` 中（通常在 `C:\Users\%USERNAME%\.erlang.cookie`）。
- 如果 `HOMEDRIVER` 和 `HOMEPATH` 都没赋值，那么就在 `%USERPROFILE%\.erlang.cookie`（通常在 `C:\Users\%USERNAME%\.erlang.cookie`）。
- 针对 RabbitMQ 的 Windows 服务 —— `%WINDIR%\.erlang.cookie`（通常在 `C:\Windows\.erlang.cookie`）。

如果使用了 RabbitMQ Windows 服务，cookie 应该从 `C:\Windows\system32\config\systemprofile\.erlang.cookie` 拷贝到用户运行命令行的（如 `rabbitmqctl.bat`） 预期的位置。

#### 使用 CLI 和运行时命令行参数覆写

还有另一种方式，可以给 RabbitMQ 结点的 `RABBITMQ_SERVER_ADDITIONAL_ERL_ARGS` 环境变量添加可选项 `-setcookie <value>`：

```cmd
RABBITMQ_SERVER_ADDITIONAL_ERL_ARGS="-setcookie cookie-value"
```

CLI 工具可以用命令行传递一个 cookie 值：

```cmd
rabbitmq-diagnostics status --erlang-cookie "cookie-value"
```

这两者都是最不安全的选项，一般不推荐这么做。

#### 问题解答

当一个结点开始，它将记录其有效用户的主目录位置：

```
node           : rabbit@cdbf4de5f22d
home dir       : /var/lib/rabbitmq
```

除非[服务目录](https://www.rabbitmq.com/relocate.html)被改写了，否则 cookie 文件的目录就不会变，并且如果 cookie 文件不存在，则在第一次引导时由节点创建。

上面例子，cookie 文件路径将会在 `/var/lib/rabbitmq/.erlang.cookie`。

### 身份验证失败

当 cookie 配置错误时（例如配置跟之前的完全相同），RabbitMQ 将会记录一个 `从不允许的节点尝试连接`、`无法自动集群化` 的错误。

例如当用 CLI 工具用一个错误密码来连接以及尝试身份验证时，就会报如下错误信息：

```plaintext
2020-06-15 13:03:33 [error] <0.1187.0> ** Connection attempt from node 'rabbitmqcli-99391-rabbit@warp10' rejected. Invalid challenge reply. **
```

当 CLI 工具（如 `rabbitmqctl`）身份验证 RabbiMQ 失败，消息会提示：

```plaintext
* epmd reports node 'rabbit' running on port 25672
* TCP connection succeeded but Erlang distribution failed
* suggestion: hostname mismatch?
* suggestion: is the cookie set correctly?
* suggestion: is the Erlang distribution using TLS?
```

一个不正确的 cookie 文件地址或 cookie 值无法匹配是出现这个问题的最常见的错误。

当使用最新的 Erlang/OTP 版本时，认证失败包含更多的信息，可以更好地识别 cookie 不匹配：

```ini
* connected to epmd (port 4369) on warp10
* epmd reports node 'rabbit' running on port 25672
* TCP connection succeeded but Erlang distribution failed

* Authentication failed (rejected by the remote node), please check the Erlang cookie
```

关于 CLI 更多信息详见 [CLI 指南](https://www.rabbitmq.com/cli.html)。

### 基于 Cookie 身份证的问题解答

#### 服务端节点

当节点开启时，会记录活动的 user 的主目录路径：

```plaintext
node           : rabbit@cdbf4de5f22d
home dir       : /var/lib/rabbitmq
```

除非[服务端目录](https://www.rabbitmq.com/relocate.html)被改写，查找的 cookie 文件目录如果不存在，节点则会新建一个。

上面的例子，cookie 文件路径在 `/var/lib/rabbitmq/.erlang.cookie`。

#### 主机名解析

因为主机名解析是成功的节点间通信的先决条件。从 RabbitMQ 3.8.6 开始，CLI 工具提供两种命令验证节点上的主机名。这些命令并不是要取代 dig 和其他专门的 DNS 工具，而是在考虑 Erlang 运行时主机名解析器特性的同时，提供一种执行最基本检查的方法。

#### CLT 工具

从 RabbitMQ 3.8.6 开始，`rabbitmq-diagnostics` 包括一个命令，它提供 CLI 工具使用的 Erlang cookie 文件的相关信息：

```bash
rabbitmq-diagnostics erlang_cookie_sources
```

这个命令能报告当前活动的用户，用户主目录以及预期的 cookie 文件路径：

```plaintext
Cookie File

Effective user: antares
Effective home directory: /home/cli-user
Cookie file path: /home/cli-user/.erlang.cookie
Cookie file exists? true
Cookie file type: regular
Cookie file access: read
Cookie file size: 20

Cookie CLI Switch

--erlang-cookie value set? false
--erlang-cookie value length: 0

Env variable  (Deprecated)

RABBITMQ_ERLANG_COOKIE value set? false
RABBITMQ_ERLANG_COOKIE value length: 0
```

## 节点数和选举（Quorum）

因为这些特性（如选举队列，MQTT 客户端追踪）要求在集群的成员之间必须一致，所以推荐奇数个集群节点：1，3，5，7 等等。

集群只有两个节点是非常不合适的，因为在连接丢失的情况下，集群节点不可能识别出大多数节点并形成一致意见。例如，当两个节点丢失连接性时，MQTT 客户端连接将不被接受，选举队列将失去其可用性，等等。

从一致的观点来看，4 个或 6 个节点集群与 3 个或 5 个节点集群具有相同的可用性特征。

**对于支持所有协议消息的客户端，一次只能连接一个节点。**

在节点失败的情况下，客户端能重新不同的节点来恢复它们的拓扑关系和后续的操作。因此，大多数客户端都接受一个终结点列表（主机名和 IP 地址）来作为一个连接操作。如果客户端支持的话，主机列表将在初始连接和连接恢复期间使用。

通过[选举队列](https://www.rabbitmq.com/quorum-queues.html)，客户端将会在队列上执行一个操作，通过选举在线备份数据。

通过经典镜像队列，在某些情况下，客户机可能无法在连接到不同节点后透明地继续操作。这些通常涉及到[托管在失败节点上的非镜像队列](https://www.rabbitmq.com/ha.html#non-mirrored-queue-behavior-on-node-failure)。

## 集群可观察

客户端连接、通道和队列都分布在集器中各个节点。操作者需要能够检查和监控集群中所有节点的状态。

RabbitMQ CLI 工具（如`rabbitmq-diagnostics` 和 `rabbitmqctl`）提供命令行来检查集群的资源以及状态。一些命令（如`rabbitmq-diagnostics environment` 和 `rabbitmq-diagnostics status`）聚焦于单节点的状态，以及其它的集群内的状态。后面的例子包括 `rabbitmqctl list_connections`、`rabbitmqctl list_mqtt_connections`、`rabbitmqctl list_stomp_connections`、`rabbitmqctl list_users`、`rabbitmqctl list_vhosts` 等等。

例如 `cluster-wide` 首先会连接其中一个节点，发现集群的成员并尝试联系它们以及组合它们各自的状态。例如使用 `rabbitmqctl list_connections` 将会连接所有的节点，检索它们的 AMQP 0-9-1 和 AMQP 1.0 连接，并将它们显式给用户。而用户不需要手动的连接所有的节点。假设集群上的节点没有变化（如没有连接被关闭或打开），对两个不同的节点依次执行两个 CLI 命令将产生相同的或语义上相同的结果。"Node-local" 命令不会这样，因为两个节点的状态几乎很少相同：至少，它们的节点名称是不同的。

[管理 UI](https://www.rabbitmq.com/management.html) 的工作类似：一节点负责响应 HTTP API 请求的节点，将散开到其他集群成员并聚合它们的响应。有多个节点的集群要开启管理插件，操作者能使用任意节点来访问管理 UI。使用 HTTP API 收集关于集群状态的数据的监视工具也是如此。不需要依次向每个集群节点发出请求。

### 节点失败处理程序

RabbitMQ 能容忍个别节点的失败。节点能随意的停止和开启，只要它们在关闭的时间点连接一个集群成员节点。

队列镜像允许队列在多个集群节点之间备份。

在集群中也能使用非镜像队列。非镜像队列在节点失败的行为取决于队列的持久化能力。

RabbitMQ 集群有一些处理[网络分区](https://www.rabbitmq.com/partitions.html)的模式，主要面向一致性。集群就意味着横跨局域网（LAN）。不建议在广域网（WAN）运行集群。[Shovel](https://www.rabbitmq.com/shovel.html) 或 [Federation](https://www.rabbitmq.com/federation.html) 是在广域网处理连接 broker 有效的组件。但请注意：[Shovel and Federation 不等价于集群](https://www.rabbitmq.com/distributed.html)。

### 度量和统计

每个阶段存储和聚合了它们自己的状态和度量，并为之提供 API 给其它的节点来访问。一些状态是集群范围内的，其它特殊的在单独的节点中。注意负责响应 HTTP API 请求的节点与对等节点联系以检索它们的数据和它们产生一个聚合结果。

在早于 3.6.7 版本的 RabbitMQ，管理 UI 插件需要使用一个转有的节点来收集状态和聚合。

## 使用 `rabbitmqctl` 集群副本

下面几节将提供跨三台机器手动设置和操作 RabbitMQ 集群的记录：`rabbit1`、`rabbit2`、`rabbit3`。建议在学习更加[自动化友好的集群](https://www.rabbitmq.com/cluster-formation.html)构造手段之前先了解这个例子。

我们假设在三个集器上都记录日志，并且已经在机器上安装了 RabbitMQ，以及 rabbitmq-server 和 rabbitmqctl 脚本都在用户的 PATH 中。

这个副本能在单主机上修改运行，下面做出了详细解释。

## 开启独立节点

集群是通过将现有的 RabbitMQ 节点重新配置为集群配置来设置的。第一步在所有节点以常规模式开启 RabbitMQ：

```bash
# on rabbit1
rabbitmq-server -detached
# on rabbit2
rabbitmq-server -detached
# on rabbit3
rabbitmq-server -detached
```

这个创建了三个独立的 RabbitMQ broker，每个节点都有一个，这个可以通过命令 `cluster_status` 来确定：

```bash
# on rabbit1
rabbitmqctl cluster_status
# => Cluster status of node rabbit@rabbit1 ...
# => [{nodes,[{disc,[rabbit@rabbit1]}]},{running_nodes,[rabbit@rabbit1]}]
# => ...done.

# on rabbit2
rabbitmqctl cluster_status
# => Cluster status of node rabbit@rabbit2 ...
# => [{nodes,[{disc,[rabbit@rabbit2]}]},{running_nodes,[rabbit@rabbit2]}]
# => ...done.

# on rabbit3
rabbitmqctl cluster_status
# => Cluster status of node rabbit@rabbit3 ...
# => [{nodes,[{disc,[rabbit@rabbit3]}]},{running_nodes,[rabbit@rabbit3]}]
# => ...done.
```

RabbitMQ broker 节点的名称从 `rabbitmq-server` shell 脚本开始的，是 `rabbit@*shorthostname`，其中短节点名是小写驼峰式（`rabbit@rabbit1`）。在 Windows，如果 `rabbitmq-server.bat` 批处理文件已经被使用，这个短节点名就是大写的（rabbit@RABBIT1）。当你指定节点名称时，这些字符串一定要精准匹配。

## 创建一个集群

为了在集群中连接三个节点，我们需要告诉其它两个节点，告诉 `rabbit@rabbit2` 和 `rabbit@rabbit3` 加入到第三个的集群中来，它是 `rabbit@rabbit1`。
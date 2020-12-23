# RabbitMQ 配置

RabbitMQ 默认使用内置的配置。这在一些环境中是完全足够的（如开发环境和 QA）。对于其它情况以及[生产部署调试](https://www.rabbitmq.com/production-checklist.html)，在 broker 中有插件的方式来[配置](https://www.rabbitmq.com/plugins.html)很多事情。

这个教程涵盖了下面一些主题的配置：

- 以不同的方式配置服务器以及插件配置
- 配置字段：主要就是 `rabbitmq.conf` 和 可选参数的 `advanced.config` 这两个配置文件
- 在各个平台的配置文件的位置
- 配置的常见问题快照：如何[发现配置文件的位置](https://www.rabbitmq.com/configure.html#verify-configuration-config-file-location)以及[检查和验证有效的配置](https://www.rabbitmq.com/configure.html#verify-configuration-effective-configuration)
- 环境变量
- 操作系统（内核）限制
- 可用的服务器核心设置
- 可用的环境变量
- 怎样加密敏感的配置值

等等还有很多。

因为配置影响系统很多方面，包括插件，个别[文档指南](https://www.rabbitmq.com/documentation.html)更深入地介绍了可以配置的内容。[运行时调优](https://www.rabbitmq.com/runtime.html)是本指南的附加部分，它关注于运行时中的可配置参数。身缠配置清单是一个相关的指南，概述了在大多数生产环境中可能需要调优哪些设置。

## 配置手段

一个 RabbitMQ 节点可以配置使用许多机制来负责不同的领域：

​																									RabbitMQ 配置方式

| 机制                                                         | 描述                                                         |
| ------------------------------------------------------------ | ------------------------------------------------------------ |
| 配置文件                                                     | 包含服务器和插件设置<br />TCP监听器和其它网络设置<br />TLS<br />资源约束（警报）<br />后台鉴权和验证<br />消息存储设置等 |
| 环境变量                                                     | 定义[节点名称](https://www.rabbitmq.com/cli.html#node-names)，文件和目录位置，运行期标志从 shell 设置，或者在环境配置文件中设置，`rabbitmq-env-conf`（Linux、MacOS、BSD） 以及 `rabbitmq-env-conf.bat` |
| rabbitmqctl                                                  | 当使用[系统内的后端鉴权和验证](https://www.rabbitmq.com/access-control.html)时，`rabbitmqctl` 是管理虚拟主机、用户以及授权的工具。它也能用来管理[运行时参数和策略](https://www.rabbitmq.com/parameters.html) |
| [rabbitmq-queues](https://www.rabbitmq.com/cli.html)         | `rabbitmq-queues` 是管理特定[投票](https://www.rabbitmq.com/quorum-queues.html)的工具 |
| [rabbitmq-plugins](https://www.rabbitmq.com/cli.html)        | `rabbitmq-plugins` 是管理[插件](https://www.rabbitmq.com/plugins.html)的工具 |
| [rabbitmq-diagnostics](https://www.rabbitmq.com/cli.html)    | `rabbitmq-diagnostics` 允许查看节点的状态，包括有效的配置，像其它监控和[健康检查](https://www.rabbitmq.com/monitoring.html) |
| [参数和策略](https://www.rabbitmq.com/parameters.html)       | 定义可以在运行时更改的集群范围的设置，以及便于为队列组（如交换机）配置的设置比如包括可选队列参数 |
| [运行期（Erlang VM）标识](https://www.rabbitmq.com/runtime.html) | 控制系统的低级别方面：内存分配设置，节点间通讯缓冲区大小，运行期调度设置等 |
| [操作系统内核限制](https://www.rabbitmq.com/configure.html#kernel-limits) | 控制内核强制限制进程限制：[最大打开文件处理数量](https://www.rabbitmq.com/networking.html#open-file-handle-limit)，大量的进程和内存线程，设置最大存留大小等等 |

**在这些教程中大多数配置设置都是使用第一二个方法。因此着重关注第一二个方法**

## 文件文件

### 介绍

当 RabbitMQ 的一些设置能够通过使用环境变量调整的时候，大多数配置都是可以用[主配置](https://www.rabbitmq.com/configure.html#config-file)文件来设置，通常命名为 `rabbitmq.conf`。这包括核心服务器以及插件的配置。附加的配置文件可用于配置无法在主配置文件格式表示的设置。下面是具体详细

下面这节涵盖了文件的语法和[位置](https://www.rabbitmq.com/configure.html#config-file-location)，在下面例子可以看到更多。

### 配置文件位置

[默认配置文件位置](https://www.rabbitmq.com/configure.html#config-location)因操作系统和[包类型](https://www.rabbitmq.com/download.html)而异。

这个主题详细在余下的指南中有提到。

当我们对 RabbitMQ 的配置文件有疑问时，如下一节所述，请参阅日志文件和/或管理UI。

### 如何查找配置文件位置

一个正在活动的配置文件可以查看 RabbitMQ 日志文件中得到验证。它会在[日志文件](https://www.rabbitmq.com/logging.html) 的最上方显示出来，属于其它 broker 启动日志条目。例如：

```ini
node           : rabbit@example
home dir       : /var/lib/rabbitmq
config file(s) : /etc/rabbitmq/advanced.config
               : /etc/rabbitmq/rabbitmq.conf
```

如果配置文件在 RabbitMQ 找不到或读到，那么日志文件就会提示：

```ini
node           : rabbit@example
home dir       : /var/lib/rabbitmq
config file(s) : /var/lib/rabbitmq/hare.conf (not found)
```

另外，本地结点的配置文件路径，可以使用 [rabbitmq-diagnostics status](https://www.rabbitmq.com/rabbitmq-diagnostics.8.html) 命令：

```bash
# displays key
rabbitmq-diagnostics status
```

以及 `Config files` 节点看起来就像这样：

```plaintext
Config files

 * /etc/rabbitmq/advanced.config
 * /etc/rabbitmq/rabbitmq.conf
```

为了查看指定节点的路径，包括远程连接运行的节点，使用 `-n`（`--node` 的缩写）切换：

```bash
rabbitmq-diagnostics status -n [node name]
```

最后，配置文件就能在 [UI 管理工具](https://www.rabbitmq.com/management.html)找到了，并且还有节点的其它详细信息。

当设置了问题快照配置时，用来[验证有效配置节点](https://www.rabbitmq.com/configure.html#verify-configuration-effective-configuration) 之前，验证配置文件路径是否正确、存在以及能否被加载非常有用（如文件是可读的）。这些步骤一起能帮们快速缩小最常见的错误配置问题。

### 新老配置文件格式化

所有的 RabbitMQ 版本都支持对主配置文件使用 `ini-like,sysctl` 配置文件格式化。这个文件指定了命名 `rabbitmq.conf`。

新的配置文件非常简单，对人是非常可读的，机器也容易生成。与RabbitMQ 3.7.0之前使用的经典配置格式相比，它也相对有限。例如当配置了 [LDAP 支持](https://www.rabbitmq.com/ldap.html)，它就需要深度嵌套数据结构来表示想要的配置。

为了满足这一需求，现代的 RabbitMQ 版本允许在单独的文件中同时使用这两种格式：`rabbitmq.conf` 使用新风格的格式化，并推荐用在大多数设置，并且 `advanced.config` 涉及到更多 ini-配置风格无法表达的高级设置。下面的提到的节点详细信息：

| 配置文件                                             | 使用的格式化                     | 目的                                                         |
| ---------------------------------------------------- | -------------------------------- | ------------------------------------------------------------ |
| rabbitmq.conf                                        | 新风格格式化(sysctl or ini-like) | [主配置文件](https://www.rabbitmq.com/configure.html#config-file). 用于大多数设置。人类可读和利于机器（部署工具）生成。不是每个都能用这个格式化。 |
| advanced.config                                      | 经典 (Erlang terms)              | 有一些配置有一些限制，无法使用新风格的格式化, 如 [LDAP 查询](https://www.rabbitmq.com/ldap.html). 只有在必要的时候才会用这个。 |
| rabbitmq-env.conf (rabbitmq-env.conf.bat on Windows) | 环境变量对                       | 用于在一个地方设置与 RabbitMQ 相关的环境变量                 |

比较以下 `rabbitmq.conf` 文件

```ini
# A new style format snippet. This format is used by rabbitmq.conf files.
ssl_options.cacertfile           = /path/to/ca_certificate.pem
ssl_options.certfile             = /path/to/server_certificate.pem
ssl_options.keyfile              = /path/to/server_key.pem
ssl_options.verify               = verify_peer
ssl_options.fail_if_no_peer_cert = true
```

vs

```erlang
%% A classic format snippet, now used by advanced.config files.
[
  {rabbit, [{ssl_options, [{cacertfile,           "/path/to/ca_certificate.pem"},
                           {certfile,             "/path/to/server_certificate.pem"},
                           {keyfile,              "/path/to/server_key.pem"},
                           {verify,               verify_peer},
                           {fail_if_no_peer_cert, true}]}]}
].
```

### 主配置文件，rabbitmq.conf

配置文件 `rabbitmq.conf` 定义 RabbitMQ 服务器和插件 。从 RabbitMQ 3.7.0 开始，是 `sysctl` 格式化。

这种语法能用三行简短的解释：

- 一个设置单独一行
- 结构如 `Key = Value`
- 所有行都是用 `#` 字符开始一个注释

一个最小的关于配置文件例子：

```ini
# this is a comment
listeners.tcp.default = 5673
```

相同内容在经典配置格式化：

```erlang
%% this is a comment
[
  {rabbit, [
      {tcp_listeners, [5673]}
    ]
  }
].
```

这个例子将修改 RabbitMQ 监听的端口，用于从 5672 到 5673 的AMQP 0-9-1 和 AMQP 1.0 客户端连接。

RabbitMQ 服务器源存储库包含一个名为 `RabbitMQ .conf` 的示例文件。它包含了大部分你想要设置的配置项的例子（省略了一些模糊的），以及那些设置的文档。

文档指南如[网络](https://www.rabbitmq.com/networking.html)、[TLS](https://www.rabbitmq.com/ssl.html)以及[访问控制](https://www.rabbitmq.com/access-control.html)包含很多与之相关的格式化的例子。

注意，请不要将环境变量配置文件 `rabbitmq-env.conf` 和 `rabbitmq-env-conf.bat` 混淆。

为了覆盖主配置文件路径，使用 `RABBITMQ_CONFIG_FILE` 环境变量。用 `.conf` 给新的配置格式化风格作为文件拓展，如 `/etc/rabbitmq/rabbitmq.conf` 或 `/data/configuration/rabbitmq/rabbitmq.conf`。

### advanced.config 文件

使用 systcl 命令对有些配置文件不可能或很难去配置的。例如，它可能会附加额外的配置文件到 erlang 术语格式化（如 `rabbitmq.conf`） 。这文件都被名称 `advanced.conf`。它将与 rabbitmq.conf 中提供的配置合并。
RabbitMQ 服务器源存储库包含一个名为 `advanced.config.example` 的示例文件。它聚焦于指定使用 advanced.confi 设置的选项。

为了覆盖 advanced 配置文件路径，使用 `advanced.config.example` 环境变量。

### rabbitmq.conf、advanced.config 以及 rabbitmq.env.conf  的文件路径

配置文件的位置是指定分布的。RabbitMQ 包或节点不允许创建任何配置文件。用户和发布工具应该按照下面的路径来创建文件：

| **Platform**                                                 | 默认文件目录配置                                             | 配置文件路径例子                                             |
| ------------------------------------------------------------ | ------------------------------------------------------------ | ------------------------------------------------------------ |
| [Generic binary package](https://www.rabbitmq.com/install-generic-unix.html) | $RABBITMQ_HOME/etc/rabbitmq/                                 | $RABBITMQ_HOME/etc/rabbitmq/rabbitmq.conf, $RABBITMQ_HOME/etc/rabbitmq/advanced.config |
| [Debian and Ubuntu](https://www.rabbitmq.com/install-debian.html) | /etc/rabbitmq/                                               | /etc/rabbitmq/rabbitmq.conf, /etc/rabbitmq/advanced.config   |
| [RPM-based Linux](https://www.rabbitmq.com/install-rpm.html) | /etc/rabbitmq/                                               | /etc/rabbitmq/rabbitmq.conf, /etc/rabbitmq/advanced.config   |
| [Windows](https://www.rabbitmq.com/install-windows.html)     | %APPDATA%\RabbitMQ\                                          | %APPDATA%\RabbitMQ\rabbitmq.conf, %APPDATA%\RabbitMQ\advanced.config |
| [MacOS Homebrew Formula](https://www.rabbitmq.com/install-homebrew.html) | ${install_prefix}/etc/rabbitmq/, and the Homebrew cellar prefix is usually /usr/local | ${install_prefix}/etc/rabbitmq/rabbitmq.conf, ${install_prefix}/etc/rabbitmq/advanced.config |

环境变量可以覆写配置文件的位置：

```ini
# overrides primary config file location
RABBITMQ_CONFIG_FILE=/path/to/a/custom/location/rabbitmq.conf

# overrides advanced config file location
RABBITMQ_ADVANCED_CONFIG_FILE=/path/to/a/custom/location/advanced.config

# overrides environment variable file location
RABBITMQ_CONF_ENV_FILE=/path/to/a/custom/location/rabbitmq-env.conf
```

### 何时应用更改后的配置文件

`rabbitmq.conf` 和 `advanced.config` 发生的更改在节点重启之后生效。

如果 `rabbitmq-env.conf` 不存在，它能通过 `RABBITMQ_CONF_ENV_FILE ` 指定的路径下手动创建。在 Windows 系统，它命名为 `rabbitmq-env-conf.bat`。

如果配置文件路径或在 `rabbitmq-env-conf.bat` 的任何值发生改变，Windows 服务用户需要安装重新安装服务。否则引用环境变量的服务不会更新。

在自动化部署上下文中，意味着如 `RABBITMQ_BASE` 和 `RABBITMQ_CONFIG_FILE` 环境变量应该要在 RabbitMQ 安装时就要设置。这可以避免不必要的困惑和重装 Windows 服务。

### 如何查看和验证运行中的节点的有效配置

使用 `rabbitmq-diagnostics` 命令查看打印有效的配置信息（用户从所有配置文件提供的值都合并到默认配置文件）

```bash
# inspect effective configuration on a node
rabbitmq-diagnostics environment
```

检查指定节点的有效配置，包含远程正在运行的节点，使用 `-n` 切换指定节点：

```bash
rabbitmq-diagnostics environment -n [node name]
```

上面的命令将会打印在运行节点上的每个应用正在应用的配置信息（RabbitMQ、插件、库）。有效的节点信息是通过下面两部计算的：

- `rabbitmq.conf` 转换到内部使用的配置（advance）格式。这些配置信息都合并至默认。
- `advanced.config` 如果提供了就会在家并合并到上面第一步的结果中。

有效配置是通过配置文件路径一起来确认的。这两步能快速缩小大多数常见的错误配置问题。

### RabbitMQ.config 文件

在 RabbitMQ 3.7.0 之前，它允许配置文件命名为 `rabbitmq.config` 以及今日 `advanced.config` 使用的相同 Erlang 术语格式。这个格式仍支持向后兼容。

经典格式现在是弃用的。请在 `rabbitmq.conf` 以及 `advanced.config` 优先使用新风格格式化。

为了在经典格式化中使用配置文件，导出 `RABBITMQ_CONFIG_FILE` 到拓展为 `.config` 的配置文件。这个拓展表明 RabbitMQ 将这个文件视作一个经典格式化文件处理。

[一个配置文件的例子](https://github.com/rabbitmq/rabbitmq-server/blob/v3.7.x/docs/rabbitmq.config.example)命名为 `rabbitmq.config.example`。它包含了在经典格式化配置大多数配置项的例子。

为了覆写主配置文件的路径，使用 `RABBITMQ_CONFIG_FILE` 环境变量。使用 `.config` 作为经典格式化配置的文件拓展。

经典格式化配置的使用应该只受限于 [advanced.config 文件](https://www.rabbitmq.com/configure.html#advanced-config-file)，那些 `ini 风格` 配置文件的设置是无法配置的。

### 配置文件例子

RabbitMQ 服务资源仓库包含了下面配置文件的例子：

- [rabbitmq.conf.example](https://github.com/rabbitmq/rabbitmq-server/blob/master/docs/rabbitmq.conf.example)
- [advanced.config.example](https://github.com/rabbitmq/rabbitmq-server/blob/master/docs/advanced.config.example)

这个两个文件包含了大多数关键配置，还有简短的解释说明。所有的配置都标注了注释，你可以根据需要取消注释。注意，这是个例子，所以一般情况下是不建议直接用于生产环境的。

在大多数发行版的例子文件都放置在与真是文件相同的路径里。在 Debian 和 RPM 的发行版中由于策略的不允许，例子文件放置在了 `/usr/share/doc/rabbitmq-server/` 或 `/usr/share/doc/rabbitmq-server-3.8.9/` 中。

### rabbitmq.conf  中可配置的核心服务变量

这里有一些通用的变量。这个列表不包含全部，因为有些设置很模糊。

| 键                                     | 文档                                                         |
| -------------------------------------- | ------------------------------------------------------------ |
| listeners                              | 监听“plain” AMPQ 0-9-1 以及 AMPQ 1.0 连接的端口号或主机名/对。详细的例子详见 [Networking 指南](https://www.rabbitmq.com/networking.html) <br />默认监听: <br />`listeners.tcp.default = 5672 ` |
| num_acceptors.tcp                      | TCP 监听的将接收连接的 Erlang 进程数 <br />默认：<br /> `num_acceptors.tcp = 10 ` |
| handshake_timeout                      | AMQP 0-9-1 握手的最长时间 (在 socket 握手和 TLS 连接之后)， 毫秒单位<br />默认：<br /> `handshake_timeout = 10000 ` |
| listeners.ssl                          | 监听开启了 TLS 的 AMQP 0-9-1 以及 AMQP 1.0 连接的端口或主机/对，详细理解详见 [TLS 指南](https://www.rabbitmq.com/ssl.html) <br />默认： none |
| num_acceptors.ssl                      | 将会从客户端接收 TLS 连接的 Erlang 进程数<br />默认：`num_acceptors.ssl = 10 ` |
| ssl_options                            | TLS 配置。详见 [TLS 指南](https://www.rabbitmq.com/ssl.html#enabling-ssl)<br />默认： `ssl_options = none ` |
| ssl_handshake_timeout                  | TLS 握手超时时间，毫秒<br /> 默认：`ssl_handshake_timeout = 5000 ` |
| vm_memory_high_watermark               | 内存控制流程触发的阈值，可是绝对或相对于 OS 的 RAM 可用量：<br />`vm_memory_high_watermark.relative = 0.6 ` <br />`vm_memory_high_watermark.absolute = 2GB ` <br />详见 [基于内存流程控制](https://www.rabbitmq.com/memory.html) 以及 [警报](https://www.rabbitmq.com/alarms.html) 文档。<br />默认：`vm_memory_high_watermark.relative = 0.4 ` |
| vm_memory_calculation_strategy         | 内存使用报表策略。可以是下面中的一个：<br />1. `allocated`: 使用 Erlang 内存分配器统计信息 <br />2. `rss`: 使用操作系统 RSS 内存报报告，这使用特定于操作系统的方法，可以启动短生命周期的子进程。<br />3. `legacy`: 使用遗留内存报告 (要考虑在运行时到底使用了多少内存)。这种策略是不准确的。<br />4. `erlang`: 由于历史遗留问题，要提供一个向后兼容的能力<br />Default: `vm_memory_calculation_strategy = allocated ` |
| vm_memory_high_watermark_paging_ratio  | 由于内存的高水位阈值的限制，队列开始将消息分页存储到磁盘来释放内存。详见[基于内存流程控制](https://www.rabbitmq.com/memory.html)文档<br />Default: `vm_memory_high_watermark_paging_ratio = 0.5 `，这个意思就是说当内存使用到服务器总内存的 50% 时，会引起服务器 GC |
| total_memory_available_override_value  | 为了尽可能的修改可使用的总内存，而不是使用操作系统特定的方法从环境中推断。只有当节点可用的实际最大 RAM 数量与节点推断的值不匹配时，才应该使用此选项，如由于容器化或类似的约束，节点无法感知。值可以设置 byte 的整数范围，或者是设置单位信息（如 `8GB`）。例如当值设置位 4GB 时，节点就会认为这是运行在只有 4GB 内存的 RAM 的机器。<br />Default: undefined （没有使用或赋值） |
| disk_free_limit                        | 银盘释放的 RabbitMQ 存储数据的空间。当磁盘可用空间低于这个限制值时，控制流程就会触发。值可以设置相对于 ROM 总内存的值（如 `50MB` 或 `5GB`）也可以以字节为绝对值，或者是单元信：<br />`disk_free_limit.relative = 3.0` <br />`disk_free_limit.absolute = 2GB` <br />默认空闲空间必须大于 50MB。详见 [磁盘警告](https://www.rabbitmq.com/disk-alarms.html) 文档。<br />Default: `disk_free_limit.absolute = 50MB ` |
| log.file.level                         | 控制日志记录的粒度（granularity）。值是日志事件分类和日志级别对的集合。<br />级别可以是 `error`（只记录错误日志），`warning` (只记录警告和错误)，`info`（错误，警告，information 以及调试都会记录），或 `debug`（errors, warnings, informational 以及 debug 都会记录）<br />Default: `log.file.level = info ` |
| channel_max                            | 允许与客户端协商的最大通道数，不包括协议中使用的特殊通道数 0。 0 代表无限制，当发生通道泄露时，设置这个值非常危险。使用更多通道会增加 broker 的内存占用。<br />Default: `channel_max = 2047 ` |
| channel_operation_timeout              | 通道操作超时(以毫秒为单位)(内部使用，由于消息传递协议的差异和限制，没有直接暴露给客户端)。<br />Default: `channel_operation_timeout = 15000 ` |
| max_message_size                       | 允许的最大消息负载大小(以字节为单位)。较大的消息将被拒绝，并有一个合适的通道异常。<br />Default: `134217728` <br />Max value: `536870912` |
| heartbeat                              | 表示服务器在连接期间，建议的心跳超时的值。如果设置为 0 就代表禁用心跳检查 (不建议)。详见 [Heartbeats 指南](https://www.rabbitmq.com/heartbeats.html).。<br />Default: `heartbeat = 60 ` |
| default_vhost                          | 当 RabbitMQ 从零开始创建一个新数据库时创建的虚拟主机。 交换机 `amq.rabbitmq.log` 在这个虚拟主机中就已经存在。<br />Default: `default_vhost = / ` |
| default_user                           | 当 RabbitMQ 从零开始创建一个新数据库时要创建的用户名。<br />Default: `default_user = guest ` |
| default_pass                           | `default_user` 的密码。<br />Default: `default_pass = guest ` |
| default_user_tags                      | `default_user` 的标签<br />Default: `default_user_tags.administrator = true ` |
| default_permissions                    | [Permissions](https://www.rabbitmq.com/access-control.html) 当创建它的时候，用户的默认许可权限集合<br />Default: <br />default_permissions.configure = .*<br />default_permissions.read = .*<br />default_permissions.write = .* （每个选项占一行） |
| loopback_users                         | 只允许通过回路接口（[loopback](https://baike.baidu.com/item/loopback/2779210?fr=aladdin)）连接到代理的用户列表 (如 `localhost`)。允许默认的 `user` 用户远程连接 ([不适合生产使用的安全措施](https://www.rabbitmq.com/production-checklist.html))，赋值为 none: <br /># awful security practice, <br /># consider creating a new <br /># user with secure generated credentials! <br />loopback_users = none <br />要限制另一个用户只能使用 localhost-only 连接，就像下面这样做(`monitoring` 是用户的名称):<br />`loopback_users.monitoring = true `  <br />默认内容: <br /># guest uses well known <br /># credentials and can only <br /># log in from localhost <br /># by default <br />loopback_users.guest = true |
| cluster_formation.classic_config.nodes | Classic [peer discovery](https://www.rabbitmq.com/cluster-formation.html) backend's list of nodes to contact. For example, to cluster with nodes `rabbit@hostname1` and `rabbit@hostname2` on first boot: `cluster_formation.classic_config.nodes.1 = rabbit@hostname1 cluster_formation.classic_config.nodes.2 = rabbit@hostname2 ` Default: `none` (not set) |
| collect_statistics                     | Statistics collection mode. Primarily relevant for the management plugin. Options are: `none` (do not emit statistics events) `coarse` (emit per-queue / per-channel / per-connection statistics) `fine` (also emit per-message statistics)  Default: `collect_statistics = none ` |
| collect_statistics_interval            | Statistics collection interval in milliseconds. Primarily relevant for the [management plugin](https://www.rabbitmq.com/management.html#statistics-interval). Default: `collect_statistics_interval = 5000 ` |
| management_db_cache_multiplier         | Affects the amount of time the [management plugin](https://www.rabbitmq.com/management.html#statistics-interval) will cache expensive management queries such as queue listings. The cache will multiply the elapsed time of the last query by this value and cache the result for this amount of time. Default: `management_db_cache_multiplier = 5 ` |
| auth_mechanisms                        | [SASL authentication mechanisms](https://www.rabbitmq.com/authentication.html) to offer to clients. Default: `auth_mechanisms.1 = PLAIN auth_mechanisms.2 = AMQPLAIN ` |
| auth_backends                          | List of [authentication and authorisation backends](https://www.rabbitmq.com/access-control.html) to use. See the [access control guide](https://www.rabbitmq.com/access-control.html) for details and examples.  Other databases than `rabbit_auth_backend_internal` are available through [plugins](https://www.rabbitmq.com/plugins.html). Default: `auth_backends.1 = internal` |
| reverse_dns_lookups                    | Set to `true` to have RabbitMQ perform a reverse DNS lookup on client connections, and present that information through `rabbitmqctl` and the management plugin. Default: `reverse_dns_lookups = false` |
| delegate_count                         | Number of delegate processes to use for intra-cluster communication. On a machine which has a very large number of cores and is also part of a cluster, you may wish to increase this value. Default: `delegate_count = 16` |
| tcp_listen_options                     | Default socket options. You probably don't want to change this. Default: `tcp_listen_options.backlog = 128 tcp_listen_options.nodelay = true tcp_listen_options.linger.on = true tcp_listen_options.linger.timeout = 0 tcp_listen_options.exit_on_close = false ` |
| hipe_compile                           | Do not use. This option is no longer supported. HiPE supported has been dropped starting with Erlang 22. Default: `hipe_compile = false` |
| cluster_partition_handling             | How to handle network partitions. Available modes are: ignore autoheal pause_minority pause_if_all_down pause_if_all_down mode requires additional parameters: nodes recover See the [documentation on partitions](https://www.rabbitmq.com/partitions.html#automatic-handling) for more information. Default: `cluster_partition_handling = ignore` |
| cluster_keepalive_interval             | How frequently nodes should send keepalive messages to other nodes (in milliseconds). Note that this is not the same thing as [net_ticktime](https://www.rabbitmq.com/nettick.html); missed keepalive messages will not cause nodes to be considered down. Default: `cluster_keepalive_interval = 10000 ` |
| queue_index_embed_msgs_below           | Size in bytes of message below which messages will be embedded directly in the queue index. You are advised to read the [persister tuning](https://www.rabbitmq.com/persistence-conf.html) documentation before changing this. Default: `queue_index_embed_msgs_below = 4096 ` |
| mnesia_table_loading_retry_timeout     | Timeout used when waiting for Mnesia tables in a cluster to become available. Default: `mnesia_table_loading_retry_timeout = 30000 ` |
| mnesia_table_loading_retry_limit       | Retries when waiting for Mnesia tables in the cluster startup. Note that this setting is not applied to Mnesia upgrades or node deletions. Default: `mnesia_table_loading_retry_limit = 10 ` |
| mirroring_sync_batch_size              | Batch size used to transfer messages to an unsynchronised replica (queue mirror). See [documentation on eager batch synchronization](https://www.rabbitmq.com/ha.html#batch-sync). Default: `mirroring_sync_batch_size = 4096 ` |
| queue_master_locator                   | Queue master location strategy. Available strategies are: min-masters client-local random See the [documentation on queue master location](https://www.rabbitmq.com/ha.html#queue-master-location) for more information. Default: `queue_master_locator = client-local ` |
| proxy_protocol                         | If set to true, RabbitMQ will expect a [proxy protocol](http://www.haproxy.org/download/1.8/doc/proxy-protocol.txt) header to be sent first when an AMQP connection is opened. This implies to set up a proxy protocol-compliant reverse proxy (e.g. [HAproxy](http://www.haproxy.org/download/1.8/doc/proxy-protocol.txt) or [AWS ELB](http://docs.aws.amazon.com/elasticloadbalancing/latest/classic/enable-proxy-protocol.html)) in front of RabbitMQ. Clients can't directly connect to RabbitMQ when proxy protocol is enabled, so all connections must go through the reverse proxy. See [the networking guide](https://www.rabbitmq.com/networking.html#proxy-protocol) for more information.  Default: `proxy_protocol = false ` |
| cluster_name                           | Operator-controlled cluster name. This name is used to identify a cluster, and by the federation and Shovel plugins to record the origin or path of transferred messages. Can be set to any arbitrary string to help identify the cluster (eg. london). This name can be inspected by AMQP 0-9-1 clients in the server properties map. Default: by default the name is derived from the first (seed) node in the cluster. |

### 配置值加密

在 RabbitMQ 配置文件中有敏感的条目（比如密码，包含凭证的 URL）项是可以进行加密的。broker 在开始的时候可以加密解密。但并不是说在配置文件使用加密就能使系统安全，不过，它们允许 RabbitMQ 的部署遵循各国的规定，即配置文件中不应以纯文本形式显示敏感数据。

要加密的值必须放在 Erlang `encrypted` 元组中：`{encrypted, ...}`。这里有在配置文件中默认是加密的例子：

```erlang
[
  {rabbit, [
      {default_user, <<"guest">>},
      {default_pass,
        {encrypted,
         <<"cPAymwqmMnbPXXRVqVzpxJdrS8mHEKuo2V+3vt1u/fymexD9oztQ2G/oJ4PAaSb2c5N/hRJ2aqP/X0VAfx8xOQ==">>
        }
      },
      {config_entry_decoder, [
             {passphrase, <<"mypassphrase">>}
         ]}
    ]}
].
```

要注意其中的 `config_entry_decoder` 的密码和密钥，RabbitMQ 将使用它解密加密的值。

这个 `passphrase` 的值不应该硬编码到配置文件中，它应该在单独的文件中：

```erlang
[
  {rabbit, [
      %% ...
      {config_entry_decoder, [
             {passphrase, {file, "/path/to/passphrase/file"}}
         ]}
    ]}
].
```

RabbitMQ 开始时会使用 `{passphrase, prompt}` 请求一个操作将 passphrase 输入进来。

使用 [rabbitmqctl](https://www.rabbitmq.com/cli.html) 以及 `encode` 命令来加密值:

```bash
rabbitmqctl encode '<<"guest">>' mypassphrase
{encrypted,<<"... long encrypted value...">>}
rabbitmqctl encode '"amqp://fred:secret@host1.domain/my_vhost"' mypassphrase
{encrypted,<<"... long encrypted value...">>}
```

在 Windows 平台中

```powershell
rabbitmqctl encode "<<""guest"">>" mypassphrase
{encrypted,<<"... long encrypted value...">>}
rabbitmqctl encode '"amqp://fred:secret@host1.domain/my_vhost"' mypassphrase
{encrypted,<<"... long encrypted value...">>}
```

添加 `decode` 来解密

```bash
rabbitmqctl decode '{encrypted, <<"...">>}' mypassphrase
<<"guest">>
rabbitmqctl decode '{encrypted, <<"...">>}' mypassphrase
"amqp://fred:secret@host1.domain/my_vhost"
```

WIndows:

```powershell
rabbitmqctl decode "{encrypted, <<""..."">>}" mypassphrase
<<"guest">>
rabbitmqctl decode "{encrypted, <<""..."">>}" mypassphrase
"amqp://fred:secret@host1.domain/my_vhost"
```

不同类型的值都能被加密。上面的示例编码了二进制文件(`<<"guest">>`) 和字符串(`"amqp://fred:secret@host1.domain/my_vhost"`)。

或者使用[命令行工具](https://www.rabbitmq.com/cli.html):

```bash
rabbitmqctl encode --cipher blowfish_cfb64 --hash sha256 --iterations 10000 \ '<<"guest">>' mypassphrase
```



windows:

```powershell
rabbitmqctl encode --cipher blowfish_cfb64 --hash sha256 --iterations 10000 \ "<<""guest"">>" mypassphrase
```

### 配置使用的环境白能量

某些服务器参数可以使用环境变量进行配置：[节点名称](https://www.rabbitmq.com/cli.html#node-names)，RabbitMQ [配置文件路径](https://www.rabbitmq.com/configure.html#configuration-files)，[内部节点通信端口](https://www.rabbitmq.com/networking.html#ports)，Erlang VM 标志等

### 路径于目录名称限制

一些环境变量配置路径和位置(节点的基目录或数据目录、[插件源目录和扩展目录](https://www.rabbitmq.com/plugins.html)，等等)。这些路径必须排除一些字符:

- `*` 和`?` (在Linux、macOS、BSD和其他类unix系统上)
- `^` and `!` (Windows 平台)
- `[` 和 `]`
- `{` 和 `}`

上述字符会使该节点无法按预期启动或工作（如展开插件并加载它们的元数据）


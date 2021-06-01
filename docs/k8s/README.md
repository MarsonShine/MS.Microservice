# K8S 相关资料

官网社区：https://kubernetes.io/docs/home/

中文社区：http://docs.kubernetes.org.cn

书籍：

1. [Kubernetes 指南](https://kubernetes.feisky.xyz/#ben-shu-ban-ben-geng-xin-ji-lu)（次数更新涉及到的版本为 v1.11 版本，很陈旧了）
2. 《Kubernetes in Action》

# Kubernet 资源三个结构

## metadata

这主要包括**名称、命名空间、标签和关于该容器的其他信息**

## spec

主要包含 pod 的内容实际说明，例如 pod 的容器、卷以及其他数据

## status

包含当前运行中的 pod 的状态信息，例如 pod 所处的条件、每个容器的描述和状态，以及内部 IP 和其他基本信息

# 初学遇到的问题

##  创建 pod 遇到的问题

在启动一个 pod 时，直接运行以下命令：

```bash
kubectl run basicdata --image=basicdata:latest --port=9000
```

这段命令 k8s 会驱使 doker 去镜像仓库拉取对应的 `basicdata:latest` 的镜像，但是这个镜像由于本地的，还没有 push 到远程仓库中。所以当执行查询 pod 状态信息时，会出现以下错误状态：

```bash
kubectl get pods # 查询所有 pod 状态
```

结果如下：

```bash
NAME        READY   STATUS    RESTARTS   AGE
basicdata   1/1     ImagePullBackOff   0          23m
```

要想知道 pods 的详细信息，则可以执行以下命令：

```bash
kubectl get pods -o wide
```

这是拉取镜像时出现未知错误，要想查看具体哪一步出了问题，可以执行以下命令：

```bash
kubectl describe pod
```

返回的就是具体哪一步出现了错误。

**解决方案**：

这是因为 k8s 默认的镜像拉取策略时远程为主的。所以我们可以修改其配置项 `imagePullPolicy`，而 windows 可以直接在创建 pod 时指定拉取策略 `--image-pull-policy Never`，或者是 `--image-pull-policy IfNotPresent`：

```
kubectl run basicdata --image=basicdata:latest --port=9000 --image-pull-policy Never
```

这个时候 pod 是运行状态了，那么此时我们还不能通过 localhost:port 访问，因为 k8s 还没有暴露对外访问的端口。可以通过 service 和 `kubectl expose` 实现，具体详见：https://kubernetes.io/docs/tasks/administer-cluster/access-cluster-services/

具体命令如下：

```bash
 kubectl expose rc basicdata --type=LoadBalancer --name basicdata-tcp
```

> 注意，上述命令只能在建立 replicationcontroller 之后才有效果。

## 直接访问 pod bash

```bash
 kubectl exec -it basicdata-2fzx8 -- bash
```

## 如何横向拓展 pod 节点

我们可以通过创建一个 basicdata 的 replicationcontroller。首先我们定义一个 replicationcontroller.yml 文件

```yaml
apiVersion: v1
kind: ReplicationController
metadata:
  name: basicdata
spec:
  replicas: 3
  selector:
    app: basicdata
  template:
    metadata:
      name: basicdata
      labels:
        app: basicdata
    spec:
      containers:
      - name: basicdata
        image: basicdata:latest
        imagePullPolicy: IfNotPresent
        ports:
        - containerPort: 9000
```

再执行以下命令：

```bash
kubectl apply -f .\replicationcontroller.yml
```

## 关于 kubernet dashboard

kubectl 默认启动 pod 是不带 dashboard 的，所以我们要新起一个 dashboard，具体措施详见 https://github.com/kubernetes/dashboard/blob/master/docs/user/access-control/creating-sample-user.md

首先准备两个用户权限文件：

```yaml
# admin-user.yml
apiVersion: v1
kind: ServiceAccount
metadata:
  name: admin-user
  namespace: kubernetes-dashboard
  
# cluster-role-binding.yml
apiVersion: rbac.authorization.k8s.io/v1
kind: ClusterRoleBinding
metadata:
  name: admin-user
roleRef:
  apiGroup: rbac.authorization.k8s.io
  kind: ClusterRole
  name: cluster-admin
subjects:
- kind: ServiceAccount
  name: admin-user
  namespace: kubernetes-dashboard
```

具体步骤如下：

```yaml
# 1
kubectl apply -f https://raw.githubusercontent.com/kubernetes/dashboard/v2.2.0/aio/deploy/recommended.yaml
# 2
kubectl proxy
# 3
kubectl apply -f ./admin-user.yml
# 4
kubectl apply -f ./cluster-role-binding.yml
# 5
kubectl -n kubernetes-dashboard get secret $(kubectl -n kubernetes-dashboard get sa/admin-user -o jsonpath="{.secrets[0].name}") -o go-template="{{.data.token | base64decode}}"
# 6 拷贝 #5 输出的 token 填入 dashboard 的 token 输入栏即可
# 7 如果要删除
kubectl -n kubernetes-dashboard delete serviceaccount admin-user
kubectl -n kubernetes-dashboard delete clusterrolebinding admin-user
```

## 如何查看已经创建的 pod 的 yaml 文件内容

```bash
kubectl get po basicdata-8znt9 -o yaml
```

## 如何给大量的 pod 分组

可以通过设置标签 `labels`

```yaml
apiVersion: v1
kind: Pod
metadata:
  name: basicdata-label
  labels:	# 设置标签
    creation_method: manual
    env: dev
spec:
  containers:
  - image: basicdata:latest # 创建容器所用的镜像
    imagePullPolicy: IfNotPresent # 本地拉取，如果本地没有则拉取远程镜像仓库
    name: basicdata # 容器名称
    ports:
    - containerPort: 9000 # pod 开启应用监听的端口
      protocol: TCP
```

查询带有标签的 pods 信息：

```bash
kubectl get pods --show-labels
# 查询指定标签,显示成列
kubectl get po -L creation_method,env
# 只查询带有指定标签的 pod
kubectl get po -l creation_method,env
kubectl get po -l '!env'  # 查询非 env 标签的 pod
```

修改已有的 pod 的标签：

```bash
kubectl label po basicdata env=staging --overwrite
```

## 除了标签还有命名空间可以分组

创建命名空间

```
kubectl create -f .\custom-namespace.yml
```

```yml
apiVersion: v1
kind: Namespace
metadata: 
  name: custom-namespace
```

也可以直接通过命令

```bash
kubectl create namespace custom-namespace
# 也可以直接在创建pod时就添加命名空间
kubectl create -f basicdata-manual.yml -n custom-namespace
```

## kubectl 命令重复很长的命令，可以设置别名

如 `alias kcd= 'kubectl config set-context $(kubectl config currentcontext) --namespace'`，然后可以直接这样使用：

`kcd some-namespace` 快捷的进行命名空间切换了

> 注意：尽管命名空间将对象分隔到不同的组，只允许你对属于特定命名空间的对象进⾏操作，但实际上命名空间之间并不提供对正在运⾏的对象的任何隔离。

## 删除 pod

```bash
kubectl delete po podName1,podName2 # 删除指定 pod
```

按标签批量删除 pod

```bash
kubectl delete po -l env=dev # 删除环境变量位 dev 的 pod
```

通过命名空间删除 pod

```bash
kubectl delete ns custom-namespace	# 删除 custom-namespace 命名空间的所有内容
```

删除所有的 pod

```bash
kebectl delete pod --all
```

> 通过 rc 控制的 pod 经过上面的删除操作是无法删除所有的 pod 的，因为 rc 会始终创建 pod。所以我们要想完全的删除这些资源，就还得删除 rc。也可以直接执行 `kubectl delete all --all`

## 设置 Pod 健康检查

```yml
apiVersion: v1
kind: Pod
metadata:
  containers:
  - image: basicdata:latest # 创建容器所用的镜像
    imagePullPolicy: IfNotPresent # 本地拉取，如果本地没有则拉取远程镜像仓库
    name: basicdata-unhealthy # 容器名称
    livenessProbe: # 存活探针
      httpGet:  # http get 类型的探针
        path: /
        port: 9000


# 存活探针默认的设置是定期调用指定路径，如果超过 5 次 http 状态码不是 20x/3xx 就会重启 pod
```

启动之后，可以用命令 `kubectl describe po basicdata-unhealthy` 查看其中的节点 `Liveness` 这个存活探针的工作流程。

> **在设置探针的时候要注意，一般默认会有初始化延迟时间的，也就是 `initialDelaySeconds: 15`。因为如果设置 0 就代表立即启动健康检查探测，这个时候容器还没启动，所以就会导致探测失败。**

在设置探针的时候要注意**职责分离**，举个例子，如果服务端失败是因为数据库连接失败，那么即使是多次重启（在数据库恢复之前）是无用的。又如后端出现问题，前端服务器的探针应该不受影响。并且一定要是**轻量级**的。

## 关于 ReplicationController

rc 运行过程包括三个模块：

- 标签选择器 label selector
- replica count 副本个数，指定应运行的 pod 数量
- pod template pod 模板，用于创建新的 pod 模板副本。**模板仅影响由此 ReplicationController 创建的新pod。**

## 删除 RC 会自动删除受 RC 管理的 Pod 么

因为 Pod 是受 RC 管理的（指由 RC 创建的 Pods），那么是不是只要直接删除 RC 就会自动删除那些 Pods 呢？

```bash
kubectl delete rc basicdata-rc
```

上面命令是会自动删除对应的管理的 pod 的。但是能只删除现有的 rc 而不影响现有正在运行的 pod 么？其实也有的，增加选项参数 `--cascade=false` 就代表只是删除 rc 不删除 pod，即将对应的 pods 不受 rc 的控制。

```bash
kubectl delete rc basicdata-rc --cascade=false	# 删除之后，对应的 pod 就独立了，即人为删除是不会重建的
```

## 关于 ReplicaSet（请停止使用 ReplcationController）

根据 kubernet 的建议，后续会删除 replicationcontroller，应该始终使用 replicaset 代替。使用方式几乎完全一样。

定义 ReplicaSet

```yaml
apiVersion: apps/v1 # ReplicaSet 不是 api v1 的一部分，所以要指定正确的 api version
kind: ReplicaSet
metadata:
  name: basicdata-replicaset
spec:
  replicas: 3
  selector: 
    matchLabels: # matchLabels 选择器,与 rc 不同
      app: basicdata-rc
  template:
    metadata:
      labels:
        app: basicdata-rc
    spec:
      containers:
      - name: basicdata
        image: basicdata:latest
        imagePullPolicy: IfNotPresent
```

### ReplicaSet 标签匹配的优势：标签表达式

```yaml
... 
  selector: 
    matchExpressions:
      - key: app
        operator: In
        values:
          - basidata-rc
...
```

每个表达式必须包括：

1. key
2. operator，有四个运算符
   1. In：Label 的值必须与其中⼀个指定的 values 匹配
   2. NotIn：Label 的值与任何指定的 values 不匹配。
   3. Exists：pod 必须包含⼀个指定名称的标签（值不重要）。**注意，指定了这个标识符，就不应该标明 values**
   4. DoesNotExist：pod 不得包含有指定名称的标签。**并且 values 属性不能指定**
3. values

如果同时指定 matchLabels 和 matchExpressions，则所有标签都必须同时匹配这两个条件。

### 删除 rs

```bash
kubectl delete rs basicdata-replicaset
```

## 关于 DaemonSet

因为它的⼯作是确保**⼀个 pod 匹配它的选择器并在每个节点上运⾏。即每个节点上运行一个 pod**。其副本是在节点上随机分布的。

```yaml
apiVersion: apps/v1
kind: DaemondSet
metadata:
  name: ssd-monitor
spec:
  selector:
    matchLabels:
      apps: ssd-monitor
    template:
      metadata:
        labels:
          app: ssd-monitor
      spec:
        nodeSelector:
          disk: ssd # 节点选择器，选择 disk=ssd 标签的节点
        constainers:
        - name: main
          image: luksa/ssd-monitor
```

这就代表了选择每个带有 disk=ssd 标签的节点上部署一个 pod。

## 关于定时任务 pod（Job）

像定时作业不应该像之前的 pod 一样一直保持启动状态。有些 pod 如完成特定的作业，当这个作业的 pod 完成时就关闭无需重新开启。这个时候我们就需要开启 Job 类型的 pod

```yaml
apiVersion: batch/v1
kind: Job
metadata:
  name: batch-job
spec:
  template:
    metadata:
      labels:
        app: batch-job
    spec:
      restartPolicy: OnFailure  # 定义重启策略
      containers:
      - name: main
        image: luksa/batch-job
```

要注意的是其中的 `restartPolicy`，默认是 `Always`，但是我们不需要它无限重启，而是正常完成就可以关闭。所以我们要设置成 `Onfailure`。还有一个值是 `Never`。

可以设置 `completions` 以及 `parallelism` 属性来实现串行还是并行完成作业。

### 横向伸缩 Job

```bash
kubectl scale job multi-completion-batch-job --replicas 3
```

效果就是将 parallelism 变为 3，另外的 pod 就会立即启动。

## 直连 pod 执行 cmd 命令

```bash
kubectl exec basicdata-8znt9 -- curl -s http://10.111.206.238
```

> 双横杠（--）代表着 kubectl 命令项的结束。在两个横杠之后的内容是指在 pod 内部需要执⾏的命令。

## 创建对外访问的服务

### NodePart

通过创建 NodePort 服务，可以让 Kubernetes 在其所有节点上保留⼀个端口（所有节点上都使⽤相同的端口号），并将传⼊的连接转发给作为服务部分的 pod。

即：连接集群外部的服务通讯

```yml
apiVersion: v1
kind: Service
metadata:
  name: basicdata-nodepart
spec:
  type: NodePort
  ports:
  - port: 8000
    targetPort: 9000
    nodePort: 30123 # 通过集群节点的 30123 端口可以访问该服务
  selector:
    app: basicdata
```

> 在通信时要注意可能会因为防火墙的原因通信失败

### LoadBlancer

```yml
apiVersion: v1
kind: Service
metadata:
  name: basicdata-loadbalancer
spec:
  type: LoadBalancer
  ports:
  - port: 9001	# 如果端口被其它程序占用则会失败
    targetPort: 9000
  selector:
    app: basicdata
```

### Ingress

通过创建 Ingress 服务也能对外部访问服务。

⼀个重要的原因是每个LoadBalancer 服务都需要⾃⼰的负载均衡器，以及独有的公有 IP 地址，**⽽ Ingress 只需要⼀个公⽹ IP 就能为许多服务提供访问。**

**只有 Ingress 控制器在集群中运⾏，Ingress 资源才能正常⼯作。**不同的 Kubernetes 环境使⽤不同的控制器实现，但有些并不提供默认控制器。

Kubernet 官方提供了很多 Ingress 控制器，具体详见：https://kubernetes.io/zh/docs/concepts/services-networking/ingress/

## 获取所有节点的 IP

```bash
kubectl get nodes -o jsonpath='{.items[*].status.addresses[?(@.type=="ExternalIP")].address}' #
kubectl get nodes -o json # 查看所有节点的信息，json 格式
```

## TLS

```yaml
apiVersion: networking.k8s.io/v1
kind: Ingress
metadata:
  name: basicdata-ingress-tls
spec:
  tls:
  - hosts:
    - basicdata.example.com
    secretName: tls-secret
  rules:
    - host: basicdata.example.com
      http:
        paths:
        - path: /
          backend:
            serviceName: basicdata-nodepart
            servicePost: 8000
```

首先要创建 Secret；第一步就要创建私钥和证书：

```bash
openssl genrsa -out tls.key 2048	# 创建 tls.key 密钥
openssl req -new -x509 -key tls.key -out tls.cert -days 360 -subj /CN=basic.example.com	# 创建 tls.cert 证书
```

创建 Secret

```bash
kubectl create secret tls tls-secret --cert=tls.cert --key=tls.key
```

然后通过 CertificateSigningRequest（CSR） 资源签署证书：

```bash
kubectl certificate approve <name of the CSR>
```

> 请注意，证书签署者组件必须在集群中运⾏，否则创建 CertificateSigningRequest 以及批准或拒绝将不起作⽤。

## 探针（probe）

探针之前有说到存货探针，相当于一个健康检查。除了存活探针之外还有**就绪探针（readiness probe）**，代表 pod 准备就绪了。内部是通过申明就绪探针的检验方式来实现的，在 `spec.containers` 节点下增加就绪探针申明：

```yml
readinessProbe:  # pod 的每个容器都会有一个就绪探针
  exec:
    command:
    - ls
    - /var/ready
```

工作原理就是 pod 会检测所在的服务器中是否有指定的 /var/ready 文件，如果有的话说明就准备就绪了，反之查询 pod 的状态会一直处于 `ready /0` 状态。

就绪探针类型有三种：

- Exec  探针，执行进程的地方。容器的状态进程的退出状态代码确定。
- HTTP GET 探针，向容器发送 HTTP GET 请求，通过响应的 HTTP 状态代码判断容器是否准备好。
- TCP Socket 探针，它打开⼀个TCP 连接到容器的指定端口。如果连接已建⽴，则认为容器已准备就绪。

## 客户端如何连接所有的 Pod

- 通过访问 Kubernetes API 获取 pod 以及它的 IP 地址列表
- 通过解析 DNS 查找发现 pod IP，客户端可以做一个简单的 DNS A 记录查找属于该服务的所有 pod IP。客户端可以使用该信息连接到其中一个、多个或全部

具体措施：这个时候我们可以开启新的 pod，利用 `tutum/dnsutils` 镜像来访问其它的 pod，这样就能进行 dns 解析获取该服务所有的 pod 的 IP 信息。

```bash
kubectl run dnsutils --image=tutum/dnsutils --command -- sleep infinity
```

然后运行 dnsutils 的命令：

```bash
kubectl exec dnsutils nslookup basicdata-headless
```

```
Server:         10.96.0.10
Address:        10.96.0.10#53

Name:   basicdata-headless.default.svc.cluster.local
Address: 10.1.0.178
Name:   basicdata-headless.default.svc.cluster.local
Address: 10.1.0.176
Name:   basicdata-headless.default.svc.cluster.local
Address: 10.1.0.177
Name:   basicdata-headless.default.svc.cluster.local
Address: 10.1.0.183
```

上述查询都是查出该服务中已经准备就绪（就绪探针）的 pod。那么如果要查所有（也包括哪些未就绪的）的 pod 呢？

在服务的 `metadata.annotations` 节点下添加 `service.alpha.kubernetes.io/tolerate-unready-endpoints: true`。注意最新版这个节点已经弃用，改用 `.spec.publishNotReadyAddresses: true` 即可。

## 卷：容器之间共享数据

在一个 pod 上创建三个容器，容器之间共享卷。先创建一个 pod：

```yml
apiVersion: v1
kind: Pod
metadata:
  name: fortune
spec:
  containers:
  - image: marsonshine/fortune
    name: html-generator
    volumeMounts:
    - name: html
      mountPath: /var/htdocs
  - image:  nginx:alpine
    name: web-server
    volumeMounts:
    - name: html
      mountPath: /usr/share/nginx/html
      readOnly: true	# 卷只读
    ports:
    - containerPort: 8080
      protocol: TCP
volumes:  # 一个名为 html 的类型为 emptyDir 的卷，挂载在上面的两个容器中
  - name: html
    emptyDir: {}
```


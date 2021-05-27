# K8S 相关资料

官网社区：https://kubernetes.io/docs/home/

中文社区：http://docs.kubernetes.org.cn

书籍：

1. [Kubernetes 指南](https://kubernetes.feisky.xyz/#ben-shu-ban-ben-geng-xin-ji-lu)（次数更新涉及到的版本为 v1.11 版本，很陈旧了）
2. 《Kubernetes in Action》

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


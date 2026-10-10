# S3 适配器设计

用法与配置见 [README](../README.md)。适配器只依赖 Core 契约及 AWS SDK，避免云 SDK 进入本地／HTTP 存储使用者的依赖图。AWSSDK.S3 4.0.104.2 为 Apache-2.0，声明 Core 范围 `[4.0.102.9, 5.0.0)`，当前实际恢复下界。

## 输入与条件写入

所有流从当前位置进入同一限额包装，不信任 `Length`，每次读取最多请求剩余额度加一。先通过已有 `LocalFileStorage` 保存到本次随机私有目录，再从零位置打开完整可定位文件交给 SDK。这样重试与签名不需要重放调用者的非定位流，也不重复实现本地发布逻辑。

请求明确设置 `IfNoneMatch="*"`、`AutoCloseStream=false`、`AutoResetStreamPosition=false` 和确定内容长度。关闭请求分块编码，保留 SDK 校验和及 SigV4，不改全局 TLS 或校验和开关。412 表示已有当前对象，映射为带原 SDK 错误的 `IOException`；409 等冲突保留 SDK 类型，由宿主协调。

前缀允许 ASCII 字母、数字、点、下划线和连字符组成的规范相对段。其最长 981 字符，加分隔符及最长 42 字符 key 后不超过 1024 字节。key 校验完全复用 `StoredFileKey`；不编码任意用户路径来绕过契约。桶和前缀不是授权判断。

## 所有权与失败

Unix 暂存操作目录使用当前用户权限，Windows 继承宿主私有父目录。SDK 只读取本组件拥有的文件句柄；调用者源流和注入 client 均不关闭。操作成功、失败或取消后清理本次目录，已有原错误时清理失败以 `AggregateException` 保留。Core 暂存复制／发布也遵循同一双错误规则。

GET 返回的包装流拥有整个 `GetObjectResponse`，而非仅暴露正文流。初始化响应后的取消或校验失败会释放响应；释放同时失败时保留两项错误。正常释放响应出现的错误直接传播。调用者不释放下载流会保留 HTTP 资源。

远端成功响应之前断网或取消可能仍已提交对象；本地清理失败也可能发生在已确认 PUT 成功之后。适配器没有可靠的反向删除依据，因此从不自动删除远端对象。调用者持有写入前生成的 key，再协调自己的任务／文件记录。Delete 面向当前对象，版本与删除标记不在本接口生命周期内。

## 缺失与签名 URL

仅对象型 404（`NoSuchKey` 或服务通用未找到）映射为缺失。403、可识别 `NoSuchBucket`、其他错误码传播。HEAD 通用 404 缺少足够错误细节，不能承诺检测全部桶配置问题。元数据需要长度及最后修改时间，输出 UTC 时间；查询不构成写入状态事务。

GET 预签名沿用宿主凭据和 SDK 端点配置，限制 1 秒至 7 天。它不验证对象存在，也不替代授权。SDK 签名方法没有取消令牌，适配器只在调用前后检查取消；凭据有效期仍约束 URL。

## 验证边界

[S3FileStorageTests](../../../test/MS.Microservice.Storage.S3.Tests/S3FileStorageTests.cs) 使用真实 `AmazonS3Client` 与静态假凭据。[localhost 接收器](../../../test/MS.Microservice.Storage.S3.Tests/S3WireServer.cs) 独立重建 SigV4 canonical request／HMAC，校验实际请求签名和条件头，执行仅供测试的对象状态机。

覆盖真实 PUT／GET／HEAD／DELETE、默认 checksum 头、并发条件写、当前位置、非定位短读、空与实际限额、未知成功取消、下载响应释放、403／错误桶、键／前缀及 URL 限时。fake SDK 仅补元数据缺失、Windows readonly 暂存清理及响应释放故障；后者验证取消与清理错误不会互相覆盖。

该接收器不是完整 AWS／MinIO 服务。真实云权限、跨区、版本、加密与兼容端点未配置，不能把本地协议通过称为云集成验证。

依据：[SDK PUT 条件与流属性](https://docs.aws.amazon.com/sdkfornet/v4/apidocs/items/S3/TPutObjectRequest.html)、[单 PUT 上传范围](https://docs.aws.amazon.com/AmazonS3/latest/userguide/upload-objects.html)、[响应所有权](https://docs.aws.amazon.com/sdkfornet/v4/apidocs/items/S3/TGetObjectResponse.html)。

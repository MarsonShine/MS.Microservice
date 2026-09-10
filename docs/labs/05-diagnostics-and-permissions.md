# 实验五：诊断与权限

目标：从一次请求定位到出站、消费和失败，同时验证普通身份不能执行运维操作。
先完成[公共准备](README.md)。

## 串联 Trace

先结束旧宿主，再显式打开控制台遥测：

    ./build/start-local.ps1 -Lab -ConsoleTelemetry

在第二个终端提交带标准 W3C Trace 上下文的请求：

    $traceId = [Guid]::NewGuid().ToString('N')
    $spanId = [Guid]::NewGuid().ToString('N').Substring(0,16)
    $headers['traceparent'] = "00-$traceId-$spanId-01"
    $headers['requestId'] = [Guid]::NewGuid().ToString('N')
    $body = @{ issuer='http://localhost:5210'; subject=[Guid]::NewGuid().ToString(); displayName='Trace example'; roles=@('reader') } | ConvertTo-Json
    $profile = Invoke-RestMethod -Method Post -Uri "$labUrl/lab/messaging/profiles" -Headers $headers -ContentType 'application/json' -Body $body
    Select-String -Path artifacts/local/Lab.stdout.log -Pattern $traceId

结合 Outbox 的 TraceParent 和消息 Id，跟踪 HTTP、messaging.publish、messaging.consume。
正常诊断只需要标识、类型、耗时和结果，不需要密码或消息正文。

默认消息指标包括操作结果、耗时和未完成消息数量。积压默认 30 秒采样；
数据库中断时可能是上次采样值，应结合健康状态和错误日志判断。
Wolverine 使用原生指标，见[观测说明](../../src/MS.Microservice.Observability/README.md)。

## 验证权限

    $readerHeaders = @{ Authorization='Bearer ' + (Get-LabToken $settings -Reader) }
    $denied = Invoke-WebRequest "$labUrl/lab/messaging/failures" -Headers $readerHeaders -SkipHttpErrorCheck
    $denied.StatusCode

应得到 403；匿名请求应得到 401。操作员查询：

    $failures = Invoke-RestMethod "$labUrl/lab/messaging/failures" -Headers $headers
    $failures

LabMessagingOperations 使用明确的 lab/messaging/operations 权限路径，
不依赖 MVC controller/action 路由值。仅给档案增加业务角色不能获取这项权限。

如有已经检查过的失败记录，选择一个原始 failureId 后重放：

    if (@($failures).Count -gt 0) {
        $failureId = [Uri]::EscapeDataString($failures[0].failureId)
        Invoke-WebRequest -Method Post -Uri "$labUrl/lab/messaging/failures/$failureId/replay" -Headers $headers -SkipHttpErrorCheck
    }

202 表示已受理，404 表示找不到，409 表示当前状态或契约不允许重放；
202 不代表消费已经完成。未知版本需要先完成相应代码/数据迁移，不能盲目循环重放。

## 对照正式参考服务

Reference 使用外部 JWT/OIDC 与 profiles.manage / messaging.manage 权限 claim。
它的 readiness 检查业务迁移、消息存储和 Broker：数据库问题返回 503，Broker 中断报告 degraded。
旧 Lab 健康端点主要检查练习数据库，不能把它当作完整的正式 readiness 验收。

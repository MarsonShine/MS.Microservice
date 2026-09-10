# 实验一：请求与分层

目标：沿着 HTTP → Application → Domain → Persistence 跟踪一次档案创建。
先完成[公共准备](README.md)。

## 正常请求

    $state = Invoke-LabExercise $settings -BaseUrl $labUrl
    Wait-ReferenceAudit $state -BaseUrl $labUrl -AuditPath '/lab/messaging/audit'
    $state.Profile

应得到业务档案 Id、版本 1，并在审计查询中看到同一 profileId/profileVersion。
本地档案 Id 不等于登录账号的外部 subject。

阅读以下实现并标注各自职责：

- [HTTP 入口](../../samples/Lab/MS.Microservice.Lab/Hosting/LabMessaging.cs)负责协议和身份上下文。
- [ProfileService](../../samples/Reference/MS.Microservice.Reference.Application/ProfileService.cs)组织工作单元，显式映射集成事件。
- [UserProfile](../../samples/Reference/MS.Microservice.Reference.Domain/UserProfile.cs)维护身份、名称和业务角色规则。
- [业务映射](../../samples/Reference/MS.Microservice.Reference.Persistence/ReferenceDbContext.cs)维护唯一性与版本约束。

## 非法请求

先记录业务表和 Outbox 行数：

    $sql = 'SELECT (SELECT COUNT(*) FROM reference."UserProfiles") AS profiles, (SELECT COUNT(*) FROM messaging."Outbox") AS outbox;'
    Invoke-LocalCompose exec -T postgres psql -U ms_reference -d ms_lab_messages_selfmanaged -c $sql

发送不存在的业务角色：

    $body = @{ issuer='http://localhost:5210'; subject=[Guid]::NewGuid().ToString(); displayName='invalid'; roles=@('unknown-role') } | ConvertTo-Json
    $response = Invoke-WebRequest -Method Post -Uri "$labUrl/lab/messaging/profiles" -Headers $headers -ContentType 'application/json' -Body $body -SkipHttpErrorCheck
    $response.StatusCode

应返回 400。再次查询行数，应与之前一致。
再尝试空名称、空 subject、相同 issuer/subject 重复创建，分别观察验证与冲突结果。

## 思考与检查

业务角色 reader/editor/administrator 与消息运维权限是两件事；
给档案增加业务角色不会提升当前登录身份的权限。

完成时应能解释：为什么领域事件不直接发送到 Broker，
为什么 EnqueueAsync 成功不能当作 Broker 已接收，以及非法请求在哪一层被拒绝。

本地回归入口：

    dotnet test test/MS.Microservice.Reference.Application.Tests -c Release
    dotnet test test/MS.Microservice.Reference.Domain.Tests -c Release

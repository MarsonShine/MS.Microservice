# HTTP 文件上传校验

`MS.Microservice.AspNetCore.Uploads.FileUploadValidator` 校验 `IFormFile` 的声明长度、扩展名、Content-Type 与文件头。它可以直接实例化，也可以由宿主注册到依赖注入容器。

```csharp
services.AddScoped<FileUploadValidator>();

var result = await validator.ValidateAsync(
    file,
    UploadFileKind.Image,
    maximumBytes: 5 * 1024 * 1024,
    cancellationToken);

if (!result.IsValid)
{
    return StatusCode(result.StatusCode, result.Message);
}
```

调用方选择 `Image` 或 `Excel`，并提供正数大小上限。大小恰好等于上限时允许通过；缺少文件或长度为零返回 400，超过上限返回 413，类型或文件头不符返回 415。校验成功返回 200、空消息与规范化为小写的扩展名，供后续保存使用。

| 类型 | 扩展名 | Content-Type |
| --- | --- | --- |
| 图片 | `.jpg`、`.jpeg` | `image/jpeg` |
| 图片 | `.png` | `image/png` |
| 图片 | `.webp` | `image/webp` |
| Excel | `.xls` | `application/vnd.ms-excel` |
| Excel | `.xlsx` | `application/vnd.openxmlformats-officedocument.spreadsheetml.sheet` |

扩展名与 Content-Type 的比较忽略大小写，二者必须匹配同一行。读取文件头支持分段返回和不可定位的流，图片最多读取 12 字节，Excel 最多读取 8 字节。校验器释放自己通过 `OpenReadStream()` 打开的读取流；后续处理应重新打开文件流。读取时的取消或 I/O 异常直接传给调用方，打开的流仍会释放。

文件头校验只用于初步筛选格式，不代表完整图片或工作簿有效。例如 `.xlsx` 检查的是 ZIP 文件头，普通 ZIP 也可能通过；图片只含匹配的签名字节也可能通过。后续组件仍需解码图片或解析工作簿，并处理内容损坏。大小检查使用 `IFormFile.Length`，宿主还应设置请求体限制；自定义 `IFormFile` 实现必须如实提供长度。

请求体限制、认证授权、存储路径、随机文件名、文件保留策略及业务导入规则由宿主或相应组件负责。Lab 的图片和 Excel 上传端点复用此校验器，业务配置继续保留在 Lab。

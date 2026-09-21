# 查看编译器生成的绑定代码

如果还不清楚为什么原调用能转到生成的方法，先读 [配置绑定的编译过程](../../ConfigurationBindingGeneration.md)。下面的命令用于把已经参与编译的生成代码保存到磁盘。

在仓库根目录执行任一组件的常规构建，并输出生成文件到该项目的 `obj` 下：

```powershell
dotnet build src/MS.Microservice.AspNetCore/MS.Microservice.AspNetCore.csproj --no-restore -t:Rebuild -p:EmitCompilerGeneratedFiles=true -p:CompilerGeneratedFilesOutputPath=obj/GeneratedConfiguration
dotnet build src/MS.Microservice.Observability/MS.Microservice.Observability.csproj --no-restore -t:Rebuild -p:EmitCompilerGeneratedFiles=true -p:CompilerGeneratedFilesOutputPath=obj/GeneratedConfiguration
```

在 `obj/GeneratedConfiguration` 中查看 `BindingExtensions.g.cs`：配置键对应具体的属性赋值，不再循环查找 `PropertyInfo`。文件中的拦截位置指回生产调用点；这是判断生成器是否覆盖调用的依据，不能仅凭项目设置为 `true` 就宣称迁移完成。

对照阅读顺序：旧调用文件 → 项目生成器设置 → interceptor → 具体类型的绑定方法。AspNetCore 应覆盖身份选项和字符串数组；Observability 应覆盖遥测选项的即时读取及 OptionsBuilder 绑定。

这些文件是编译产物，不提交到仓库。新旧 API 写法相同，区别是编译器已为这些具体类型生成绑定代码，所以没有另造一个手写 binder 来冒充新的生产实现。

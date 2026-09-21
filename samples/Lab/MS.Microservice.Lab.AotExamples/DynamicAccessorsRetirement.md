# 旧属性访问器为什么退出生产组件

旧 `PropertyAccessors` 为运行时类型扫描属性，再用 IL Emit 或 `Expression.Compile()` 生成 getter、setter、工厂和查询填充委托。它缓存了生成结果，所以不应说每次访问都重新生成；成本是首次发现/编译、常驻缓存及 NativeAOT 无法支持的动态代码路径。表达式版本也会编译，不能充当 AOT 回退。

查询已有 `QueryParameterMap<T>`，Excel 已有 `ExcelModelMap<T>`。二者在声明时就知道成员和构造方法，运行时只需要调用普通委托。旧访问器已没有生产消费者，因此删除入口和策略开关，不再维护两种动态实现作为隐式兜底。

迁移：`PropertyAccessors.Materializer(typeof(Request))(request, destination)` 改为 `QueryStringParameters.Dispatch(request, destination, RequestMap)`；`RequestMap` 逐项声明名称与读取委托。Excel 的 factory、getter、setter 和值转换使用显式列定义，详见 [ExcelModelMappings.md](ExcelModelMappings.md)。

完整旧实现放在 `Legacy/Reflection`，改用学习区命名空间，原访问器/查询策略对照测试也移到学习测试项目。静态对照实现和同输入测试已在 `Static/Query` 与 `Static/Excel` 中；学习类库不打包，生产组件不引用它。旧测试仍验证顺序、空集合、值类型、工厂和 setter，保留用于理解历史机制，不意味着这些实现适合 AOT。

Excel 兼容性修正恢复了旧 Excel 接口，但未恢复 Core 的通用 PropertyAccessors。旧 Excel 的成员发现与编译封装在其内部，只进入 Legacy/All 构建；Aot 构建不包含它。

using System.Collections;
using System.Reflection;
using System.Reflection.Emit;
using MS.Microservice.Core.Net.Http;

namespace MS.Microservice.Lab.AotExamples.Legacy.Reflection;

/// <summary>用 <c>DynamicMethod</c> + <c>ILGenerator</c> 直接发射 MSIL 的编译实现。</summary>
/// <remarks>
/// 与表达树版的差别都来自"能不能拿到编译期的静态类型"：
/// <list type="bullet">
/// <item>表达树只能用 <c>Expression.Convert(..., typeof(object))</c> 装箱；IL 按属性声明类型决定是否 <c>box</c>，
/// 枚举还能直接 <c>call Enum.ToString()</c>，省掉 <c>IFormattable.ToString(null, provider)</c> 的接口分发。</item>
/// <item>零下界一维数组属性在 IL 里可以用 <c>ldlen</c> + 与元素类型匹配的 <c>ldelem</c> 展开，不构造枚举器；
/// 表达树要写等价循环得手工拼 <c>Loop</c>/<c>Break</c>/<c>Label</c>，因此那边退化为 <c>IEnumerable</c> 分发。</item>
/// </list>
/// 代价是可读性与验证成本：栈平衡、局部变量、分支标签都要自己维护，改动前先跑同源测试。
/// </remarks>
public sealed class IlPropertyAccessorStrategy : PropertyAccessorStrategy
{
    private static readonly MethodInfo AppendOne = typeof(QueryStringParameters)
        .GetMethod(nameof(QueryStringParameters.AppendValue), [typeof(string), typeof(object), typeof(ICollection<string>)])!;

    private static readonly MethodInfo AppendMany = typeof(QueryStringParameters)
        .GetMethod(nameof(QueryStringParameters.AppendManyIfNotNull), [typeof(string), typeof(IEnumerable), typeof(ICollection<string>)])!;

    private static readonly MethodInfo Count = typeof(ICollection<string>)
        .GetProperty(nameof(ICollection<string>.Count))!.GetMethod!;

    public override Func<object, ICollection<string>, int> CompilePopulate(Type type)
    {
        var method = new DynamicMethod(
            $"Populate_{type.Name}",
            typeof(int),
            [typeof(object), typeof(ICollection<string>)],
            typeof(PropertyAccessors).Module,
            skipVisibility: false);
        var il = method.GetILGenerator();
        var typed = il.DeclareLocal(type);
        var appended = il.DeclareLocal(typeof(bool));

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, type);
        il.Emit(OpCodes.Stloc, typed);

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!property.CanRead || property.GetMethod?.IsPublic != true) continue;
            if (property.GetIndexParameters().Length != 0) continue;
            EmitProperty(il, typed, appended, property);
        }

        // 返回 destination.Count，调用方据此判断是否写入了任何参数。
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, Count);
        il.Emit(OpCodes.Ret);
        return method.CreateDelegate<Func<object, ICollection<string>, int>>();
    }

    /// <summary>发射"读取一个属性并写入目标集合"的语句块；进入与退出时求值栈深度一致。</summary>
    private static void EmitProperty(ILGenerator il, LocalBuilder typed, LocalBuilder appended, PropertyInfo property)
    {
        var storage = property.PropertyType;

        // SZArray：ldlen + 零起点索引循环；其他数组交给 IEnumerable 处理其维度与下界。
        if (storage.IsSZArray)
        {
            var array = il.DeclareLocal(storage);
            var index = il.DeclareLocal(typeof(int));
            var skip = il.DefineLabel();
            var check = il.DefineLabel();
            var next = il.DefineLabel();

            il.Emit(OpCodes.Ldloc, typed);
            il.Emit(OpCodes.Callvirt, property.GetMethod!);
            il.Emit(OpCodes.Stloc, array);
            il.Emit(OpCodes.Ldloc, array);
            il.Emit(OpCodes.Brfalse, skip);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Stloc, index);
            il.Emit(OpCodes.Br, check);

            il.MarkLabel(next);
            EmitAppendElement(il, property.Name, array, index, appended);
            il.Emit(OpCodes.Ldloc, index);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Stloc, index);

            il.MarkLabel(check);
            il.Emit(OpCodes.Ldloc, index);
            il.Emit(OpCodes.Ldloc, array);
            il.Emit(OpCodes.Ldlen);
            il.Emit(OpCodes.Conv_I4);
            il.Emit(OpCodes.Blt, next);
            il.MarkLabel(skip);
            return;
        }

        // 非字符串的可枚举属性：单次 IEnumerable 分发，null 由被调方法按"无参数"跳过。
        if (IsExpandable(storage))
        {
            il.Emit(OpCodes.Ldstr, property.Name);
            il.Emit(OpCodes.Ldloc, typed);
            il.Emit(OpCodes.Callvirt, property.GetMethod!);
            il.Emit(OpCodes.Castclass, typeof(IEnumerable));
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, AppendMany);
            il.Emit(OpCodes.Stloc, appended);
            return;
        }

        // 标量：值类型先装箱，再交给 AppendValue 走统一格式化规则（含 null 跳过）。
        // 装箱必须发生在判空之前且不能插在对象与判空之间：box 产出的是引用、不会在栈上留下可比较的原值，
        // 所以先落局部变量再决定压入什么，避免"先装箱后补 null 检查"造成的栈失衡。
        // 压栈顺序必须与 AppendValue(string, object, ICollection) 一致：先参数名，再值，最后目标集合。
        il.Emit(OpCodes.Ldstr, property.Name);
        il.Emit(OpCodes.Ldloc, typed);
        il.Emit(OpCodes.Callvirt, property.GetMethod!);
        if (storage.IsValueType)
        {
            var value = il.DeclareLocal(storage);
            var notNull = il.DefineLabel();
            il.Emit(OpCodes.Stloc, value);
            il.Emit(OpCodes.Ldloc, value);
            il.Emit(OpCodes.Box, storage);
            // Nullable<T> 无值时 box 压入 null；该 null 原样传给 AppendValue 即被跳过。
            // 非空值类型的装箱结果永远非 null，分支与标签同址，等于没有分支。
            if (Nullable.GetUnderlyingType(storage) is not null)
            {
                il.Emit(OpCodes.Dup);
                il.Emit(OpCodes.Brtrue, notNull);
            }
            else
            {
                il.Emit(OpCodes.Nop);
            }

            il.MarkLabel(notNull);
        }

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, AppendOne);
        il.Emit(OpCodes.Stloc, appended);
    }

    /// <summary>把数组下标处的元素取出来交给单值追加方法，并把结果并入"是否写入过"。</summary>
    private static void EmitAppendElement(ILGenerator il, string name, LocalBuilder array, LocalBuilder index, LocalBuilder appended)
    {
        il.Emit(OpCodes.Ldloc, appended);
        il.Emit(OpCodes.Ldstr, name);
        il.Emit(OpCodes.Ldloc, array);
        il.Emit(OpCodes.Ldloc, index);
        var elementType = array.LocalType.GetElementType()!;
        if (elementType.IsValueType)
        {
            il.Emit(OpCodes.Ldelem, elementType);
            il.Emit(OpCodes.Box, elementType);
        }
        else
        {
            il.Emit(OpCodes.Ldelem_Ref);
        }
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, AppendOne);
        il.Emit(OpCodes.Or);
        il.Emit(OpCodes.Stloc, appended);
    }

    public override Func<object, object?>? CompileGetter(PropertyInfo property)
    {
        if (property.GetMethod?.IsPublic != true) return null;
        var method = new DynamicMethod(
            $"Get_{property.DeclaringType?.Name}_{property.Name}",
            typeof(object),
            [typeof(object)],
            typeof(PropertyAccessors).Module,
            skipVisibility: false);
        var il = method.GetILGenerator();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, property.DeclaringType!);
        il.Emit(OpCodes.Callvirt, property.GetMethod);
        EmitBoxIfNeeded(il, property.PropertyType);
        il.Emit(OpCodes.Ret);
        return method.CreateDelegate<Func<object, object?>>();
    }

    public override Action<object, object?>? CompileSetter(PropertyInfo property)
    {
        if (!IsWritable(property)) return null;
        var setter = property.SetMethod!;

        var method = new DynamicMethod(
            $"Set_{property.DeclaringType?.Name}_{property.Name}",
            typeof(void),
            [typeof(object), typeof(object)],
            typeof(PropertyAccessors).Module,
            skipVisibility: false);
        var il = method.GetILGenerator();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, property.DeclaringType!);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Unbox_Any, property.PropertyType);
        il.Emit(OpCodes.Callvirt, setter);
        il.Emit(OpCodes.Ret);
        return method.CreateDelegate<Action<object, object?>>();
    }

    public override Func<object>? CompileFactory(Type type)
    {
        var constructor = type.GetConstructor(Type.EmptyTypes);
        if (constructor is null) return null;

        var method = new DynamicMethod(
            $"New_{type.Name}",
            type,
            Type.EmptyTypes,
            typeof(PropertyAccessors).Module,
            skipVisibility: false);
        var il = method.GetILGenerator();

        il.Emit(OpCodes.Newobj, constructor);
        il.Emit(OpCodes.Ret);
        return method.CreateDelegate<Func<object>>();
    }

    private static void EmitBoxIfNeeded(ILGenerator il, Type type)
    {
        if (type.IsValueType) il.Emit(OpCodes.Box, type);
    }
}

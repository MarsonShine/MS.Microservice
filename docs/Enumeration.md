# 枚举的替代方案 —— 枚举类

在 ALT.NET 消息版上有一个问题是问我值对象是否应该跨服务边界。当然，沟通的时候绕了好几圈，最终回到了这个问题，“你如何处理横跨服务边界的枚举”，我还是忽视了这个问题，但是已经在我的模型中用不同的方法来表示枚举。现在，枚举在多数情况都是好的，但是在其他地方就不是了。在我的领域模型中当枚举出问题的时候，我喜欢使用其他方式，我直接选了另一个方案。

举个例子，我这里有几个模型：

```c#
public class Employee
{
	public EmployeeType Type { get; set; }
}

public enum EmployeeType
{
	Manager,
	Servant,
	AssistanToTheRegionalManager
}
```

这个模型的问题是它意图创建很多像下面的 switch 片段：

```c#
public void ProcessBonus(Employee employee)
{
	switch (employee.Type)
	{
		case EmployeeType.Manager:
            employee.Bonus = 1000m;
            break;
        case EmployeeType.Servant:
            employee.Bonus = 0.01m;
            break;
        case EmployeeType.AssistantToTheRegionalManager:
            employee.Bonus = 1.0m;
            break;
        default:
            throw new ArgumentOutOfRangeException();
	}
}
```

像这样的枚举有下面一些问题：

- 其枚举相关的行为散落在应用程序各个地方
- 新的枚举值出现是要到处修改
- 枚举不符合开放关闭原则（OCP）

添加一个新的枚举值是非常痛苦的，特别是存在 switch 语句，要回过头修改。在上面的例子，我想用默认的行为来进行防御性编码，但是新的枚举值将会抛出一个错误。

随着枚举行为散落四周，我们将永远不会把它回到源类型，因为枚举类不能有任何行为（或是状态）。

这个时候我喜欢用枚举类来代替枚举。

## 创建枚举类

我用枚举类来移除那些枚举，我首先将枚举超类型使用在默认架构上：

```c#
abstract public class Enumeration : IComparable
{
    private readonly int _value;
    private readonly string _name;

    protected Enumeration() { }

    protected Enumeration(int value, string name)
    {
        _value = value;
        _name = name;
    }

    public int Value => _value;

    public string Name => _name;

    public override string ToString() => Name;

    public static IEnumerable<T> GetAll<T>() where T : Enumeration, new()
    {
        var type = typeof(T);
        var fields = type.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
        foreach (var info in fields)
        {
            var instance = new T();

            if (info.GetValue(instance) is T locatedValue)
            {
                yield return locatedValue;
            }
        }
    }

    public override bool Equals(object obj)
    {
        if (!(obj is Enumeration otherValue))
            return false;

        var typeMatches = GetType().Equals(obj.GetType());
        var valueMatches = _value.Equals(otherValue.Value);

        return typeMatches && valueMatches;
    }

    public override int GetHashCode()
    {
        return _value.GetHashCode();
    }

    public static int AbsoluteDifference(Enumeration firstValue, Enumeration secondValue)
    {
        var absoluteDifference = Math.Abs(firstValue.Value - secondValue.Value);
        return absoluteDifference;
    }

    public static T FromValue<T>(int value) where T : Enumeration, new()
    {
        var matchingItem = Parse<T, int>(value, "value", item => item.Value == value);
        return matchingItem;
    }

    private static T Parse<T, K>(K value, string name, Func<T, bool> predicate) where T : Enumeration, new()
    {
        var matchingItem = GetAll<T>().FirstOrDefault(predicate);

        if(matchingItem == null)
        {
            var message = string.Format("'{0}' is not a valid {1} in {2}", value, name, typeof(T));
            throw new ApplicationException(message);
        }

        return matchingItem;
    }

    public int CompareTo(object obj) => Value.CompareTo(((Enumeration)obj).Value);
}
```

它是一个大类，但是它给我们一些好的开箱即用的功能，如比较相等操作。下一步，我要创建包含不同枚举值的子类

```c#
public class EmployeeType : Enumeration
{
}
```

我还能分开 employee 类，比如 Manager 和 Servant，以及我能开放一个只读静态字段来表示 employee 类型：

```c#
public class EmployeeType : Enumeration
{
    public static readonly EmployeeType Manager
    = new EmployeeType(0, "Manager");
    public static readonly EmployeeType Servant
        = new EmployeeType(1, "Servant");
    public static readonly EmployeeType AssistantToTheRegionalManager
        = new EmployeeType(2, "Assistant to the Regional Manager");
    private readonly int _value;
    private readonly string _name;

    private EmployeeType() { }
    private EmployeeType(int value, string name) : base(value, name) { }
}
```

注意，我还能得到很好的带有空格的显示。在过去，在我要显示它的时候，我总是不得不做一些额外的事情在名字中加入空格。当要分配 Employee 类型的时候，它无论使用还是外观都与之前没有区别：

```c#
dwightScherute.Type = EmployeeType.AssistantToTheRegionalManager;
```

至此，我有了真正的类，它有行为就像值对象一样，这个行为是带有目标性的。例如，我能附加 "BonusSize" 属性：

```c#
public void ProcessBonus(Employee employee)
{
	employee.Bonus = employee.Type.BonusSize;
}
```

对的，这是个非常愚蠢的例子，但是它说明了大部分问题，并不是全部，之前枚举的 switch 语句表达式都远离了。行为可以下推到枚举类中，每个特定的枚举类型提供特定的行为。

这个模式深圳可以更进一步，我有一个独立的 EmployeeType 超类。永远不会向外面暴露任何东西：

```c#
public abstract class SuperEmployeeType : Enumeration
{
    public static readonly SuperEmployeeType Manager
    = new ManagerType();

    protected SuperEmployeeType() { }
    protected SuperEmployeeType(int value, string name) : base(value, name) { }

    public abstract decimal BonusSize { get; }


    private class ManagerType : SuperEmployeeType
    {
        public ManagerType() : base(0, "Manager")
        {
        }

        public override decimal BonusSize => 1000m;
    }
}
```

所有的枚举类的变量都能被下推，并不只是 Enumeration 类，每个子类也是如此。BonusSize 现在变成了单独的 EmployeeType 的具体实现细节了。

Enumerations 能在一些场景里工作的很好，我能把一些可变性和知识放置模型中。如果存在一些理由我需要去检查特定的枚举类的值，那么这个选择很适合我。这个模式不应该替代所有枚举类，但是确实在一些场景是很好的选择。

eshopContainer 实例项目中对枚举类也进行了实现：

https://github.com/dotnet/eShopOnContainers/blob/master/src/Services/Ordering/Ordering.Domain/SeedWork/Enumeration.cs

翻译自：https://lostechies.com/jimmybogard/2008/08/12/enumeration-classes/

延伸阅读资料：

https://ardalis.com/enum-alternatives-in-c#disqus_thread

https://codeblog.jonskeet.uk/2014/10/23/violating-the-smart-enum-pattern-in-c/
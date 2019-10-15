# 值对象

在编程的时候，我经常发现将事务表示复合物是很有用的。一个 2D 坐标有一个 x 和 y 值。钱由数字和货币构成。日期范围由开始和结束时间组成，它们自己都是由年，月，日复合而成的。

当我这样做的时候，遇到了一个问题，这两个复合物是否相同。如果我有两个对象，他们都表示笛卡尔坐标值 (2,3)，把它们看作相等时很有必要的。由于它们的属性，在这个例子就是 x 和 y 坐标值这两个对象时相等的，那么我们就把它称为是值对象。

在我编程的时候，除非我很小心，我也许不会在我的程序中得到它们的行为。

我想用 Javascript 代码来表示我想说的。

```javascript
const p1 = {x: 2, y: 3};
const p2 = {x: 2, y: 3};
assert(p1 !== p2); 	// 这不是我想要的
```

我很难过看到这个测试是通过的。这是因为在 Javascript 比较两个对象相等是通过它们的引用来比较的，忽视了它们包含的值。

在很多场景中使用引用而不是值是有意义的。如果我正在等待和操作一组销售订单，加载每个订单到单独的空间，它会很有意义。如果我想要看 Alice 最新的订单是否在下一次发送，我可以传递这个 Alice 的订单的内存引用，或标识，并且看到这个引用是否在订单这次发送的订单列表中。对于这样的测试，我不用担心。更简单的，我会只是靠订单的唯一编号去测试 Alice 的订单是否在这次递送的订单列表中。

因此我我认为这两类对象是很有用的：值对象和引用对象，这取决于我如何区分它们。我需要确保我知道这每个对象要处理相等性以及让它们的行为按照自己的预期来编码它们。如何做到这点，取决于依赖编程的语言。

一些语言把这些复合对象当作值一样对待。如果我有一个简单的复合 Clojure 对象，它看起来是这样的。

```clojure
> (= {:x 2, :y 3} {:x 2, :y 3})
true
```

这是函数式风格 - 对待每个值都是不变的。

但是如果我用的不是函数式编程语言，来新建一个值对象。以 Java 为例，我期望的这类默认行为是这样。

```java
assertEquals(new Point(2, 3), new Point(2, 3))	// Java
```

这种工作的方式 point 类用值复写了默认的 `equals` 方法。

用 Javascript 我可以这样做来达到相同的效果。

```javascript
class Point {
  constructor(x, y) {
    this.x = x;
    this.y = y;
  }
  equals(other) {
    return this.x === other.x &&
      this.y === other.y;
  }
}
const p1 = new Point(2, 3);
const p2 = new Point(2, 3);
assert(p1.equals(p2));
```

Javascript 这里的问题是那个我定义 `equals` 方法，它对于其它的 Javascript 库来说是神秘的。

```javascript
const somePoints = [new Point(2, 3)];
const p = new Point(2, 3);
assert.isFalse(somePoints.includes(p));	// 这不是预期想要的

// 这个才是我想要的
assert(somePoints.some(i => i.equals(p)));
```

这对于 Java 来说不是问题，因为 `Object.equals` 是定义在核心库里，所有的其它库都用它来做比较（== 操作符就是用的这个方法）。

值对象有一个很重要的好处就是我无需担心在内存中我引用的对象是否相同，或者不同的引用有相等的值。但是，如果我不关注那些不知情就会导致一些问题，这里我会用点 Java 代码来说明。

```java
Date retirementDate = new Date(Date.parse("Tue 1 Nov 2016"));

// 这意味着我们需要一个退休 party
Date partyDate = retirementDate;

// 但是这一天是周二，让我们把 party 放在周末
partyDate.setDate(5);

assertEquals(new Date(Date.parse("Sat 5 Nov 2016")), retirementDate);
// 等等，现在我必须得多工作三天 :-(
```

这个例子是一个 [Aliasing Bug](https://martinfowler.com/bliki/AliasingBug.html)，我在一个地方改变了日期并且后果超出了我的预期。为了避免这种问题，我按照下面简单但是非常重要的规则：**值对象必须是不变的**。如果我想改变我的 party 时间，我就要创建新的对象来代替。

```java
Date retirementDate = new Date(Date.parse("Tue 1 Nov 2016"));
Date partyDate = retirementDate;
// 日期是不变的
partyDate = new Date(Date.parse("Sat 5 Nov 2016"));
// 我还能在周二退休
assertEquals(new Date(Date.parse("Tue 1 Nov 2016")), retirementDate);
```

当然，它犯下错很容易修复，把值当作不可变的即可，如果它们真的是不变的。这些对象我经常这么做，不提供任何设置方法。所以我的 JavaScript 类就像这样：

```javascript
class Point {
	constructor(x, y) {
		this._data = {x: x, y: y};
	}
	get x() {return this._data.x;}
	get y() {return this._data.y;}
	equals(other) {
		return this.x === other.x && this.y === other.y;
	}
}
```

通过我喜欢的记录来达到不变体，来避免 aliasing bug，也可以确保总是分配拷贝的副本来避免它们。一些语言提供这种能力如 C# 中的结构体。

无论是引用对象还是值对象的概念，都依赖你自己的上下文。在很多场景下，把邮件地址当成一个简单的结构问题来表示值相等是不好的。但是在越来越复杂的映射系统可能会链接邮件地址到更复杂深层次的模型，这些模型是引用对象会更加有意义。在大多数模型问题里，不同上下文会有不同的解决方案。

总是好的注意，替换原始通用的地方，比如字符串，会有合适的值对象。当我能用一串数字字符串表示电话的时候，就会转成电话对象能够使变量和参数更加明确（通过类型检查当语言支持的时），一个寻常的聚焦验证，以及避免不想干的行为（如在整数 id 数字上做算术）。

小对象，如 坐标，钱，或范围，都是值对象的好例子。但是大结果也能经常作为值对象编程，如果它们任何概念标识或者不需要共享问题附近的引用。这是很自然地满足函数式语言的，因为默认就是不变的。

我发现值对象一般都是特指小的，经常被忽视的 - 看起来微不足道，不考虑会变坏。但是一旦发现一组好的值对象，我就会发现我能在它们上面创建丰富的行为。为了尝它们，我尝试使用了 [Range]([Range class](https://martinfowler.com/eaaDev/Range.html)) 类以及通过丰富的行为查看它预防开始和结束属性的各种重复操作。我经常在代码中运行，像这样基于领域值对象，可以作为重构的焦点，从而导致系统简单化。

最后给出 eshopContainer 中的值对象的代码设计思路：

```c#
public abstract class ValueObject
{
  	protected static bool EqualOperator(ValueObject left, ValueObject right) 
    {
    		if(ReferenceEquals(left, null) ^ ReferenceEquals(null, right))
        {
          	return false;
        }
      	return ReferenceEquals(left, null) || left.Equals(right);
    }
  
    protected static bool NotEqualOperator(ValueObject left, ValueObject right)
    {
        return !(EqualOperator(left, right));
    }
  
    protected abstract IEnumerable<object> GetAtomicValues();
  	
  	public override bool Equals(object obj)
    {
      	if(obj == null || obj.GetType() != GetType())
        {
          	return false;
        }
      	ValueObject other = (ValueObject)obj;
        IEnumerator<object> thisValues = GetAtomicValues().GetEnumerator();
        IEnumerator<object> otherValues = other.GetAtomicValues().GetEnumerator();
        while (thisValues.MoveNext() && otherValues.MoveNext())
        {
            if (ReferenceEquals(thisValues.Current, null) ^ ReferenceEquals(otherValues.Current, null))
            {
                return false;
            }
            if (thisValues.Current != null && !thisValues.Current.Equals(otherValues.Current))
            {
                return false;
            }
        }
        return !thisValues.MoveNext() && !otherValues.MoveNext();
    }
}
```



翻译自：https://martinfowler.com/bliki/ValueObject.html
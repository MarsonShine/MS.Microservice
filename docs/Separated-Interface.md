# 接口分离原则

在开发一个系统的时候，通过设计减少系统里功能之间的耦合来提高质量。 一个好的方式就是把类分组到包中，并控制好他们之间的依赖。你可以根据下面的规则告诉类怎么在一个包中调用其他的类 — — 例如，在领域层的类可能不会调用在显示层包中的类。

然而，你也许需要调用与一般结构相冲突的方法。如果是这样，使用接口隔离在包中定义一个接口，但是在另一个包实现它。在客户端使用这种方式就需要依赖这个接口，并且它完全不知道它的实现。对于网管接口隔离原则提供了一个好的组件。

## 案例

一般情况下，我们在设计类与类之间的关系会像下面这样：

```c#
public class Ops {
	public void Op1() {
	}
	
	public void Op2() {
	}
	
	public void Op3() {
	}
}

public class User1 {
	Ops op1;
	void Do() {
		op1.Op1();
	}
}
public class User2 {
	Ops op2;
	void Do() {
		op2.Op2();
	}
}
public class User3 {
	Ops op3;
	void Do() {
		op3.Op3();
	}
}
```

这段代码设计有什么问题呢？

首先 User 三个类都直接依赖类 Ops，虽然这三个类各自用 Ops 不同的方法，但是由于都共同依赖一个实现类 Ops，当你只修改 `Ops.Op2()` 方法时，虽然其他两个不受影响，但是也同样由于 Op2 代码更改要重新编译发布。更严重会面临代码意料之外的更改的风险，特别是在 Ops 类内部细节可能存在共享变量的情况。

如果我们采用接口分离原则来重新设计上面那段代码 — — 把类 Ops 中的三个方法分别抽象成三个接口，然后用一个类来实现这个三个接口，那么我们的实体 User 只需要分别依赖继承对应的接口即可，不需要关注其具体实现：

```c#
interface IUser1Ops {
    void Ops1();
}
interface IUser2Op2 {
    void Ops2();
}
interface IUser3Op3 {
    void Ops3();
}

class User1 {
    User1(IUser1Ops ops)
    void Do() {
        ops.Ops1();
    }
}
class User2 {
    ...
    void Do() {
        ops.Ops2();
    }
}
class User3 {
    ...
    void Do() {
        ops.Ops3();
    }
}

public Ops : IUser1Ops,IUser2Ops,IUser3Ops {
    void Ops1(){}
    void Ops2(){}
    void Ops3(){}
}
```

我们可以看到，User 不直接依赖于具体实现类，而是更上一层的抽象类。这样一来，当更改具体实现类是，我们修改 Ops 具体的业务逻辑代码，根本不需要重新编译和部署 User 部分的代码。

https://martinfowler.com/eaaCatalog/separatedInterface.html
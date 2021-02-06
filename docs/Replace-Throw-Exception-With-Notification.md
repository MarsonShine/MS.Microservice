# 验证——通知代替抛错

先列出下面常用的验证代码

```java
public void check() {
   if (date == null) throw new IllegalArgumentException("date is missing");
   LocalDate parsedDate;
   try {
     parsedDate = LocalDate.parse(date);
   }
   catch (DateTimeParseException e) {
     throw new IllegalArgumentException("Invalid format for date", e);
   }
   if (parsedDate.isBefore(LocalDate.now())) throw new IllegalArgumentException("date cannot be before today");
   if (numberOfSeats == null) throw new IllegalArgumentException("number of seats cannot be null");
   if (numberOfSeats < 1) throw new IllegalArgumentException("number of seats must be positive");
 }
```

对一些数据运行一系列检查(这里只是涉及的类中的一些字段)。如果其中任何一项检查失败，就抛出一个异常，并返回错误消息。

对于这种验证我是不太喜欢用这种方式的。异常（Exception）表明代码行为超出了预期范围。但是像这种你在检查一些输入参数，这些错误都是你意料知道的会导致程序失败——如果一个失败行为是预期的，那么你就不应该使用异常。

第二个问题是，像这种一旦触发了一个异常，只是返回一次错误信息而不是所有的错误信息。也就是说这种最好的做法就是用一次输入就能报告所有错误信息。

我更倾向于选择用[通知模式](https://github.com/MarsonShine/Books/tree/master/DesignPattern/DesignPatternCore/Observer)来导出所有验证错误信息。这个通知对象包含了所有的错误信息，你可以根据这个对象信息来获取这些验证信息，如下面代码：

```java
private void validateNumberOfSeats(Notification note) {
  if (numberOfSeats < 1) note.addError("number of seats must be positive");
  // more checks like this
}
```

我们就可以像调用 `Nofication.hasErrors()` 就能获取其中的错误信息，如果验证不通过的话。这样我们就可以把那些详细的错误信息添加至这个对象中。

## 何时使用通知模式

我需要指出的是，我并不是提倡在代码中消除所有错误信息。异常（Exceptions）是一个非常有用的技术，他可以远离从主逻辑流程来处理异常行为。只有当异常发出的结果不是真正异常时，才可以使用这种重构，因此应该通过程序的主程序逻辑来处理。比如现在说的这个例子就是如此。

在考虑异常时，那些有经验的程序员给出了一条有用的经验法则：

> 我们认为异常不应该作为程序正常流程的一部分使用：异常应该为意外事件所用。假设一个未捕获的异常将终止你的程序，你要扪心自问：”如果我删除所有异常处理程序，这段代码还会运行吗?“，如果答案是，”不会“，那么异常（Exceptions）可能是在非异常情况下使用的。—— [Dave Thomas and Andy Hunt](https://www.amazon.com/gp/product/020161622X?ie=UTF8&tag=martinfowlerc-20&linkCode=as2&camp=1789&creative=9325&creativeASIN=020161622X)

这样做的一个重要后果是，是否对特定任务使用异常取决于上下文。正如从不存在的文件中读取可能是异常，也可能不是异常，这取决于环境。又如，如果您试图读取一个众所周知的文件位置，例如 unix 系统上的 /etc/hosts，那么您可能会认为该文件应该在那里，因此找不到抛出异常是合理的。另一方面，如果您试图从用户在命令行中输入的路径读取文件，那么您就应该预料到该文件可能不存在，并应该使用另一种机制——一种传达错误的正常性质的机制（比如通知模式）。

## 例子

场景：接收前段传参来预定电影院座位，请求体有两个字段，我们就来验证这两个字段，演出日期字段和座位数字段

```java
// class BookingRequest
private Integer numberOfSeats; 
private String date;
```

下面是我的一个验证

```java
// class BookingRequest
public void check() {
 if (date == null) throw new IllegalArgumentException("date is missing");
 LocalDate parsedDate;
 try {
   parsedDate = LocalDate.parse(date);
 }
 catch (DateTimeParseException e) {
   throw new IllegalArgumentException("Invalid format for date", e);
 }
 if (parsedDate.isBefore(LocalDate.now())) throw new IllegalArgumentException("date cannot be before today");
 if (numberOfSeats == null) throw new IllegalArgumentException("number of seats cannot be null");
 if (numberOfSeats < 1) throw new IllegalArgumentException("number of seats must be positive");
}
```

### 构建通知

因为通知对象要存储错误信息，所以首先要创建一个通知对象来容纳错误信息，通知对象一定要尽量简单

```java
public class Notification {
  private List<String> errors = new ArrayList<>();

  public void addError(String message) { errors.add(message); }
  public boolean hasErrors() {
    return ! errors.isEmpty();
}
```

### 拆分验证方法

一般是要分两个部分，一部分是内部验证，处理通知以及阻止抛出任何异常，另一部分是外部验证，阻止当前的验证方法抛出可能存在的任何检查（check）失败

```java
public void check() {
    validation();
}

public void validation() {
    if (date == null) throw new IllegalArgumentException("date is missing");
    LocalDate parsedDate;
    try {
      parsedDate = LocalDate.parse(date);
    }
    catch (DateTimeParseException e) {
      throw new IllegalArgumentException("Invalid format for date", e);
    }
    if (parsedDate.isBefore(LocalDate.now())) throw new IllegalArgumentException("date cannot be before today");
    if (numberOfSeats == null) throw new IllegalArgumentException("number of seats cannot be null");
    if (numberOfSeats < 1) throw new IllegalArgumentException("number of seats must be positive");
}
```

然后我只需要调整 validation 方法返回 Notification 对象即可。

```java
// class BookingRequest
public Notification validation() {
    Notification note = new Notification();
    if (date == null) throw new IllegalArgumentException("date is missing");
    LocalDate parsedDate;
    try {
      parsedDate = LocalDate.parse(date);
    }
    catch (DateTimeParseException e) {
      throw new IllegalArgumentException("Invalid format for date", e);
    }
    if (parsedDate.isBefore(LocalDate.now())) throw new IllegalArgumentException("date cannot be before today");
    if (numberOfSeats == null) throw new IllegalArgumentException("number of seats cannot be null");
    if (numberOfSeats < 1) throw new IllegalArgumentException("number of seats must be positive");
    return note;
  }
```

测试代码

```java
// class BookingRequest
public void check() {
	if (validation().hasErrors()) 
  		throw new IllegalArgumentException(validation().errorMessage());
}
```

### 优化，提取错误类

很多时候我们返回错误并不只是一个字符串的信息，还有其它更详尽的错误附加信息等。所以我们可以将错误信息独立成一个错误类

```java
// class Notification
private static class Error {
    String message;
    Exception cause;

    private Error(String message, Exception cause) {
      this.message = message;
      this.cause = cause;
    }
}
```

现在就变为了

```java
// class Notification…

  private List<Error> errors = new ArrayList<>();

  public void addError(String message, Exception e) {
    errors.add(new Error(message, e));
  }
  public String errorMessage() {
    return errors.stream()
            .map(e -> e.message)
            .collect(Collectors.joining(", "));
  }

```


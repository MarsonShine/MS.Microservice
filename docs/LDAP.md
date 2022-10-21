# LDAP

**LDAP（Lightweight Directory Access Protocol）**是一个互联网访问和维护分布式目录信息服务，它是开放、中立基于TCP/IP协议的行业标准协议。它提供了搜索、修改、添加、比较和删除基于用户选择模式的条目能力。LDAP目录是一个目录条目数，每个条目都包含一组属性（attribute）。

## 协议概述

客户端通过连接到LDAP服务器来启动LDAP会话，这称为目录系统代理（Directory System Agent），默认是在TCP和UDP的389端口，或者是在LDAPS（LDAP over TLS/SSL）的636端口。客户端向服务器发送操作请求，服务器发送响应作为返回。但有一些例外情况是客户端在发送下一个请求之前不需要等待响应，服务器可以以任何顺序发送响应。所有信息都使用基本编码规则(BER)进行传输。

客户端可以请求以下操作:

- StartTLS：使用LDAPv3传输层安全(TLS)扩展进行安全连接
- Bind：验证并指定版本的LDAP协议
- 查询（Search）：搜索/检索目录条目
- 比较（Compare）：比较指定条目的名称是目标属性的值
- 新增条目
- 删除条目
- 修改条目
- 修改唯一识别名（DN）—— 移动或重命名条目
- 丢弃——丢弃上一个请求
- 拓展操作——在使用通用操作去定义其它操作
- 解绑：关闭连接（注意，不是Bind操作的逆操作）

## 目录结构

该协议提供了一个接口，其目录遵循X.500模型的1993版:

- 一个条目由一组属性构成
- 每个属性都有名称（属性类型或属性描述）以及一个或多个值。这些属性被定义为schema
- 每个条目都有唯一标识，被称为Distinguished Name（DN）。它由相对专有名称（Relative Distinguished Name）构成，RDN由条目中的某些属性构造而成，后面跟着的是父条目的DN（下面的例子会做展示）。将DN视为完整的文件路径，RDN作为其父文件夹中的相对文件名（例如如果路径是`/foo/bar/myfile.txt`是DN，那么`myfile.txt`就是RDN）

在条目的生命周期中，DN可能会发生变化，例如，当条目在树中移动时。为了可靠和明确地标识条目，可以在条目的操作属性集中提供唯一值，如UUID。

当条目以LDAP数据交换格式(LDAP Data Interchange Format，LDIF)表示时(LDAP本身是一种二进制协议):

```
 dn: cn=John Doe,dc=example,dc=com
 cn: John Doe
 givenName: John
 sn: Doe
 telephoneNumber: +1 888 555 6789
 telephoneNumber: +1 888 555 1232
 mail: john@example.com
 manager: cn=Barbara Doe,dc=example,dc=com
 objectClass: inetOrgPerson
 objectClass: organizationalPerson
 objectClass: person
 objectClass: top
```

`dn`是条目的专有名称;它既不是条目的属性，也不是条目的一部分。`cn=John Doe`是条目的RDN，`dc=example,dc=com`是父条目的DN，其中`dc`表示域组件（Domain Component）。

其他行显示条目中的属性。属性名称通常是有助于记忆的字符串，比如`cn`表示通用名称，`dc`表示域组件，`mail`表示电子邮件地址，`sn`表示姓氏(surname)。

服务器持有从特定条目开始的子树，例如。`dc=example,dc=com`及其子对象。服务器还可能保存对其他服务器的引用，因此尝试访问`ou=department,dc=example,dc=com`可能会返回对持有该目录树部分的服务器的引用或延续引用。然后客户端可以连接另一个服务器。一些服务器还支持链（chaining），这意味着服务器连接另一个服务器并将结果返回给客户端。

LDAP很少定义任何顺序：服务器可以以任何顺序返回属性的值、条目中的属性和搜索操作找到的条目。这源于正式的定义——条目被定义为一组属性，而属性是一组值，集合不需要排序。

## 操作

### 新增

ADD操作将一个新条目插入到目录服务器数据库。如果添加请求中的专有名称已经存在于目录中，那么服务器将不会添加重复条目，而是将添加结果中的结果代码设置为十进制68，表示"entryAlreadyExists"（具体详见[LDAP错误码](http://tools.ietf.org/html/rfc4511#appendix-A)）

- 兼容LDAP的服务器在试图定位条目时永远不会解除对add请求中传输的DN的引用，也就是说，DN永远不会解除别名（distinguished names are never de-aliased）。
- 兼容LDAP的服务器将确保DN和所有属性符合命名标准。
- 要添加的条目必须不存在，并且直属上级必须存在。

```
dn: uid=user,ou=people,dc=example,dc=com
changetype: add
objectClass:top
objectClass:person
uid: user
sn: last-name
cn: common-name
userPassword: password
```

上面的例子，`uid=user,ou=people,dc=example,dc=com`必须不存在，而`ou=people,dc=example,dc=com`必须存在

### 绑定（验证）

创建LDAP会话时，即LDAP客户端连接服务器时，会话的认证状态设置为匿名。BIND操作为这个会话建立身份验证状态。

Simple BIND和SASL PLAIN可以以明文的方式发送用户DN和密码，因此，使用Simple或SASL PLAIN的连接应该使用TLS进行加密。服务器通常根据已命名条目中的`userPassword`属性检查密码。匿名BIND(DN、密码为空)将连接重置为匿名状态。

BIND通过各种各样的机制提供身份验证服务，例如Kerberos或使用TLS发送的客户端证书。

BIND还通过发送整数形式的版本号来设置LDAP协议版本。如果是客户端请求服务器不支持的版本号，服务器必须在BIND响应中为协议错误的代码设置结果代码。通常客户端应该使用LDAPv3，这是协议中的默认值，但在LDAP库中并不总是这样。

在LDAPv2中，BIND必须是会话中的第一个操作，但在LDAPv3中则不需要。在LDAPv3中，每个成功的BIND请求都会更改会话的身份验证状态，而每个不成功的BIND请求都会重置会话的身份验证状态。

### 删除

要删除条目，LDAP客户端会向服务器发送一个格式正确的删除请求。

- 删除请求必须包含要删除条目的DN
- 请求控制也可以附加到删除请求
- 服务器在处理删除请求时不会解除对别名的引用
- 只有叶子条目(没有下属的条目)可以被删除请求删除。一些服务器支持一个操作属性`hasSubordinates`，它的值指示一个条目是否有任何下级条目，一些服务器还支持`numSubordinates`操作属性，它表示从包含`numSubordinates`属性条目的下级条目的数量。
- 有些服务器支持子树删除请求控制，允许删除DN及以下的所有对象，并受到访问控制。删除请求受制于访问控制，也就是说，是否允许具有给定身份验证状态的连接删除给定条目是由特定于服务器的访问控制机制控制的（即再删除之前需验证删除操作请求的身份信息）。

### 比较and查询

Search操作用于搜索和读取条目。它的参数是:

**baseObject**

​	相对于要执行搜索的基本对象条目(或者可能是根)的名称

**scope**

​	baseObject下面要搜索的元素。它可以是`BaseObject`(只搜索已命名的条目，通常用于读取一个条目)、`singleLevel`(基DN下方的条目)或`wholeSubtree`(从基DN开始的整个子树)。

**filter**

​	在范围内选择元素时使用的标准(criteria)。例如过滤器`(&(objectClass=person)(|(givenName=John)(mail=John *))`将选择`persons`(objectClass 为person的元素)，其中`givenName`和`mail`的匹配规则将决定这些属性的值是否匹配过滤器断言。注意，一个常见的误解是LDAP数据是区分大小写的，而实际上匹配规则和排序规则决定匹配、比较和相对值关系。如果要求示例筛选器匹配属性值的大小写，则必须使用可扩展的匹配筛选器，例如`((&(objectClass=person)(|(givenName:caseExactMatch:=John)(mail:caseExactSubstringsMatch:=john*)))`

**derefAliases**

​	是否以及如何跟随别名条目(引用其他条目的条目)

**attributes**

​	在结果条目中返回哪些属性

**sizeLimit, timeLimit**

​	返回的最大条目数，以及允许搜索运行的最大时间。但是，这些值不能覆盖服务器设置的大小限制和时间限制。

**typesOnly**

​	只返回属性类型，而不返回属性值

服务器返回匹配的条目和可能的延续引用。这些可以以任何顺序返回。最终结果将包括结果代码。

Compare操作接受一个DN、一个属性名称和一个属性值，并检查命名条目是否包含该属性的值。

### 修改

LDAP客户端使用`MODIFY`操作请求LDAP服务器更改现有条目。试图修改不存在的条目将会失败。修改请求受制于服务器实现的访问控制。

MODIFY操作要求指定条目的DN和更改的序列。序列中的每一个更改必须是：

- add（新增一个新值，该值必须在属性中不存在）
- delete（删除一个已存在的值）
- replace（用新值替换一个已存在的值）

一个向属性添加值的[LDIF](https://en.wikipedia.org/wiki/LDAP_Data_Interchange_Format)示例:

```
dn: dc=example,dc=com
changetype: modify
add: cn
cn: the-new-cn-value-to-be-added
-
```

要替换已有属性的值，可以使用`replace`关键字。如果属性是多个值的，客户端必须指定要更新的属性的值。

要从条目中删除属性，请使用关键字`delete`和`changetype`赋值为`modify`。如果属性是多值的，客户端必须指定要删除的属性的值。

还有一个修改增量扩展（Modify-Increment extension），它允许一个可增长的属性值，它按指定的数量增加。下面这个例子使用LDIF increment，将`employeeNumber`增加5:

```
dn: uid=user.0,ou=people,dc=example,dc=com
changetype: modify
increment: employeeNumber
employeeNumber: 5
-
```

当LDAP服务器处于复制拓扑中时，LDAP客户端应该考虑使用读后控制(post-read control)来验证更新，而不是在更新之后进行搜索。读后控制的设计使应用程序无需在更新后发出搜索请求——由于复制最终一致性模型，仅出于检查更新是否有效而检索条目，这是一种糟糕的形式。LDAP客户端不应该假设它对每个请求都连接到相同的目录服务器，因为架构师可能在LDAP客户端和服务器之间放置了负载平衡器或LDAP代理，或者两者都放置了。

### 修改DN

修改DN(移动/重命名条目)采用新的RDN (Relative Distinguished Name)，可选的新的父DN，以及指示是否删除条目中与旧RDN匹配的值的标志。服务器可能支持对整个目录子树进行重命名。

一个更新操作是原子的：其他操作将看到新条目或旧条目的操作结果。另一方面，LDAP没有定义多个操作的事务：如果您读取了一个条目，然后修改了它，那么另一个客户机可能在此期间已经更新了该条目。但是，服务器可以通过实现[扩展](http://tools.ietf.org/html/draft-zeilenga-ldap-txn-15)来支持这一点。

### 拓展操作

扩展操作是一种通用的LDAP操作，它可以定义不属于原始协议规范的新操作。StartTLS是最重要的扩展之一。其他示例包括“取消”和“修改密码”。

**StartTLS**

StartTLS 操作在连接上建立传输层安全性（SSL的后代）。它可以提供数据机密性（保护数据不被第三方观察）或数据完整性保护（保护数据不被篡改）。在TLS协商期间，服务器发送其X.509证书以证明其身份。客户端也可以发送证书来证明其身份。然后客户端可以使用SASL/EXTERNAL。通过使用SASL/EXTERNAL，客户端请求服务器从较低级别（例如TLS）提供的凭据中获取其身份。尽管从技术上讲，服务器可以使用在任何较低级别建立的任何身份信息，但通常是使用TLS建立的身份信息。服务器还经常在单独的端口上支持非标准的“LDAPS”（“Secure LDAP”，通常称为“LDAP over SSL”）协议，默认为 636。LDAPS与LDAP有两个方面的不同：1）在连接时，客户端和服务器在传输任何LDAP消息之前建立TLS（没有StartTLS操作）；2）LDAPS连接必须在TLS关闭时关闭。

**Abandon**

Abandon操作请求服务器中止由消息ID命名的操作。服务器不需要接受请求。无论Abandon操作是否成功都不会发送响应。类似的Cancel扩展操作就会发送响应，但并非所有实现都支持这一点。

**Unbind**

Unbind操作放弃所有未完成的操作并关闭连接。它不会有任何响应。该操作命名是有历史渊源的，与Bind操作并不是相反的。

客户端可以通过简单地关闭连接来中止会话，但他们应该使用Unbind。Unbind允许服务器优雅地关闭连接并释放资源，否则它会保留一段时间，直到发现客户端已放弃连接。它还指示服务器取消可以取消的操作，并且不对无法取消的操作发送响应

## URI方案

LDAP已经存在统一资源标识符(URI)的方案，客户端在不同程度上支持该方案，服务器在引用和延续引用中返回（参见 RFC 4516）：

```
ldap://host:port/DN?attributes?scope?filter?extensions
```

下面描述的大多数组件都是可选的。

- host为要搜索的LDAP服务器的FQDN或IP地址。
- post为LDAP服务器的网络端口（默认为389）
- DN是用来搜索基准的专有名称。
- attributes是要检索的以逗号分隔的属性列表。
- scope指定搜索范围，可以是“base”(默认值)、“one”或“sub”。
- filter为查询过滤器。例如`(objectClass=*)`在RFC4515中有定义。
- *extensions*是LDAP URL格式的扩展

举个例子，`dap://ldap.example.com/cn=John%20Doe,dc=example,dc=com`指的是在`ldap.example.com`中的`John Doe`的条目的所有用户的属性，而`ldap:///dc=example,dc=com??sub?(givenName=John)`是在默认服务器搜索条目（注意三斜杠，省略主机，双问号，省略属性）。与其他URL一样，特殊字符必须采用百分比编码。

LDAP over SSL有一个类似的非标准ldaps URI 方案。这不应与带有TLS的LDAP混淆，后者是通过使用标准ldap方案的StartTLS操作实现的。

## Schema

子树中条目的内容由目录模式、与目录信息树 (DIT) 结构有关的一组定义和约束来管理。

Directory Server的模式定义了一组规则，这些规则管理服务器可以保存的信息种类。它有许多元素，包括：

- 属性语法——提供关于可存储在属性中的信息类型的信息。
- 匹配规则——提供关于如何与属性值进行比较的信息。
- 匹配的规则使用——指出哪些属性类型可以与特定的匹配规则一起使用。
- 属性类型——定义一个对象标识符(OID)和一组可能引用给定属性的名称，并将该属性与语法和匹配规则相关联。
- 对象类（Object Classes）——定义已命名的属性集合，并将它们分类为必需和可选属性集。
- 命名形式——为条目的RDN中应包含的属性定义规则。
- 内容规则——定义关于对象类和属性的附加约束，这些约束可以与条目一起使用。
- 结构规则——定义控制给定条目可能具有的从属条目类型的规则。

属性是负责在目录中存储信息的元素，schema定义了条目中可以使用哪些属性的规则，这些属性可能具有的值的类型，以及客户端如何与这些值交互。

客户端可以通过检索适当的子模式子条目来了解服务器支持的schema元素。schema定义对象类。每个条目都必须有一个objectClass属性，其中包含schema中定义的命名类。条目类的schema定义了条目可能代表的对象类型——例如个人、组织或是域。对象类定义(object class definition)还定义了必须包含值的属性列表和可能包含值的属性列表。例如，代表一个ren的条目可能属于“top”和“person”类。“person”类中的成员资格要求条目包含“sn”和“cn”属性，并允许条目还包含“userPassword”、“telephoneNumber”以及其他属性。由于条目可能有多个ObjectClasses值，因此每个条目都有一个复杂的可选和必选的属性集，这些属性集由它所代表的对象类的联合形成。ObjectClasses可以被继承，并且单个条目可以具有多个ObjectClasses值，这些值定义了条目本身的可用和必需属性。与objectClass的schema类似的是面向对象编程中的类定义和实例，分别表示LDAP的objectClass和LDAP的entry。

目录服务器可以在条目的`subschemaSubentry`操作属性给定的基本DN处发布控制条目的目录schema。（操作属性描述目录的操作而不是用户信息，并且仅在明确请求时从搜索中返回）。

除了提供的schema元素外，服务器管理员还可以添加其他schema条目。在组织中表示个人的模式称为[白页schema(white page schema)](https://en.wikipedia.org/wiki/White_pages_schema)。

## 用法

LDAP服务器可能会针对无法自行完成的请求将引用返回给其他服务器。这需要LDAP条目的命名结构，以便可以找到拥有给定专有名称(DN)的服务器，这是在X.500目录中定义的概念，也在LDAP中使用。为组织定位LDAP服务器的另一种方法是[DNS服务器记录(SRV)](https://en.wikipedia.org/wiki/SRV_record)

一个具有域`example.org`的组织可以使用顶级LDAP DN`dc=example,dc=org`(其中dc表示域组建)。如果LDAP服务器也命名为`ldap.example.org`，则组织的顶级LDAP URL就会变为`ldap://ldap.example.org/dc=example,dc=org`。

在X.500[2008]和LDAPv3中主要使用两种常见的命名风格。这些都记录在ITU规范和IETF rfc中。原始形式将顶层对象作为国家对象，如c=US，c=FR。域组件模型使用上述模型。原始形式将顶层对象作为国家对象，如c=US，c=FR。域组件模型使用上述模型。一个基于国家命名的例子可能是`l=Locality, ou=Some Organizational Unit, o=Some Organization, c=FR`，或是在美国：`cn=Common Name, l=Locality, ou=Some Organizational Unit, o=Some Organization, st=CA, c=US`
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
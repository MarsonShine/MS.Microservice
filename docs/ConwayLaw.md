# 康威定律

定义：

`Conway’s law: Organizations which design systems are constrained to  produce designs which are copies of the communication structures of  these organizations. - Melvin Conway(1967)`

设计系统的组织其产生的设计等价于组织间的沟通结构。

反过来理解也是成立的。

`Conway’s law reversed：You won’t be able to successfully establish an  efficient organizational structure that is not supported by your system  architecture design.`

如果系统架构不支持的话，你无法建立一个高效的组织。

[Mike Amundsen ](https://links.jianshu.com/go?to=https%3A%2F%2Fwww.apiacademy.co%2Fprofile%2Fmike-amundsen)归纳了如下四个核心观点：

- 第一定律

  Communication dictates the design：组织沟通方式会通过系统设计表达出来；对于复杂的，需要协作完成的系统，沟通是必须要持续提升的问题。每个团队由 5-10 人组成，**在团队内部进行频繁的，细粒度的沟通。对团队外部，定义好接口，契约，只进行粗粒度的沟通**。这样可以降低沟通成本，同时也负责高内聚，低耦合的原则

- 第二定律

  There is never enough time to do something right, but there is always enough time to do it over：时间再多，一件事也不可能做的完美，但是总有时间会做完一件事。再敏捷开发中，就是产品迭代，总会有 bug 修复，做到持续交付，获取反馈再快速相应反馈（bug）并交付。

- 第三定律

  There is a homomorphism from the linear graph of a system to the linear graph of its design organization：线性系统和线性组织架构间有潜在的异质同态特性；意思就是说根据架构的特定，切分成对应的团队。比如整个系统包含支付模块，订单模块，那么就可以分成支付和订单两个团队独立负责。做到系统模块，服务自洽。

- 第四定律

  The structures of large systems tend to disintegrate during development, qualitatively more so than with small systems：大的系统组织总是比小系统更加倾向于分解；项目团队，系统架构随着时间的推移，不断优化，业务不断发展。团队肯定也会增加，沟通成本也会随之增加（比如人员的变动）

相关阅读：

- 《架构整洁之道》
- https://ardalis.com/conways-law-ddd-and-microservices/
- https://www.jianshu.com/p/ba2d444c89d2


namespace MS.Microservice.AspNetCore.Encryption;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class NoEncryptAttribute : Attribute;

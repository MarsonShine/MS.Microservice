namespace MS.Microservice.Lab.Application.Models.Authorization;

public sealed record UserPermissionSnapshot(int Id, IReadOnlyList<RolePermissionSnapshot> Roles);
public sealed record RolePermissionSnapshot(int Id, IReadOnlyList<ActionPermissionSnapshot> Actions);
public sealed record ActionPermissionSnapshot(string? Path);
namespace MS.Microservice.AI.Abstractions;

/// <summary>
/// A single message within a chat conversation.
/// </summary>
/// <param name="Role">The role of the message author, e.g. <c>system</c>, <c>user</c>, or <c>assistant</c>.</param>
/// <param name="Content">The text content of the message.</param>
public sealed record AIChatMessage(string Role, string Content);
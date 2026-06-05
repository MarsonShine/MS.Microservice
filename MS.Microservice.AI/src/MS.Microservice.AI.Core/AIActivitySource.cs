using System.Diagnostics;

namespace MS.Microservice.AI.Core;

public static class AIActivitySource
{
    public static ActivitySource Instance { get; } = new("MS.Microservice.AI");
}
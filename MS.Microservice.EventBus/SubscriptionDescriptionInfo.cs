using System;
using System.Diagnostics.CodeAnalysis;

namespace MS.Microservice.EventBus
{
    internal class SubscriptionDescriptionInfo
    {
        public SubscriptionDescriptionInfo(Type handlerType)
        {
            HandlerType = handlerType;
        }
        public Type HandlerType { get; set; }
    }
}
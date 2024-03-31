using System;

namespace MinimalAzureServiceBus.Core
{
    [AttributeUsage(AttributeTargets.Method)]
    public abstract class MessagingAttribute : Attribute
    {
        public string Name { get; }

        protected MessagingAttribute(string name = "") => Name = name;
    }
}
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MinimalAzureServiceBus.Core.Models
{
    internal sealed class DelegateInfo
    {
        internal static Func<Delegate, DelegateInfo> From = delegateToCheck => new DelegateInfo(delegateToCheck);

        internal DelegateInfo(Delegate delegateToCheck)
        {
            Delegate = delegateToCheck;
            Parameters = delegateToCheck.Method.GetParameters().ToDictionary(x => x.Name ?? "unnamed", x => x.ParameterType);
            ReturnType = delegateToCheck.Method.ReturnType;

            if (ReturnType != typeof(Task) && ReturnType.BaseType != typeof(Task)) return;

            ReturnsTask = true;

            if (ReturnType.IsGenericType)
                InnerReturnType = ReturnType.GetGenericArguments()[0];
        }

        public Delegate Delegate { get; }
        public Dictionary<string, Type> Parameters { get; }
        public Type ReturnType { get; }
        public Type? InnerReturnType { get; }
        public bool ReturnsTask { get; }
    }
}
using System;
using System.Collections.Generic;

namespace ES.IoC
{
    public interface IExecutionContext
    {
        IList<string> ExcludePrefixes { get; }
        T Find<T>() where T : class;
        T[] FindAll<T>() where T : class;
        IEnumerable<Tuple<string, IEnumerable<string>>> Registered();
        IExecutionContext OnWireException(Action<Exception> handler);
        string BootstrapCodeFor<T>();
    }
}

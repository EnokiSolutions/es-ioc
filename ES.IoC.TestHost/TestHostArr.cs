using System.Collections.Generic;
using System.Linq;
using ES.IoC.TestPlugin.Interface;
using ES.IoC.Wiring;

namespace ES.IoC.TestHost
{
    [Wire]
    public sealed class TestHostArr : ITestHostArr
    {
        private readonly IDoSomething[] _plugins;

        public TestHostArr(IDoSomething[] plugins)
        {
            _plugins = plugins;
        }

        IEnumerable<string> ITestHost.Run(string toWhat)
        {
            return _plugins.Select(x => x.DoSomething(toWhat));
        }
    }
}

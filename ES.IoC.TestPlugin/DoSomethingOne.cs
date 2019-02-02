using ES.IoC.TestPlugin.Interface;
using ES.IoC.Wiring;

namespace ES.IoC.TestPlugin
{
    [Wire]
    public sealed class DoSomethingOne : IDoSomething
    {
        private readonly IOnePrefix _onePrefix;

        public DoSomethingOne(IOnePrefix onePrefix)
        {
            _onePrefix = onePrefix;
        }

        string IDoSomething.DoSomething(string toThis)
        {
            return _onePrefix.Prefix + ": " + toThis;
        }
    }
}

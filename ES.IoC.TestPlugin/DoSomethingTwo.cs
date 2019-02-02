using ES.IoC.TestPlugin.Interface;
using ES.IoC.Wiring;

namespace ES.IoC.TestPlugin
{
    [Wire(typeof(IDoSomething))]
    public sealed class DoSomethingTwo : IDoSomething
    {
        string IDoSomething.DoSomething(string toThis)
        {
            return $"TWO: {toThis}";
        }
    }
}

using ES.IoC.Wiring;

namespace ES.IoC.TestPlugin
{
    [Wire]
    public sealed class OnePrefix : IOnePrefix
    {
        string IOnePrefix.Prefix => "ONE";
    }
}

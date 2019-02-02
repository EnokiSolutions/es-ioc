using System;

namespace ES.IoC.Wiring
{
    public sealed class Wire : Attribute
    {
        public Wire()
        {
            InterfaceType = null;
        }

        public Wire(Type interfaceType)
        {
            InterfaceType = interfaceType;
        }

        public Type InterfaceType { get; }
    }
}

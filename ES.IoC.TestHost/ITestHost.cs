using System.Collections.Generic;

namespace ES.IoC.TestHost
{
    public interface ITestHost
    {
        IEnumerable<string> Run(string toWhat);
    }
}

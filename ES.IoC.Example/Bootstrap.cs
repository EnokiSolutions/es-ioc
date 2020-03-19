using System;
using System.IO;
using System.Linq;
using System.Text;

namespace ES.IoC.Example
{
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    internal static class Bootstrap
    {
        public static T Update<T>() where T : class
        {
            var executionContext = ES.IoC.ExecutionContext.Create();
            var bootstrapFileName = Path.Combine(Environment.CurrentDirectory, "Bootstrap.cs");
            if (File.Exists(bootstrapFileName))
            {
                System.Diagnostics.Debug.WriteLine($@"
****************************************************************************************************
Updating {bootstrapFileName}
****************************************************************************************************
");
                var existingBootstrapCodeLines = File.ReadAllLines(bootstrapFileName);
                var bootstrapCodeFor = executionContext.BootstrapCodeFor<T>();
                var sb = new StringBuilder();
                var i = 0;
                while (i < existingBootstrapCodeLines.Length)
                {
                    var line = existingBootstrapCodeLines[i];
                    if (line == "#if USE_ES_IOC_BOOTSTRAP // GENERATED")
                    {
                        sb.AppendLine(line);
                        sb.Append(bootstrapCodeFor);
                        while (i < existingBootstrapCodeLines.Length)
                        {
                            ++i;
                            if (i >= existingBootstrapCodeLines.Length)
                                throw new Exception(
                                    "Unexpected end of bootstrap code, nothing after: #if USE_ES_IOC_BOOTSTRAP");
                            line = existingBootstrapCodeLines[i];
                            if (line == "#endif // GENERATED")
                                break;

                        }
                    }

                    sb.AppendLine(line);
                    ++i;
                }

                var newFileContents = sb.ToString();
                var originalFileContents = string.Join(Environment.NewLine, existingBootstrapCodeLines);
                if (newFileContents != originalFileContents)
                {
                    File.WriteAllText(bootstrapFileName, newFileContents);
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($@"
****************************************************************************************************
Not updating {bootstrapFileName} because it doesn't exist.
Rerun in the same directory as {bootstrapFileName} to update it. 
****************************************************************************************************
");
            }

            return executionContext.Find<T>();
        }
        public static T Boot<T>() where T : class
        {
#if USE_ES_IOC_BOOTSTRAP
            return (T)Bootstrap.Create();
#else
            return Bootstrap.Update<T>();
#endif
        }
        
#if USE_ES_IOC_BOOTSTRAP // GENERATED
        internal static ES.IoC.Example.IRun Create()
        {
            var _000_startupmessage = new ES.IoC.Example.StartupMessage();
            var _001_commandjoin = new ES.IoC.Example.CommandJoin();
            var _002_commandcount = new ES.IoC.Example.CommandCount();
            var _003_commandtoconsoleadapterwirer = ES.IoC.Example.CommandToConsoleAdapterWirer.Wire(new ES.IoC.Example.ICommand[]{_001_commandjoin,_002_commandcount});
            var _004_value_1 = new ES.IoC.Example.Value<System.Int32>();
            var _005_value_1 = new ES.IoC.Example.Value<System.String>();
            var _006_run = new ES.IoC.Example.Run(_000_startupmessage, new ES.IoC.Example.ICommandToConsole[]{_003_commandtoconsoleadapterwirer[0],_003_commandtoconsoleadapterwirer[1]}, _004_value_1, _005_value_1);
            return _006_run;
        }
#endif // GENERATED
    }
}

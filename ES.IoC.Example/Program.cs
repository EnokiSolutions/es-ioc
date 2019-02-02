using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ES.IoC.Wiring;

namespace ES.IoC.Example
{
    internal interface IValue<T> {
        T Value { get; set; }
    }

    [Wire]
    internal class Value<T> : IValue<T>
    {
        T IValue<T>.Value { get; set; }
    }
    internal interface ICommand
    {
        string Name { get; }
        string Exec(string[] args);
    }

    [Wire]
    internal sealed class CommandJoin : ICommand
    {
        string ICommand.Name => "Echo";

        string ICommand.Exec(string[] arg)
        {
            return string.Join(" ", arg);
        }
    }

    [Wire]
    internal sealed class CommandCount : ICommand
    {
        string ICommand.Name => "Count";

        string ICommand.Exec(string[] args)
        {
            return args.Length.ToString();
        }
    }

    internal interface ICommandToConsole
    {
        string Name { get; }
        void Exec(string[] args);
    }

    internal sealed class CommandToConsoleAdapter : ICommandToConsole
    {
        private readonly ICommand _command;

        public CommandToConsoleAdapter(ICommand command) => _command = command;

        string ICommandToConsole.Name => _command.Name;

        void ICommandToConsole.Exec(string[] args)
        {
            Console.WriteLine(_command.Exec(args));
        }
    }
    
    [Wirer]
    internal static class CommandToConsoleAdapterWirer { 

        public static ICommandToConsole[] Wire(ICommand[] commands) =>
            commands.Select(command => new CommandToConsoleAdapter(command) as ICommandToConsole).ToArray();
    }
    
    internal interface IStartupMessage
    {
        string StartupMessage { get; }
    }

    [Wire]
    internal class StartupMessage : IStartupMessage
    {
        string IStartupMessage.StartupMessage => "Hello World";
    }
    
    internal interface IRun
    {
        void Run();
    }

    [Wire]
    internal sealed class Run : IRun
    {
        private readonly IStartupMessage _startupMessage;
        private ICommandToConsole[] _commandToConsoles;
        private IValue<int> _vint;
        private IValue<string> _vstring;

        public Run(IStartupMessage startupMessage, ICommandToConsole[] commandToConsoles, IValue<int> vint, IValue<string> vstring) =>
            (_startupMessage, _commandToConsoles, _vint, _vstring) = (startupMessage, commandToConsoles, vint, vstring);

        void IRun.Run()
        {
            Console.WriteLine(_startupMessage.StartupMessage);
        }
    }
    internal static class Program
    {
        public static void Main(string[] args)
        {
            var run = Bootstrap.Boot<IRun>();
            run.Run();
        }

    }
}

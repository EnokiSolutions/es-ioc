using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using ES.IoC.TestHost;
using ES.IoC.TestPlugin;
using ES.IoC.TestPlugin.Interface;
using ES.IoC.Wiring;
using FakeItEasy;
using FakeItEasy.Sdk;
using NUnit.Framework;

namespace ES.IoC.Test
{
    internal interface ILog
    {
        void Log(string what);
    }

    [Wire]
    internal class Log : ILog
    {
        void ILog.Log(string what)
        {
            Debug.WriteLine(what);
        }
    }

    [SuppressMessage("Microsoft.Design", "CA1001:TypesThatOwnDisposableFieldsShouldBeDisposable")]
    [TestFixture]
    public sealed class TestFixture
    {
        // required to prevent the assembly from being stripped out.
        // todo: there must be a better way to handle this. e.g. telling the linking not to strip the assembly out.
        // ReSharper disable once UnusedMember.Local
        private static Type _unusedReferenece = typeof(DoSomethingOne);

        [Test]
        public void TestArray()
        {
            var executionContext =
                ExecutionContext
                    .Create()
                    .OnWireException(ex => Debug.WriteLine(ex.ToString()));

            var testHost = executionContext.Find<ITestHostArr>();
            var results = testHost.Run("test").ToArray();
            Assert.That(results.Any(x => x == "ONE: test"));
            Assert.That(results.Any(x => x == "TWO: test"));
            var registered = executionContext.Registered().ToArray();
            foreach (var reg in registered)
                Debug.WriteLine($"{reg.Item1} {string.Join(",", reg.Item2)}");
            foreach (var entry in new[]
            {
                new
                {
                    i = "ES.IoC.TestPlugin.Interface.IDoSomething",
                    c = new[] {"ES.IoC.TestPlugin.DoSomethingOne", "ES.IoC.TestPlugin.DoSomethingTwo"}
                },
                new {i = "ES.IoC.TestPlugin.IOnePrefix", c = new[] {"ES.IoC.TestPlugin.OnePrefix"}},
                new
                {
                    i = "ES.IoC.TestHost.ITestHost",
                    c = new[] {"ES.IoC.TestHost.TestHostArr"}
                },
                new {i = "ES.IoC.TestHost.ITestHostArr", c = new[] {"ES.IoC.TestHost.TestHostArr"}}
            })
            {
                var re = registered.FirstOrDefault(x => x.Item1.StartsWith(entry.i));
                Assert.NotNull(re);
                foreach (var ce in entry.c)
                    Assert.Greater(re.Item2.Count(x => x.StartsWith(ce)), 0);
            }
        }

        [Test]
        public void TestDoubleResolve()
        {
            var executionContext = ExecutionContext.Create();
            {
                var q = executionContext.FindAll<IDoSomething>().ToArray();
                var p = executionContext.FindAll<IDoSomething>().ToArray();

                for (var i = 0; i < q.Length; i++)
                    Assert.AreEqual(q[i], p[i]);
            }
        }

        [Test]
        public void TestForTest()
        {
            var executionContext = ExecutionContext.ForTest<ITestHostArr>(Create.Fake);
            var fakeDoSomethings = executionContext.FindAll<IDoSomething>().ToArray();
            
            // we expect 2 because there are 2 concrete types of IDoSomething in the assemblies refereneced.
            Assert.AreEqual(2, fakeDoSomethings.Length);
            A.CallTo(() => fakeDoSomethings[0].DoSomething("asdf")).Returns("foo1");
            A.CallTo(() => fakeDoSomethings[1].DoSomething("asdf")).Returns("foo2");
            
            var harr = executionContext.Find<ITestHostArr>();
            var actual = harr.Run("asdf").ToArray();
            CollectionAssert.AreEqual(new[] {"foo1", "foo2"}, actual);
        }

        [Test]
        public void TestMissing()
        {
            var executionContext = ExecutionContext.Create();
            Assert.Throws<Exception>(() => executionContext.Find<IQueryable>());
        }

        [Test]
        public void TestMultipleGenericInterfaceViaAllWiring()
        {
            var r = ExecutionContext.Create();
            var first = r.Find<ISet<int>>();
            var second = r.Find<IGet<int>>();
            var afirst = r.FindAll<ISet<int>>().ToArray();
            var asecond = r.FindAll<IGet<int>>().ToArray();
            Assert.AreEqual(1, afirst.Length);
            Assert.AreEqual(1, asecond.Length);

            Assert.AreSame(first, afirst[0]);
            Assert.AreSame(first, second);
            Assert.AreSame(first, asecond[0]);
            var sg = r.Find<ITakeSetGet<int>>();
            Assert.AreSame(first, sg.Set);
            Assert.AreSame(first, sg.Get);
        }

        [Test]
        public void TestMultipleInterfaceWiring()
        {
            var r = ExecutionContext.Create();
            var first = r.Find<IFirst>();
            var second = r.Find<ISecond>();
            var afirst = r.FindAll<IFirst>().ToArray();
            var asecond = r.FindAll<ISecond>().ToArray();
            Assert.AreEqual(1, afirst.Length);
            Assert.AreEqual(1, asecond.Length);
            Assert.AreSame(first, afirst[0]);
            Assert.AreSame(first, second);
            Assert.AreSame(first, asecond[0]);
        }

        [Test]
        public void TestResolveAll()
        {
            var executionContext = ExecutionContext.Create();
            {
                var q = executionContext.FindAll<IDoSomething>().ToArray();
                Assert.Greater(q.Length, 1);
                foreach (var e in q)
                {
                    var type = e.GetType();
                    Assert.IsFalse(type.IsInterface);
                    Assert.NotNull(type.GetInterface(typeof(IDoSomething).FullName));
                }
            }
            {
                var q = executionContext.FindAll<IQueryable>().ToArray();
                Assert.AreEqual(0, q.Length);
            }
        }

        [Test]
        public void TestWire()
        {
            var r = ExecutionContext.Create();
            var t = r.Find<ITestWire>();
            Assert.AreEqual(4, t.Bar());
        }

        [Test]
        public void TestWirer()
        {
            var r = ExecutionContext.Create();
            var t = r.FindAll<ITestWirer>();
            Assert.AreEqual(3, t.Length);
            foreach (var e in t)
                Assert.AreEqual(9, e.Foo());
        }
    }

    internal interface IExplictInstance
    {
        string Get();
    }

    sealed class ExplicitInstance : IExplictInstance
    {
        private readonly string _arg;

        public ExplicitInstance(string arg)
        {
            _arg = arg;
        }

        string IExplictInstance.Get()
        {
            return _arg;
        }
    }

    internal interface IFirst
    {
        string Get();
    }

    internal interface ISecond
    {
        string Get();
    }

    [Wire]
    internal class DoesBoth : IFirst, ISecond
    {
        public DoesBoth()
        {
            Console.WriteLine("{0}", Environment.StackTrace);
        }

        string IFirst.Get()
        {
            return "First";
        }

        string ISecond.Get()
        {
            return "Second";
        }
    }

    internal interface ISet<in T>
    {
        void Set(T t);
    }

    internal interface IGet<out T>
    {
        T Get();
    }

    [Wire]
    internal class GetSet<T> : IGet<T>, ISet<T>
    {
        private T _t;

        T IGet<T>.Get()
        {
            return _t;
        }

        void ISet<T>.Set(T t)
        {
            _t = t;
        }
    }

    [Wire]
    internal class TakeSetGet<T> : ITakeSetGet<T>
    {
        private readonly IGet<T> _get;
        private readonly ISet<T> _set;

        public TakeSetGet(ISet<T> set, IGet<T> get)
        {
            _set = set;
            _get = get;
        }

        ISet<T> ITakeSetGet<T>.Set
        {
            get { return _set; }
        }

        IGet<T> ITakeSetGet<T>.Get
        {
            get { return _get; }
        }
    }

    internal interface ITakeSetGet<T>
    {
        ISet<T> Set { get; }
        IGet<T> Get { get; }
    }

    internal interface ITestWire
    {
        int Bar();
    }

    [Wire]
    internal class TestWire : ITestWire
    {
        int ITestWire.Bar()
        {
            return 4;
        }
    }

    internal interface ITestWirer
    {
        int Foo();
    }

    internal class TestWirerImpl : ITestWirer
    {
        int ITestWirer.Foo()
        {
            return 9;
        }
    }

    [Wirer]
    internal static class WirerTest
    {
        public static ITestWirer[] Wire()
        {
            return new ITestWirer[]
            {
                new TestWirerImpl(),
                new TestWirerImpl(),
                new TestWirerImpl()
            };
        }
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;

namespace ES.IoC
{
    public sealed class ExecutionContext : IExecutionContext
    {
        private static readonly string[] AllAssemblies = {""};

        // ReSharper disable once InconsistentNaming
        private static readonly List<string> _excludePrefixes = new List<string> {"System", "mscorlib", "JetBrains.", "DynamicProxyGenAssembly2" };

        private static int _varNameCounter;

        private static readonly ObjectInstance[] EmptyObjectInstanceArray = { };
        private readonly string[] _assemblyPrefixes;

        private readonly IDictionary<string, IList<ComponentInfo>> _components =
            new Dictionary<string, IList<ComponentInfo>>();

        private readonly Dictionary<string, Assembly> _loaded =
            new Dictionary<string, Assembly>(StringComparer.OrdinalIgnoreCase);

        private readonly HashSet<string> _seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Func<Type, object> _faker;
        private bool _setup;
        private Action<Exception> _wireExceptionAction;

        private ExecutionContext(IEnumerable<string> assemblyPrefixes, Func<Type, object> faker)
        {
            _assemblyPrefixes = assemblyPrefixes?.ToArray() ?? AllAssemblies;
            _faker = faker;
        }

        private static string ServiceName(Type t)
        {
            var name = t.Name.Replace("`","_");
            if (t.Namespace != null)
            {
                name = $"{t.Namespace}.{name}";
            }
            if (t.IsGenericType)
            {
                name += $"<{string.Join(",", t.GetGenericArguments().Select(x=>x.IsGenericParameter?"":ServiceName(x)))}>";
            }
            return name;
        }

        private static string GenericDefinitionServiceName(Type t)
        {
            // convert IDictionary<int,string> -> IDictionary<,>
            // needed for initial lookup of the generic type factory to use.
            var name = t.Name.Replace("`", "_");
            if (t.Namespace != null)
            {
                name = $"{t.Namespace}.{name}";
            }
            name += $"<{string.Join(",", t.GetGenericArguments().Select(x => ""))}>";
            return name;
        }

        T IExecutionContext.Find<T>()
        {
            Setup();
            var oi = Resolve(typeof(T));

            return (T) oi.Instance;
        }

        T[] IExecutionContext.FindAll<T>()
        {
            Setup();
            var ois = ResolveAll(typeof(T));
            if (ois == null)
                return new T[] { };
            return ois.Select(oi => (T) oi.Instance).ToArray();
        }

        IEnumerable<Tuple<string, IEnumerable<string>>> IExecutionContext.Registered()
        {
            Setup();
            return _components.Select(kvp => Tuple.Create(kvp.Key, kvp.Value.Select(c => c.Name)));
        }

        IExecutionContext IExecutionContext.OnWireException(Action<Exception> action)
        {
            _wireExceptionAction = action;
            return this;
        }

        IList<string> IExecutionContext.ExcludePrefixes { get; } = _excludePrefixes;

        public string BootstrapCodeFor<T>()
        {
            Setup();
            var bootstrapContents = new StringBuilder();
            var type = typeof(T);
            if (!type.GetTypeInfo().IsInterface)
                throw new InvalidOperationException("T must be an interface type");
            bootstrapContents.AppendLine($"        internal static {CodeTypeName(type)} Create()");
            bootstrapContents.AppendLine("        {");
            var oi = Resolve(type, bootstrapContents);
            bootstrapContents.AppendLine($"            return {oi.VarName};");
            bootstrapContents.AppendLine("        }");
            return bootstrapContents.ToString();
        }

        public static IExecutionContext Create(IEnumerable<string> assemblyPrefixes = null)
        {
            return new ExecutionContext(assemblyPrefixes, null);
        }

        public static IExecutionContext ForTest<T>(Func<Type, object> faker,
            IEnumerable<string> assemblyPrefixes = null)
        {
            return new ExecutionContext(assemblyPrefixes, type => type == typeof(T) ? null : faker(type));
        }

        private ObjectInstance Resolve(Type serviceType, StringBuilder sb = null)
        {
            var serviceNamesToTry = new List<string> {ServiceName(serviceType)};
            
            if (serviceType.IsGenericType)
            {
                serviceNamesToTry.Add(GenericDefinitionServiceName(serviceType));
            }
            
            foreach (var serviceName in serviceNamesToTry)
            {
                if (!_components.TryGetValue(serviceName, out var componentInfos))
                    continue;
                
                var first = componentInfos.FirstOrDefault();
                if (first == null)
                    continue;

                var os = Instances(first, serviceType, sb);
                var oi = os.FirstOrDefault();
                if (oi != null)
                {
                    return oi;
                }
            }

            throw new Exception("Could not resolve concrete type for interface: " + string.Join(", ", serviceNamesToTry));
        }

        private ObjectInstance[] ResolveAll(Type serviceType, StringBuilder sb = null)
        {
            var serviceName = ServiceName(serviceType);
            return !_components.TryGetValue(serviceName, out var componentInfos)
                ? EmptyObjectInstanceArray
                : componentInfos.Select(x => Instances(x, serviceType, sb)).SelectMany(x => x).ToArray();
        }

        private object ToArrayOfType(Type type, ObjectInstance[] ois)
        {
            var a = (object[]) Array.CreateInstance(type, new[] {ois.Length});
            for (var i = 0; i < ois.Length; ++i)
                a[i] = ois[i].Instance;
            return a;
        }

        private string CodeTypeName(Type type)
        {
            var fullName = type.FullName;
            if (type.IsGenericType)
            {
                // fullName is useless for generics, build it up from the parts
                var name = type.Name;
                name = name.Substring(0, name.IndexOf("`"));
                fullName = (type.Namespace != null ? type.Namespace + "." : "") + name + $"<{string.Join(",",type.GetGenericArguments().Select(CodeTypeName))}>";
            }
            return fullName;
        }
        private IList<ObjectInstance> Instances(ComponentInfo componentInfo, Type serviceType, StringBuilder sb)
        {
            if (componentInfo.Generic)
            {
                var specializedType = componentInfo.Type.MakeGenericType(serviceType.GetGenericArguments());
                componentInfo = RegisterType(specializedType,serviceType);
            }

            if (componentInfo.Initialized)
                return componentInfo.Instances;

            var existingInstancesOfThisConcreteType =
                _components
                    .Values
                    .SelectMany(i => i)
                    .Where(x => x.Initialized)
                    .SelectMany(x => x.Instances)
                    .Where(y =>
                        {
                            var type = y.Instance.GetType();
                            return type == componentInfo.Type;
                        }
                    )
                    .ToArray();

            if (existingInstancesOfThisConcreteType.Any())
            {
                // don't create more than one instance of the concrete type
                componentInfo.Instances = existingInstancesOfThisConcreteType;
                componentInfo.Initialized = true;
                return existingInstancesOfThisConcreteType;
            }

            var componentInfoDependencies = componentInfo.Dependencies;

            var t = _faker?.Invoke(componentInfo.ServiceType);
            if (t != null)
            {
                componentInfo.Instances.Add(new ObjectInstance(componentInfo, t));
                componentInfo.Initialized = true;
                return componentInfo.Instances;
            }

            if (componentInfoDependencies == null)
                throw new Exception(
                    $"Can\'t determine component dependencies. Check that {componentInfo.Type.Name} has a public ctor that takes only interfaces, arrays of interfaces, or nothing.");
 
            foreach (var dependencyInfo in componentInfoDependencies)
            {
                var dependencyInfoServiceType = dependencyInfo.ServiceType;

                if (dependencyInfo.Modifier == DependencyInfo.TypeModifier.One)
                {
                    dependencyInfo.ObjectInstance = Resolve(dependencyInfoServiceType, sb);
                    dependencyInfo.Instance = dependencyInfo.ObjectInstance.Instance;
                }
                
                if (dependencyInfo.Modifier != DependencyInfo.TypeModifier.Array)
                    continue;

                dependencyInfo.ObjectInstances = ResolveAll(dependencyInfoServiceType, sb);
                if (dependencyInfo.ObjectInstances == null)
                {
                    throw new InvalidOperationException($"Can't resolve dependency {dependencyInfoServiceType}[] creating {componentInfo.ServiceType}");
                }
                dependencyInfo.Instance = ToArrayOfType(dependencyInfoServiceType, dependencyInfo.ObjectInstances);
            }

            var parameters =
                componentInfoDependencies
                    .Select(
                        d => d.Instance
                    )
                    .ToArray();

            var newObjs = new List<object>();

            var parametersCode = string.Empty;
            if (sb != null)
                parametersCode = string.Join(", ",
                    componentInfoDependencies.Select(
                        d =>
                        {
                            return d.ObjectInstance != null ? d.ObjectInstance.VarName : $"new {CodeTypeName(d.ServiceType)}[]{{{string.Join(",", d.ObjectInstances.Select(oid => oid.VarName))}}}";
                        }
                    ));

            if (componentInfo.Ctor != null)
            {
                var obj = componentInfo.Ctor.Invoke(parameters);
                newObjs.Add(obj);
                var oi = new ObjectInstance(componentInfo, obj);
                if (sb != null)
                {
                    var type = obj.GetType();
                    sb.AppendLine($"            var {oi.VarName} = new {CodeTypeName(type)}({parametersCode});");
                }
                componentInfo.Instances.Add(oi);
            }
            else if (componentInfo.Builder != null)
            {
                var temp = componentInfo.Builder.Invoke(null, parameters);

                switch (componentInfo.ReturnType)
                {
                    case DependencyInfo.TypeModifier.One:
                    {
                        var oi = new ObjectInstance(componentInfo, temp);
                        componentInfo.Instances.Add(oi);
                        sb?.AppendLine(
                            $"            var {oi.VarName} = {CodeTypeName(componentInfo.Type)}.{componentInfo.Builder.Name}({parametersCode});");
                        newObjs.Add(temp);
                        break;
                    }
                    case DependencyInfo.TypeModifier.Array:
                    {
                        var rOi = new ObjectInstance(componentInfo, temp);
                        sb?.AppendLine(
                            $"            var {rOi.VarName} = {CodeTypeName(componentInfo.Type)}.{componentInfo.Builder.Name}({parametersCode});");
                        var index = 0;
                        foreach (var obj in (object[]) temp)
                        {
                            var oi = new ObjectInstance(componentInfo, obj,$"{rOi.VarName}[{index++}]");
                            componentInfo.Instances.Add(oi);
                            newObjs.Add(obj);
                        }
                        break;
                    }
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }

            if (newObjs.Count > 0)
                foreach (var obj in newObjs)
                foreach (var iface in
                    obj
                        .GetType()
                        .GetTypeInfo()
                        .GetInterfaces()
                        .Where(x => x != componentInfo.ServiceType))
                {
                    IList<ComponentInfo> ci = null;
                    if (iface.AssemblyQualifiedName != null &&
                        !_components.TryGetValue(ServiceName(iface), out ci))
                        continue;
                    var obj1 = obj;
                    if (ci == null)
                        continue;
                    foreach (var c in ci.Where(c => c.Instances.Count(oi => oi.Instance == obj1) == 0))
                        c.Instances.Add(new ObjectInstance(componentInfo, obj));
                }
            componentInfo.Initialized = true;
            return componentInfo.Instances;
        }

        private void LoadReferenced(Assembly assembly)
        {
            if (_loaded.ContainsKey(assembly.FullName))
                return;
            Debug.WriteLine($"Loaded: {assembly.FullName}");
            _loaded.Add(assembly.FullName, assembly);

            foreach (var assemblyName in assembly.GetReferencedAssemblies())
            {
                if (_excludePrefixes.Any(x => assemblyName.FullName.StartsWith(x)))
                    continue;

                if (_loaded.ContainsKey(assemblyName.FullName))
                    continue;
                Debug.WriteLine($"Forcing Load: {assemblyName.FullName}");

                var loadedAssembly = Assembly.Load(assemblyName);
                LoadReferenced(loadedAssembly);
            }
        }

        private void Setup()
        {
            if (_setup) return;
            _setup = true;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                LoadReferenced(assembly);
            LoadReferenced(Assembly.GetExecutingAssembly());
            Wire();
        }

        private void Wire()
        {
            var assemblies =
                    _loaded
                        .Where(
                            a =>
                                _assemblyPrefixes
                                    .Any(
                                        assemblyPrefix =>
                                            a.Key.StartsWith(assemblyPrefix)
                                    )
                        )
                        .Where(a => NotAlreadySeen(_seen, a.Value)) // remove duplicates, keeper older entries in order.
                        .Select(a => a.Value)
                        .ToArray()
                ;

            foreach (var assembly in assemblies)
                try
                {
                    var types = assembly.GetTypes();
                    foreach (var type in types)
                        try
                        {
                            var attrs = type.GetTypeInfo().GetCustomAttributes(true)
                                .Where(x => x.GetType().Name == "Wire").ToArray();
                            foreach (var attr in attrs)
                            {
                                var pi = attr.GetType().GetTypeInfo().GetProperty("InterfaceType");
                                if (pi != null)
                                    if (pi.GetValue(attr, null) is Type interfaceType)
                                    {
                                        RegisterType(type, interfaceType);
                                        continue;
                                    }
                                foreach (var i in type.GetTypeInfo().GetInterfaces())
                                    RegisterType(type, i);
                            }
                        }
                        catch (Exception ex)
                        {
                            _wireExceptionAction(ex);
                        }
                }
                catch (Exception ex)
                {
                    _wireExceptionAction(ex);
                }
            foreach (var assembly in assemblies)
                try
                {
                    var types = assembly.GetTypes();
                    foreach (var type in types)
                        try
                        {
                            var attrs = type.GetTypeInfo().GetCustomAttributes(true)
                                .Where(x => x.GetType().Name == "Wirer")
                                .ToArray();
                            if (attrs.Length <= 0) continue;

                            var mi = type.GetTypeInfo().GetMethod("Wire");
                            if (mi != null && mi.IsStatic)
                                RegisterWired(type, mi);
                            else
                            {
                                throw new InvalidOperationException($"Type {CodeTypeName(type)} does not have a static Wire method.");
                            }
                        }
                        catch (Exception ex)
                        {
                            _wireExceptionAction(ex);
                        }
                }
                catch (Exception ex)
                {
                    _wireExceptionAction(ex);
                }
        }

        //[ExcludeFromCodeCoverage]
        private static bool NotAlreadySeen(HashSet<string> seen, Assembly a)
        {
            if (seen.Contains(a.FullName))
                return false;
            seen.Add(a.FullName);
            return true;
        }

        private void RegisterWired(Type type, MethodInfo mi)
        {
            var name = ServiceName(type);
            var interfaceType = mi.ReturnType;
            var rtype = DependencyInfo.TypeModifier.One;
            if (interfaceType.IsArray)
            {
                interfaceType = interfaceType.GetElementType();
                rtype = DependencyInfo.TypeModifier.Array;
            }
            var serviceName = ServiceName(interfaceType);
            if (!_components.ContainsKey(serviceName))
                _components[serviceName] = new List<ComponentInfo>();
            var componentInfos = _components[serviceName];
            if (componentInfos.Any(x => x.Builder == mi && x.Type == type))
                return;
            var componentInfo = new ComponentInfo
            {
                Name = name,
                Type = type,
                ServiceType = interfaceType,
                Builder = mi,
                ReturnType = rtype,
                Generic = type.IsGenericTypeDefinition,
                Dependencies = mi
                    .GetParameters()
                    .Select(DependencyInfoFromParameterInfo)
                    .ToArray()
            };

            componentInfos.Add(componentInfo);
        }

        private ComponentInfo RegisterType(Type type, Type interfaceType, IEnumerable<object> instances = null)
        {
            try
            {
                var name = ServiceName(type);
                var interfaceName = ServiceName(interfaceType);

                if (!_components.ContainsKey(interfaceName))
                    _components[interfaceName] = new List<ComponentInfo>();

                var componentInfos = _components[interfaceName];
                var componentInfo =
                    componentInfos.FirstOrDefault(x => x.ServiceType == interfaceType && x.Type == type);
                if (componentInfo != null)
                    return componentInfo;
                
                componentInfo = new ComponentInfo
                {
                    Name = name,
                    Type = type,
                    ServiceType = interfaceType,
                    Generic = type.IsGenericTypeDefinition,
                };

                if (instances != null)
                {
                    componentInfo.Instances = instances.Select(i => new ObjectInstance(componentInfo, i)).ToArray();
                    componentInfo.Initialized = true;
                }
                var ctor = type
                    .GetTypeInfo()
                    .GetConstructors()
                    .Where(
                        x => x
                            .GetParameters()
                            .All(
                                p =>
                                {
                                    var pi = p.ParameterType;
                                    return pi.GetTypeInfo().IsInterface ||
                                           (pi.IsArray &&
                                            pi.GetElementType().GetTypeInfo()
                                                .IsInterface);
                                }
                            )
                    )
                    .OrderByDescending(x => x.GetParameters().Length)
                    .FirstOrDefault();

                componentInfo.Ctor = ctor;
                if (ctor != null)
                    componentInfo.Dependencies =
                        ctor
                            .GetParameters()
                            .Select(DependencyInfoFromParameterInfo)
                            .ToArray();

                componentInfos.Add(componentInfo);
                return componentInfo;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("- Component name={0} type={1} ServiceType={2}\n{3}",
                    ServiceName(type),
                    type.FullName,
                    interfaceType.FullName,
                    ex);
                throw;
            }
        }

        private static DependencyInfo DependencyInfoFromParameterInfo(ParameterInfo x)
        {
            DependencyInfo dependencyInfoFromParameterInfo;
            if (x.ParameterType.IsArray)
            {
                dependencyInfoFromParameterInfo = new DependencyInfo
                {
                    Modifier = DependencyInfo.TypeModifier.Array,
                    ServiceType = x.ParameterType.GetElementType()
                };
            }
            else
            {
                dependencyInfoFromParameterInfo = new DependencyInfo
                {
                    Modifier = DependencyInfo.TypeModifier.One,
                    ServiceType = x.ParameterType
                };
            }
            
            return dependencyInfoFromParameterInfo;
        }

        private class ComponentInfo
        {
            public MethodInfo Builder; // used by [Wired] "Method" based construction.
            public ConstructorInfo Ctor;
            public DependencyInfo[] Dependencies;
            public bool Initialized;
            public IList<ObjectInstance> Instances = new List<ObjectInstance>();
            public string Name;
            public DependencyInfo.TypeModifier ReturnType;
            public Type ServiceType;
            public Type Type;
            public bool Generic;
        }

        private class DependencyInfo
        {
            public enum TypeModifier
            {
                One,
                Array,
            }

            public object Instance;
            public TypeModifier Modifier;

            public ObjectInstance ObjectInstance;
            public ObjectInstance[] ObjectInstances;
            public Type ServiceType;
        }

        private sealed class ObjectInstance
        {
            public readonly ComponentInfo ComponentInfo;
            public readonly object Instance;
            public readonly string VarName;

            public ObjectInstance(ComponentInfo componentInfo, object instance, string varName = null)
            {
                ComponentInfo = componentInfo;
                Instance = instance;
                var typeName = componentInfo.Type.Name.Replace("`","_");
                VarName = varName ?? $"_{_varNameCounter++:D3}_{typeName.ToLowerInvariant()}";
            }
        }
    }
}

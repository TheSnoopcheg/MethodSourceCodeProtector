using Mono.Cecil;
using Newtonsoft.Json;
using System.Collections.Concurrent;
using System.Reflection;
using System.Reflection.Emit;

namespace Protector.Patcher;

public class DynamicMethodProvider
{
    private readonly ConcurrentDictionary<string, Delegate> _cache = new();
    private List<NativeObject> _nativeObjects = [];
    public Delegate GetDelegate(MethodInfo method, Type?[]? genericParamTypes)
    {
        return _cache.GetOrAdd(method.Name, _ =>
        {
            Type delegateType = GetDelegateType(method, genericParamTypes);
            MethodDefinition? nativeMethod = GetMethodByName(method);
            DynamicMethod dynamicMethod = new DynamicMethod(
                method.Name,
                GetReturnType(method, genericParamTypes),
                GetParametersArray(method, genericParamTypes),
                method.Module,
                true);
            var il = dynamicMethod.GetILGenerator();
            CecilToRefMethodBodyCloner cloner = new CecilToRefMethodBodyCloner(il, nativeMethod, method.DeclaringType?.GetGenericArguments(), genericParamTypes, Assembly.Load(_nativeObjects.FirstOrDefault(o => o.Name == $"{method.Module.Name}.{method.Name}`{method.GetGenericArguments().Length}").Assembly));
            cloner.EmitBody();
            return dynamicMethod.CreateDelegate(delegateType);
        });
    }
    private MethodDefinition GetMethodByName(MethodInfo method)
    {
        string name = $"{method.Module.Name}.{method.Name}`{method.GetGenericArguments().Length}";
        string nativeDll = File.ReadAllText($@"{AppDomain.CurrentDomain.BaseDirectory}\Native.dll");
        _nativeObjects = JsonConvert.DeserializeObject<List<NativeObject>>(nativeDll)!;
        NativeObject nativeObject = _nativeObjects.FirstOrDefault(o => o.Name == name)!;
        var asmBytes = nativeObject.Assembly;
        AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(new MemoryStream(asmBytes));
        ModuleDefinition module = assembly.MainModule;
        TypeDefinition? typeDef = module.GetType($"<NativeMethod>_{method.Module.Name}.{method.Name}`{method.GetGenericArguments().Length}.<>");
        MethodDefinition resMethod = typeDef?.Methods.FirstOrDefault(m => m.Name == method.Name)!;
        return resMethod;
    }
    private static Type? GetReturnType(MethodInfo methodInfo, Type?[]? genericParamTypes)
    {
        
        if (methodInfo.ReturnType.IsGenericParameter)
        {
            return genericParamTypes?[methodInfo.ReturnType.GenericParameterPosition];
        }
        return methodInfo.ReturnType;
    }
    private static Type[] GetParametersArray(MethodInfo methodInfo, Type?[]? genericParamTypes)
    {
        List<Type> types = [];
        if(!methodInfo.IsStatic)
        {
            types.Add(methodInfo.DeclaringType!);
        }
        types.AddRange(methodInfo.GetParameters().Select(p =>
        {
            if (p.ParameterType.IsGenericParameter)
            {
                return genericParamTypes[p.ParameterType.GenericParameterPosition]!;
            }
            return p.ParameterType;
        }));
        return types.ToArray();
    }

    /// <summary>
    /// Gets a standard Action or Func delegate type that matches the signature of the given MethodInfo.
    /// </summary>
    /// <param name="methodInfo">The method whose signature to match.</param>
    /// <returns>A delegate Type, like typeof(Action<int>) or typeof(Func<string, bool>).</returns>
    private static Type GetDelegateType(MethodInfo methodInfo, Type?[]? genereicParamTypes)
    {
        var parameterTypes = GetParametersArray(methodInfo, genereicParamTypes);

        if (methodInfo.ReturnType == typeof(void))
        {
            if (parameterTypes.Length == 0)
            {
                return typeof(Action);
            }

            Type openActionType = Type.GetType($"System.Action`{parameterTypes.Length}");
            if (openActionType == null)
            {
                throw new NotSupportedException($"No Action delegate with {parameterTypes.Length} parameters.");
            }
            return openActionType.MakeGenericType(parameterTypes);
        }
        else
        {
            var allTypes = parameterTypes.Concat(new[] { GetReturnType(methodInfo, genereicParamTypes) }).ToArray();

            Type openFuncType = Type.GetType($"System.Func`{allTypes.Length}");
            if (openFuncType == null)
            {
                throw new NotSupportedException($"No Func delegate with {allTypes.Length} generic arguments.");
            }
            return openFuncType.MakeGenericType(allTypes);
        }
    }
}

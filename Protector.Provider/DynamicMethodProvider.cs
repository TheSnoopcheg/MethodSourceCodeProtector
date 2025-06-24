using Mono.Cecil;
using System.Collections.Concurrent;
using System.Reflection;
using System.Reflection.Emit;

namespace Protector.Provider;

public class DynamicMethodProvider
{
    private readonly ConcurrentDictionary<string, Delegate> _cache = new();
    public Delegate GetDelegate(MethodInfo method, Type?[]? genericParamTypes)
    {
        if(method == null)
        {
            throw new ArgumentNullException(nameof(method), "Method cannot be null.");
        }
        return _cache.GetOrAdd(PatcherHelper.GetShortIdentityNameFromMethodInfo(method), _ =>
        {
            Type delegateType = GetDelegateType(method, genericParamTypes);
            MethodDefinition? nativeMethod = GetMethodByName(method);
            DynamicMethod dynamicMethod = new DynamicMethod(
                PatcherHelper.GetShortIdentityNameFromMethodInfo(method),
                GetReturnType(method, genericParamTypes),
                GetParametersArray(method, genericParamTypes),
                method.Module,
                true);
            var il = dynamicMethod.GetILGenerator();
            CecilToRefMethodBodyCloner cloner = new CecilToRefMethodBodyCloner(
                il, 
                nativeMethod, 
                method.DeclaringType?.GetGenericArguments(), 
                genericParamTypes);
            cloner.EmitBody();
            return dynamicMethod.CreateDelegate(delegateType);
        });
    }
    private MethodDefinition GetMethodByName(MethodInfo method)
    {
        string name = PatcherHelper.GetIdentityNameFromMethodInfo(method);

        byte[] nativeObjectsInfoData = Convert.FromBase64String(resources.METHODIDTABLE);
        var nativeObjectsInfo = BinaryListSerializer<NativeObjectInfo>.Deserialize(nativeObjectsInfoData);
        if(nativeObjectsInfo == null || nativeObjectsInfo.Count == 0)
        {
            throw new Exception("No native objects found in the resource data.");
        }
        NativeObjectInfo nativeObjectInfo = nativeObjectsInfo.FirstOrDefault(o => o.MethodName == name);
        if (nativeObjectInfo.Equals(default(NativeObjectInfo)))
        {
            throw new Exception($"Method {method.Name} not found in native objects info.");
        }
        var asmBytes = resources.ResourceManager.GetObject(nativeObjectInfo.ResourceName) as byte[];
        if (asmBytes == null)
        {
            throw new Exception($"Assembly bytes for method {method.Name} not found in resources.");
        }

        AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(new MemoryStream(asmBytes));
        ModuleDefinition module = assembly.MainModule;
        TypeDefinition? typeDef = module.GetType($"{name}.<>");
        if(typeDef == null)
        {
            throw new Exception($"Type definition for method {method.Name} not found in module {method.Module.Name}.");
        }
        MethodDefinition? resMethod = typeDef?.Methods.FirstOrDefault(m => m.Name == method.Name);
        if (resMethod == null)
        {
            throw new Exception($"Method {method.Name} not found in type {typeDef.Name} in module {method.Module.Name}.");
        }
        return resMethod;
    }
    private Type? GetReturnType(MethodInfo methodInfo, Type?[]? genericParamTypes)
    {
        
        if (methodInfo.ReturnType.IsGenericParameter)
        {
            return genericParamTypes?[methodInfo.ReturnType.GenericParameterPosition];
        }
        return methodInfo.ReturnType;
    }
    private Type[] GetParametersArray(MethodInfo methodInfo, Type?[]? genericParamTypes)
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
                return genericParamTypes![p.ParameterType.GenericParameterPosition]!;
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
    private Type GetDelegateType(MethodInfo methodInfo, Type?[]? genericParamTypes)
    {
        var parameterTypes = GetParametersArray(methodInfo, genericParamTypes);

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
            var allTypes = parameterTypes.Concat(new[] { GetReturnType(methodInfo, genericParamTypes) }).ToArray();

            Type openFuncType = Type.GetType($"System.Func`{allTypes.Length}");
            if (openFuncType == null)
            {
                throw new NotSupportedException($"No Func delegate with {allTypes.Length} generic arguments.");
            }
            return openFuncType.MakeGenericType(allTypes);
        }
    }
}

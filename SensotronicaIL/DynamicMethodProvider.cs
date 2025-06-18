using Mono.Cecil;
using System.Collections.Concurrent;
using System.Reflection;
using System.Reflection.Emit;

namespace SensotronicaIL;

public class DynamicMethodProvider
{
    private readonly ConcurrentDictionary<string, Delegate> _cache = new();
    public Delegate GetDelegate(MethodInfo method, Type? genericReturnType, Type?[]? genericParamTypes)
    {
        return _cache.GetOrAdd(method.Name, _ =>
        {
            Type delegateType = GetDelegateType(method, genericReturnType, genericParamTypes);
            AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(Globals.NEWPATH);
            ModuleDefinition module = assembly.MainModule;
            TypeDefinition typeDef = module.GetType("Test.Test1") 
                ?? throw new InvalidOperationException($"Type {method.DeclaringType.FullName} not found in assembly {assembly.Name.Name}.");
            MethodDefinition? nativeMethod = typeDef.Methods.FirstOrDefault(m => m.Name == method.Name
                && m.IsStatic == method.IsStatic
                && m.Parameters.Count == method.GetParameters().Length);
            if (nativeMethod == null)
            {
                throw new InvalidOperationException($"Method {method.Name} not found in type {typeDef.FullName}.");
            }
            DynamicMethod dynamicMethod = new DynamicMethod(
                method.Name,
                genericReturnType ?? method.ReturnType,
                GetParametersArray(method, genericParamTypes),
                method.Module,
                true);
            var il = dynamicMethod.GetILGenerator();
            CecilToRefMethodBodyCloner cloner = new CecilToRefMethodBodyCloner(il, nativeMethod, method.DeclaringType?.GetGenericArguments(), genericParamTypes);
            cloner.EmitBody();
            return dynamicMethod.CreateDelegate(delegateType);
        });
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
    private static Type GetDelegateType(MethodInfo methodInfo, Type? genericReturnType, Type?[]? genereicParamTypes)
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
            var allTypes = parameterTypes.Concat(new[] { methodInfo.ReturnType.IsGenericParameter ? genericReturnType : methodInfo.ReturnType }).ToArray();

            Type openFuncType = Type.GetType($"System.Func`{allTypes.Length}");
            if (openFuncType == null)
            {
                throw new NotSupportedException($"No Func delegate with {allTypes.Length} generic arguments.");
            }
            return openFuncType.MakeGenericType(allTypes);
        }
    }
}

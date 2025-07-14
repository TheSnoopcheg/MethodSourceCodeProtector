using Mono.Cecil;
using System.Collections.Concurrent;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;

namespace Protector.Provider;

public class DynamicMethodProvider
{

    #region PInvoke

    [DllImport("Native.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr GetByteAssembly([MarshalAs(UnmanagedType.LPStr)] string methodName, out ulong size);
    [DllImport("Native.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern void FreeByteAssembly(IntPtr buffer);

    #endregion

    private readonly ConcurrentDictionary<string, Delegate> _cache = new();
    public Delegate GetDelegate(MethodInfo method, Type?[]? typeGenericTypes, Type?[]? methodGenericTypes)
    {
        if(method == null)
        {
            throw new ArgumentNullException(nameof(method), "Method cannot be null.");
        }
        string methodName = ProviderHelper.GetShortIdentityNameFromMethodInfo(method, typeGenericTypes, methodGenericTypes);
        return _cache.GetOrAdd(methodName, _ =>
        {
            Type delegateType = GetDelegateType(method, typeGenericTypes, methodGenericTypes);
            MethodDefinition? nativeMethod = GetMethodByName(method);
            DynamicMethod dynamicMethod = new DynamicMethod(
                methodName,
                ResolveType(method.ReturnType, typeGenericTypes, methodGenericTypes),
                GetParametersArray(method, typeGenericTypes, methodGenericTypes),
                method.Module,
                true);
            var il = dynamicMethod.GetILGenerator();
            CecilToRefMethodBodyCloner cloner = new CecilToRefMethodBodyCloner(
                il, 
                nativeMethod,
                typeGenericTypes, 
                methodGenericTypes);
            cloner.EmitBody();
            return dynamicMethod.CreateDelegate(delegateType);
        });
    }

    private MethodDefinition GetMethodByName(MethodInfo method)
    {
        string name = ProviderHelper.GetIdentityNameFromMethodInfo(method);

        var asmBytes = GetAssemblyByteArray(name);
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
            throw new Exception($"Method {method.Name} not found in type {typeDef!.Name} in module {method.Module.Name}.");
        }
        return resMethod;
    }

    private Type ResolveType(Type type, Type?[]? typeGenericTypes, Type?[]? methodGenericTypes)
    {
        if (type.IsGenericParameter)
        {
            if (type.DeclaringMethod != null)
            {
                return methodGenericTypes![type.GenericParameterPosition]!;
            }
            else
            {
                return typeGenericTypes![type.GenericParameterPosition]!;
            }
        }
        if (type.IsGenericType)
        {
            var genericArgs = type.GetGenericArguments()
                .Select(t => ResolveType(t, typeGenericTypes, methodGenericTypes)).ToArray();
            return type.GetGenericTypeDefinition().MakeGenericType(genericArgs);
        }
        return type;
    }

    private Type[] GetParametersArray(MethodInfo methodInfo, Type?[]? typeGenericTypes,  Type?[]? methodGenericTypes)
    {
        List<Type> types = [];
        if (!methodInfo.IsStatic)
        {
            if (methodInfo.DeclaringType!.IsGenericTypeDefinition)
            {
                types.Add(methodInfo.DeclaringType!.MakeGenericType(typeGenericTypes));
            }
            else if (methodInfo.DeclaringType!.IsGenericType)
            {
                types.Add(ResolveType(methodInfo.DeclaringType!, typeGenericTypes, methodGenericTypes));
            }
            else
            {
                types.Add(methodInfo.DeclaringType!);
            }
        }
        types.AddRange(methodInfo.GetParameters().Select(p => ResolveType(p.ParameterType, typeGenericTypes, methodGenericTypes)));
        return types.ToArray();
    }

    /// <summary>
    /// Gets a standard Action or Func delegate type that matches the signature of the given MethodInfo.
    /// </summary>
    /// <param name="methodInfo">The method whose signature to match.</param>
    /// <returns>A delegate Type, like typeof(Action<int>) or typeof(Func<string, bool>).</returns>
    private Type GetDelegateType(MethodInfo methodInfo, Type?[]? typeGenericTypes, Type?[]? methodGenericTypes)
    {
        var parameterTypes = GetParametersArray(methodInfo, typeGenericTypes, methodGenericTypes);

        if (methodInfo.ReturnType == typeof(void))
        {
            if (parameterTypes.Length == 0)
            {
                return typeof(Action);
            }

            Type? openActionType = Type.GetType($"System.Action`{parameterTypes.Length}");
            if (openActionType == null)
            {
                throw new NotSupportedException($"No Action delegate with {parameterTypes.Length} parameters.");
            }
            return openActionType.MakeGenericType(parameterTypes);
        }
        else
        {
            var allTypes = parameterTypes.Concat(new[] { ResolveType(methodInfo.ReturnType, typeGenericTypes, methodGenericTypes) }).ToArray();

            Type? openFuncType = Type.GetType($"System.Func`{allTypes.Length}");
            if (openFuncType == null)
            {
                throw new NotSupportedException($"No Func delegate with {allTypes.Length} generic arguments.");
            }
            return openFuncType.MakeGenericType(allTypes);
        }
    }

    public byte[] GetAssemblyByteArray(string methodName)
    {
        IntPtr bufferPtr = IntPtr.Zero;
        try
        {
            bufferPtr = GetByteAssembly(methodName, out ulong size);

            if (bufferPtr == IntPtr.Zero || size == 0)
            {
                return null;
            }

            byte[] managedArray = new byte[size];

            Marshal.Copy(bufferPtr, managedArray, 0, (int)size);

            return managedArray;
        }
        finally
        {
            if (bufferPtr != IntPtr.Zero)
            {
                FreeByteAssembly(bufferPtr);
            }
        }
    }
}

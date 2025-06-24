using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using Newtonsoft.Json;
using System.Reflection;
using System.Text;
using Protector.Provider;

using MONOTypeAttributes = Mono.Cecil.TypeAttributes;
using MONOMethodAttributes = Mono.Cecil.MethodAttributes;

namespace Protector.Patcher;

public class AssemblyPatcher
{
    private List<PatchOperation> _operations = new List<PatchOperation>();
    private List<NativeObject> _nativeObjects = new List<NativeObject>();
    private AssemblyDefinition _assembly;
    public AssemblyPatcher(AssemblyDefinition assembly)
    {
        _assembly = assembly ?? throw new ArgumentNullException(nameof(assembly), "[PATCHER]: Assembly cannot be null.");
    }
    public void PatchAssembly()
    {
        foreach(var op in _operations)
        {
            if(TryPatchType(op.Type, out var field, out bool needPatchConstructor))
            {
                if (needPatchConstructor)
                {
                    PatchConstructor(op.Type, field);
                }
                var asm = CreateAssembly(op.Method);
                PatchMethod(op.Method, field);
                var nativeObject = new NativeObject
                {
                    Name = PatcherHelper.GetIdentityNameFromMethodInfo(op.Method),
                    Assembly = asm
                };
                _nativeObjects.Add(nativeObject);
            }
            else
            {
                throw new InvalidOperationException($"[PATCHER]: Type {op.Type.FullName} already has a provider field.");
            }
        }

        Console.WriteLine($"[PATCHER]: Patching completed for assembly {_assembly.Name.Name}.");
        string nativeDll = JsonConvert.SerializeObject(_nativeObjects, Formatting.Indented);
        File.WriteAllBytes(PatcherHelper.GetNativeDllPath(Path.GetDirectoryName(_assembly.MainModule.FileName)!), Encoding.UTF8.GetBytes(nativeDll));
        string patchPath = PatcherHelper.GetNewDllPath(Path.GetFileNameWithoutExtension(_assembly.MainModule.FileName));
        _assembly.Write(patchPath);
        _assembly.Dispose();
        Console.WriteLine($"[PATCHER]: Assembly {_assembly.Name.Name} patched and written to {patchPath}.");
    }

    public void AddOperation(PatchOperation operation)
    {
        if(operation.Type.Module.Assembly != _assembly)
        {
            throw new InvalidOperationException($"[PATCHER]: Operation type {operation.Type.FullName} does not belong to the assembly being patched.");
        }
        _operations.Add(operation);
    }
    public void RemoveOperation(PatchOperation operation)
    {
        if(operation.Type.Module.Assembly != _assembly)
        {
            throw new InvalidOperationException($"[PATCHER]: Operation type {operation.Type.FullName} does not belong to the assembly being patched.");
        }
        _operations.Remove(operation);
    }   

    private void PatchConstructor(TypeDefinition type, FieldDefinition field)
    {
        var constructor = type.Methods.FirstOrDefault(m => m.Name == ".cctor" && m.IsStatic && m.IsSpecialName);
        if (constructor is not null)
        {
            Console.WriteLine($"[PATCHER]: Type {type.FullName} already has a static constructor.");
            var body = constructor.Body;
            var il = body.GetILProcessor();
            il.InsertBefore(body.Instructions.Last(), il.Create(OpCodes.Newobj, type.Module.ImportReference(ResolveMethod(typeof(DynamicMethodProvider), ".ctor", BindingFlags.Default | BindingFlags.Instance | BindingFlags.Public))));
            il.InsertBefore(body.Instructions.Last(), il.Create(OpCodes.Stsfld, field));
            Console.WriteLine($"[PATCHER]: Static constructor patched for type {type.FullName}.");
        }
        else
        {
            Console.WriteLine($"[PATCHER]: Type {type.FullName} does not have a static constructor, creating one.");
            constructor = new MethodDefinition(".cctor", MONOMethodAttributes.Private | MONOMethodAttributes.Static | MONOMethodAttributes.HideBySig | MONOMethodAttributes.SpecialName | MONOMethodAttributes.RTSpecialName, type.Module.TypeSystem.Void);
            type.Methods.Add(constructor);
            var body = constructor.Body;
            var il = body.GetILProcessor();
            il.Emit(OpCodes.Newobj, type.Module.ImportReference(ResolveMethod(typeof(DynamicMethodProvider), ".ctor", BindingFlags.Default | BindingFlags.Instance | BindingFlags.Public)));
            il.Emit(OpCodes.Stsfld, field);
            il.Emit(OpCodes.Ret);
            Console.WriteLine($"[PATCHER]: Static constructor created and patched for type {type.FullName}.");
        }
    }
    private bool TryPatchType(TypeDefinition type, out FieldDefinition field, out bool needPatchConstructor)
    {
        var providerField = type.Fields.FirstOrDefault(f => f.Name == "_provider");
        if(providerField is not null)
        {
            Console.WriteLine($"[PATCHER]: Type {type.FullName} already has a provider field.");
            field = providerField;
            if(providerField.FieldType.FullName != typeof(DynamicMethodProvider).FullName)
            {
                throw new InvalidOperationException($"[PATCHER]: Type {type.FullName} has a provider field, but it is not of type {typeof(DynamicMethodProvider).FullName}.");
            }
            needPatchConstructor = false;
            return true;
        }
        Console.WriteLine($"[PATCHER]: Adding provider field to type {type.FullName}.");
        providerField = new FieldDefinition("_provider", Mono.Cecil.FieldAttributes.Private | Mono.Cecil.FieldAttributes.Static, type.Module.ImportReference(typeof(DynamicMethodProvider)));
        type.Fields.Add(providerField);
        field = providerField;
        Console.WriteLine($"[PATCHER]: Provider field added: {providerField.FullName}");
        needPatchConstructor = true;
        return true;
    }
    private void PatchMethod(MethodDefinition method, FieldDefinition field)
    {
        Console.WriteLine($"[PATCHER]: Patching method {method.FullName} in type {method.DeclaringType.FullName}.");
        var module = method.Module;
        var body = method.Body;
        body.Instructions.Clear();
        body.Variables.Clear();
        body.ExceptionHandlers.Clear();
        var il = body.GetILProcessor();

        var methodInfoVar = new VariableDefinition(method.Module.ImportReference(typeof(MethodInfo)));
        body.Variables.Add(methodInfoVar);
        il.Emit(OpCodes.Ldtoken, module.ImportReference(method.DeclaringType));
        il.Emit(OpCodes.Call, module.ImportReference(ResolveMethod(typeof(Type), "GetTypeFromHandle", BindingFlags.Default | BindingFlags.Static | BindingFlags.Public, "System.RuntimeTypeHandle")));
        il.Emit(OpCodes.Ldstr, method.Name);
        il.Emit(OpCodes.Ldc_I4, GetBindingFlagsValue(method));
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ldc_I4, method.Parameters.Count);
        il.Emit(OpCodes.Newarr, module.ImportReference(typeof(Type)));
        for (int i = 0; i < method.Parameters.Count; i++)
        {
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4, i);
            if (method.Parameters[i].ParameterType.IsGenericParameter)
            {
                il.Emit(OpCodes.Ldc_I4, ((GenericParameter)method.Parameters[i].ParameterType).Position);
                il.Emit(OpCodes.Call, module.ImportReference(ResolveMethod(typeof(System.Type), "MakeGenericMethodParameter", BindingFlags.Default | BindingFlags.Static | BindingFlags.Public, "System.Int32")));
            }
            else
            {
                il.Emit(OpCodes.Ldtoken, method.Parameters[i].ParameterType);
                il.Emit(OpCodes.Call, module.ImportReference(ResolveMethod(typeof(System.Type), "GetTypeFromHandle", BindingFlags.Default | BindingFlags.Static | BindingFlags.Public, "System.RuntimeTypeHandle")));
            }
            il.Emit(OpCodes.Stelem_Ref);
        }
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Callvirt, module.ImportReference(ResolveMethod(typeof(System.Type), "GetMethod", BindingFlags.Default | BindingFlags.Instance | BindingFlags.Public,
            "System.String", "System.Reflection.BindingFlags", "System.Reflection.Binder", "System.Type[]", "System.Reflection.ParameterModifier[]")));
        il.Emit(OpCodes.Stloc, methodInfoVar);

        var argTypes = new VariableDefinition(module.ImportReference(typeof(System.Type)).MakeArrayType());
        body.Variables.Add(argTypes);
        if (method.HasGenericParameters)
        {
            il.Emit(OpCodes.Ldc_I4, method.GenericParameters.Count);
            il.Emit(OpCodes.Newarr, module.ImportReference(typeof(System.Type)));
            for(int i = 0; i < method.GenericParameters.Count; i++)
            {
                il.Emit(OpCodes.Dup);
                il.Emit(OpCodes.Ldc_I4, i);
                il.Emit(OpCodes.Ldtoken, method.GenericParameters[i]);
                il.Emit(OpCodes.Call, module.ImportReference(ResolveMethod(typeof(System.Type), "GetTypeFromHandle", BindingFlags.Default | BindingFlags.Static | BindingFlags.Public, "System.RuntimeTypeHandle")));
                il.Emit(OpCodes.Stelem_Ref);
            }
        }
        else
        {
            il.Emit(OpCodes.Ldnull);
        }
        il.Emit(OpCodes.Stloc, argTypes);

        var del = new VariableDefinition(module.ImportReference(typeof(Delegate)));
        body.Variables.Add(del);
        il.Emit(OpCodes.Ldsfld, field);
        il.Emit(OpCodes.Ldloc, methodInfoVar);
        il.Emit(OpCodes.Ldloc, argTypes);
        il.Emit(OpCodes.Callvirt, module.ImportReference(ResolveMethod(typeof(DynamicMethodProvider), "GetDelegate", BindingFlags.Default | BindingFlags.Instance | BindingFlags.Public, "System.Reflection.MethodInfo", "System.Type[]")));
        il.Emit(OpCodes.Stloc, del);


        var res = new VariableDefinition(module.TypeSystem.Object);
        body.Variables.Add(res);
        il.Emit(OpCodes.Ldloc, del);
        int paramCount = method.Parameters.Count + (method.HasThis ? 1 : 0);
        if(paramCount > 0)
        {
            il.Emit(OpCodes.Ldc_I4, paramCount);
            il.Emit(OpCodes.Newarr, module.TypeSystem.Object);
            int i = 0;
            if(method.HasThis)
            {
                il.Emit(OpCodes.Dup);
                il.Emit(OpCodes.Ldc_I4, 0);
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Stelem_Ref);
                i++;
            }
            for(;i < paramCount; i++)
            {
                il.Emit(OpCodes.Dup);
                il.Emit(OpCodes.Ldc_I4, i);
                il.Emit(OpCodes.Ldarg, i);
                il.Emit(OpCodes.Box, method.Parameters[i - (method.HasThis ? 1 : 0)].ParameterType);
                il.Emit(OpCodes.Stelem_Ref);
            }
        }
        else
        {
            il.Emit(OpCodes.Ldnull);
        }
        il.Emit(OpCodes.Callvirt, module.ImportReference(ResolveMethod(typeof(Delegate), "DynamicInvoke", BindingFlags.Default | BindingFlags.Instance | BindingFlags.Public, "System.Object[]")));
        il.Emit(OpCodes.Stloc, res);

        if(method.ReturnType != module.TypeSystem.Void)
        {
            il.Emit(OpCodes.Ldloc, res);
            if (method.ReturnType is GenericParameter param)
            {
                il.Emit(OpCodes.Unbox_Any, method.GenericParameters[param.Position]);
            }
            else if(method.ReturnType.IsValueType)
            {
                il.Emit(OpCodes.Unbox_Any, module.ImportReference(method.ReturnType));
            }
            else
            {
                il.Emit(OpCodes.Castclass, module.ImportReference(method.ReturnType));
            }
        }

        il.Emit(OpCodes.Ret);

        Console.WriteLine($"[PATCHER]: Method {method.FullName} patched successfully.");
    }

    private int GetBindingFlagsValue(MethodDefinition method)
    {
        BindingFlags flags = 0;

        if (method.IsPublic)
            flags |= BindingFlags.Public;
        else
            flags |= BindingFlags.NonPublic;

        if (method.IsStatic)
            flags |= BindingFlags.Static;
        else
            flags |= BindingFlags.Instance;

        return (int)flags;
    }
    private byte[] CreateAssembly(MethodDefinition method)
    {
        string name = PatcherHelper.GetIdentityNameFromMethodInfo(method);
        AssemblyNameDefinition asmName = new AssemblyNameDefinition(name, new Version(1, 0, 0, 0));
        AssemblyDefinition asm = AssemblyDefinition.CreateAssembly(asmName, name, ModuleKind.Dll);
        TypeDefinition _type = new TypeDefinition(name, "<>", MONOTypeAttributes.Public | MONOTypeAttributes.Class, asm.MainModule.TypeSystem.Object);
        asm.MainModule.Types.Add(_type);
        MethodDefinition _method = new MethodDefinition(method.Name, method.Attributes, asm.MainModule.TypeSystem.Void);
        foreach (var p in method.GenericParameters)
        {
            var newGenericParam = new GenericParameter(p.Name, _method);
            _method.GenericParameters.Add(newGenericParam);
        }
        _type.Methods.Add(_method);
        if (method.ReturnType.IsGenericParameter)
        {
            var gp = (GenericParameter)method.ReturnType;
            _method.ReturnType = _method.GenericParameters[gp.Position];
        }
        else
        {
            _method.ReturnType = asm.MainModule.ImportReference(method.ReturnType, _method);
        }
        foreach (var p in method.Parameters)
        {
            TypeReference newParameterType;
            if (p.ParameterType.IsGenericParameter)
            {
                var genericParam = (GenericParameter)p.ParameterType;
                newParameterType = _method.GenericParameters[genericParam.Position];
            }
            else
            {
                newParameterType = asm.MainModule.ImportReference(p.ParameterType, _method);
            }

            var newParam = new ParameterDefinition(
                p.Name,
                p.Attributes,
                newParameterType
            );

            _method.Parameters.Add(newParam);
        }
        CecilMethodBodyCloner cl = new CecilMethodBodyCloner(method, _method);
        cl.Clone();
        MethodDefinition constructor = new MethodDefinition(".ctor", MONOMethodAttributes.Public | MONOMethodAttributes.HideBySig |
            MONOMethodAttributes.RTSpecialName | MONOMethodAttributes.SpecialName, asm.MainModule.TypeSystem.Void);
        _type.Methods.Add(constructor);
        var il2 = constructor.Body.GetILProcessor();
        il2.Emit(Mono.Cecil.Cil.OpCodes.Ret);
        using(var stream = new MemoryStream())
        {
            asm.Write(stream);
            asm.Dispose();
            return stream.ToArray();
        }
    }
    private MethodBase ResolveMethod(Type declaringType, string methodName, BindingFlags bindingFlags, params string[] paramTypes)
    {
        if (methodName == ".ctor")
        {
            var resolvedCtor = declaringType.GetConstructor(
                bindingFlags,
                null,
                paramTypes.Select(Type.GetType).ToArray(),
                null);

            if (resolvedCtor == null)
            {
                throw new InvalidOperationException($"[PATCHER]: Failed to resolve ctor [{declaringType}({string.Join(',', paramTypes)})");
            }

            return resolvedCtor;
        }

        var resolvedMethod = declaringType.GetMethod(methodName,
            bindingFlags,
            null,
            paramTypes.Select(Type.GetType).ToArray(),
            null);

        if (resolvedMethod == null)
        {
            throw new InvalidOperationException($"[PATCHER]: Failed to resolve method {declaringType}.{methodName}({string.Join(',', paramTypes)})");
        }

        return resolvedMethod;
    }
}

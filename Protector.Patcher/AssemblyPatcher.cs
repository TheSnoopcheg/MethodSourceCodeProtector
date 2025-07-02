using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using System.Reflection;
using Protector.Provider;
using Protector.Patcher.Extensions;

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

            if (op.Method.IsAsyncMethod())
            {
                var asyncAttr = op.Method.CustomAttributes.FirstOrDefault(a => a.AttributeType.FullName == "System.Runtime.CompilerServices.AsyncStateMachineAttribute");
                var stateMachineType = asyncAttr?.ConstructorArguments.FirstOrDefault().Value as TypeReference;
                if (stateMachineType == null) continue;

                op.Type = stateMachineType.Resolve();
                op.Method = op.Type.Methods.FirstOrDefault(m => m.Name == "MoveNext")!;
            }

            if (TryPatchType(op.Type, out var field, out bool needPatchConstructor))
            {
                var fieldRef = GetFieldReference(op.Type, field);
                if (needPatchConstructor)
                {
                    PatchConstructor(op.Type, fieldRef);
                }
                var asm = CreateAssembly(op.Method, op.Type);
                PatchMethod(op.Method, fieldRef, op.Type);
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

        // maybe we should choose another way to enter path for native dll
        // for now Native.dll should be in the same folder as the executable file
        using (var modifier = new NativeResourceUpdater($"{Path.GetDirectoryName(_assembly.MainModule.FileName)}\\Native.dll"))
        {
            foreach(var nativeObject in _nativeObjects)
            {
                modifier.AddResource(nativeObject.Name, nativeObject.Assembly);
            }
        }
        string patchPath = PatcherHelper.GetNewDllPath(_assembly.MainModule.FileName);
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

    private void PatchConstructor(TypeDefinition type, FieldReference field)
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
    private void EmitGenericType(ILProcessor il, GenericInstanceType type, MethodDefinition method, VariableDefinition typeVar)
    {
        var module = method.Module;
        il.Emit(OpCodes.Ldtoken, module.ImportReference(type.ElementType));
        il.Emit(OpCodes.Call, module.ImportReference(ResolveMethod(typeof(Type), "GetTypeFromHandle", BindingFlags.Default | BindingFlags.Static | BindingFlags.Public, "System.RuntimeTypeHandle")));

        il.Emit(OpCodes.Ldc_I4, type.GenericArguments.Count);
        il.Emit(OpCodes.Newarr, module.ImportReference(typeof(Type)));
        for(int i = 0; i < type.GenericArguments.Count; i++)
        {
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4, i);
            if (type.GenericArguments[i] is GenericInstanceType git)
            {
                EmitGenericType(il, git, method, typeVar);
            }
            else if(type.GenericArguments[i] is GenericParameter param)
            {
                if (param.Owner == method)
                {
                    // If the current parameter is generic and its owner is method, call Type.MakeGenericMethodParameter(index) where index is the index of the generic argument in the method declaration
                    il.Emit(OpCodes.Ldc_I4, param.Position);
                    il.Emit(OpCodes.Call, module.ImportReference(ResolveMethod(typeof(Type), "MakeGenericMethodParameter", BindingFlags.Default | BindingFlags.Static | BindingFlags.Public, "System.Int32")));
                }
                else
                {
                    // If the current parameter is generic and its owner is [type], call GetGenericArguments on [type] and take the parameter by index (in the type declaration)
                    il.Emit(OpCodes.Ldloc, typeVar);
                    il.Emit(OpCodes.Callvirt, module.ImportReference(ResolveMethod(typeof(Type), "GetGenericArguments", BindingFlags.Default | BindingFlags.Instance | BindingFlags.Public)));
                    il.Emit(OpCodes.Ldc_I4, param.Position);
                    il.Emit(OpCodes.Ldelem_Ref);
                }
            }
            else
            {
                il.Emit(OpCodes.Ldtoken, module.ImportReference(type.GenericArguments[i]));
                il.Emit(OpCodes.Call, module.ImportReference(ResolveMethod(typeof(Type), "GetTypeFromHandle", BindingFlags.Default | BindingFlags.Static | BindingFlags.Public, "System.RuntimeTypeHandle")));
            }
            il.Emit(OpCodes.Stelem_Ref);
        }

        il.Emit(OpCodes.Callvirt, module.ImportReference(ResolveMethod(typeof(Type), "MakeGenericType", BindingFlags.Default | BindingFlags.Instance | BindingFlags.Public, "System.Type[]")));
    }
    private void PatchMethod(MethodDefinition method, FieldReference field, TypeDefinition type)
    {
        Console.WriteLine($"[PATCHER]: Patching method {method.FullName} in type {method.DeclaringType.FullName}.");
        var module = method.Module;
        var body = method.Body;
        body.Instructions.Clear();
        body.Variables.Clear();
        body.ExceptionHandlers.Clear();
        var il = body.GetILProcessor();

        // Create a call to get the type
        var typeVar = new VariableDefinition(module.ImportReference(typeof(Type)));
        body.Variables.Add(typeVar);
        il.Emit(OpCodes.Ldtoken, module.ImportReference(method.DeclaringType));
        il.Emit(OpCodes.Call, module.ImportReference(ResolveMethod(typeof(Type), "GetTypeFromHandle", BindingFlags.Default | BindingFlags.Static | BindingFlags.Public, "System.RuntimeTypeHandle")));
        il.Emit(OpCodes.Stloc, typeVar);

        // Create a call to get the required method
        var methodInfoVar = new VariableDefinition(method.Module.ImportReference(typeof(MethodInfo)));
        body.Variables.Add(methodInfoVar);
        il.Emit(OpCodes.Ldloc, typeVar); // Type var
        il.Emit(OpCodes.Ldstr, method.Name); // Method name
        il.Emit(OpCodes.Ldc_I4, GetBindingFlagsValue(method)); // BindingFlags
        il.Emit(OpCodes.Ldnull); // Binder
        il.Emit(OpCodes.Ldc_I4, method.Parameters.Count); // Load the number of method parameters
        il.Emit(OpCodes.Newarr, module.ImportReference(typeof(Type))); // Create a new array of type [Type] to store the types of all parameters
        for (int i = 0; i < method.Parameters.Count; i++)
        {
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4, i); // Load current parameter index
            if (method.Parameters[i].ParameterType.IsGenericParameter)
            {
                if (((GenericParameter)method.Parameters[i].ParameterType).Owner == method)
                {
                    // If the current parameter is generic and its owner is method, call Type.MakeGenericMethodParameter(index) where index is the index of the generic argument in the method declaration
                    il.Emit(OpCodes.Ldc_I4, ((GenericParameter)method.Parameters[i].ParameterType).Position);
                    il.Emit(OpCodes.Call, module.ImportReference(ResolveMethod(typeof(Type), "MakeGenericMethodParameter", BindingFlags.Default | BindingFlags.Static | BindingFlags.Public, "System.Int32")));
                }
                else
                {
                    // If the current parameter is generic and its owner is [type], call GetGenericArguments on [type] and take the parameter by index (in the type declaration)
                    il.Emit(OpCodes.Ldloc, typeVar);
                    il.Emit(OpCodes.Callvirt, module.ImportReference(ResolveMethod(typeof(Type), "GetGenericArguments", BindingFlags.Default | BindingFlags.Instance | BindingFlags.Public)));
                    il.Emit(OpCodes.Ldc_I4, ((GenericParameter)method.Parameters[i].ParameterType).Position);
                    il.Emit(OpCodes.Ldelem_Ref);
                }
            }
            else if (method.Parameters[i].ParameterType is GenericInstanceType git)
            {
                EmitGenericType(il, git, method, typeVar);
            }
            else
            {
                // Call typeof() for current parameter type
                il.Emit(OpCodes.Ldtoken, method.Parameters[i].ParameterType);
                il.Emit(OpCodes.Call, module.ImportReference(ResolveMethod(typeof(System.Type), "GetTypeFromHandle", BindingFlags.Default | BindingFlags.Static | BindingFlags.Public, "System.RuntimeTypeHandle")));
            }
            il.Emit(OpCodes.Stelem_Ref);
        }
        il.Emit(OpCodes.Ldnull); // ParameterModifier array
        il.Emit(OpCodes.Callvirt, module.ImportReference(ResolveMethod(typeof(System.Type), "GetMethod", BindingFlags.Default | BindingFlags.Instance | BindingFlags.Public,
            "System.String", "System.Reflection.BindingFlags", "System.Reflection.Binder", "System.Type[]", "System.Reflection.ParameterModifier[]")));
        il.Emit(OpCodes.Stloc, methodInfoVar);


        // Create an array to store types of type generic arguments 
        var typeGenericTypes = new VariableDefinition(module.ImportReference(typeof(System.Type)).MakeArrayType());
        body.Variables.Add(typeGenericTypes);
        if(type.HasGenericParameters)
        {
            il.Emit(OpCodes.Ldc_I4, type.GenericParameters.Count);
            il.Emit(OpCodes.Newarr, module.ImportReference(typeof(System.Type)));
            for(int i = 0; i < type.GenericParameters.Count; i++)
            {
                il.Emit(OpCodes.Dup);
                il.Emit(OpCodes.Ldc_I4, i);
                il.Emit(OpCodes.Ldtoken, type.GenericParameters[i]);
                il.Emit(OpCodes.Call, module.ImportReference(ResolveMethod(typeof(System.Type), "GetTypeFromHandle", BindingFlags.Default | BindingFlags.Static | BindingFlags.Public, "System.RuntimeTypeHandle")));
                il.Emit(OpCodes.Stelem_Ref);
            }
        }
        else
        {
            il.Emit(OpCodes.Ldnull);
        }
        il.Emit(OpCodes.Stloc, typeGenericTypes);


        // Create an array to store types of method generic arguments
        var methodGenericTypes = new VariableDefinition(module.ImportReference(typeof(System.Type)).MakeArrayType());
        body.Variables.Add(methodGenericTypes);
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
        il.Emit(OpCodes.Stloc, methodGenericTypes);


        // Create a call to get method Delegate
        var del = new VariableDefinition(module.ImportReference(typeof(Delegate)));
        body.Variables.Add(del);
        il.Emit(OpCodes.Ldsfld, field);
        il.Emit(OpCodes.Ldloc, methodInfoVar);
        il.Emit(OpCodes.Ldloc, typeGenericTypes);
        il.Emit(OpCodes.Ldloc, methodGenericTypes);
        il.Emit(OpCodes.Callvirt, module.ImportReference(ResolveMethod(typeof(DynamicMethodProvider), "GetDelegate", BindingFlags.Default | BindingFlags.Instance | BindingFlags.Public, "System.Reflection.MethodInfo", "System.Type[]", "System.Type[]")));
        il.Emit(OpCodes.Stloc, del);

        // Create a delegate call and save the result
        var res = new VariableDefinition(module.TypeSystem.Object);
        body.Variables.Add(res);
        il.Emit(OpCodes.Ldloc, del);
        int paramCount = method.Parameters.Count + (method.HasThis ? 1 : 0); // Determine the number of parameters considering whether the method is static or not
        if (paramCount > 0)
        {
            il.Emit(OpCodes.Ldc_I4, paramCount);
            il.Emit(OpCodes.Newarr, module.TypeSystem.Object);
            int i = 0;
            if(method.HasThis)
            {
                // Load [this] parameter as first
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


        // Return the result
        if(method.ReturnType != module.TypeSystem.Void)
        {
            il.Emit(OpCodes.Ldloc, res);
            if (method.ReturnType is GenericParameter param)
            {
                // When the return type is generic, we need to unbox it anyway
                if(param.Owner == method)
                {
                    il.Emit(OpCodes.Unbox_Any, method.GenericParameters[param.Position]);
                }
                else
                {
                    il.Emit(OpCodes.Unbox_Any, type.GenericParameters[param.Position]);
                }
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
    private byte[] CreateAssembly(MethodDefinition method, TypeDefinition type)
    {
        string name = PatcherHelper.GetIdentityNameFromMethodInfo(method);
        AssemblyNameDefinition asmName = new AssemblyNameDefinition(name, new Version(1, 0, 0, 0));
        AssemblyDefinition asm = AssemblyDefinition.CreateAssembly(asmName, name, ModuleKind.Dll);
        TypeDefinition _type = new TypeDefinition(name, "<>", MONOTypeAttributes.Public | MONOTypeAttributes.Class, asm.MainModule.TypeSystem.Object);

        foreach(var p in type.GenericParameters)
        {
            var newGenericParam = new GenericParameter(p.Name, _type);
            _type.GenericParameters.Add(newGenericParam);
        }
        asm.MainModule.Types.Add(_type);
        
        MethodDefinition _method = new MethodDefinition(method.Name, method.Attributes, asm.MainModule.TypeSystem.Void);
        foreach (var p in method.GenericParameters)
        {
            var newGenericParam = new GenericParameter(p.Name, _method);
            _method.GenericParameters.Add(newGenericParam);
        }
        _type.Methods.Add(_method);
        
        _method.ReturnType = PatcherHelper.ResolveTypeReference(method.ReturnType, asm.MainModule, method, _method);

        foreach (var p in method.Parameters)
        {
            TypeReference newParameterType = PatcherHelper.ResolveTypeReference(p.ParameterType, asm.MainModule, method, _method);

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

    private FieldReference GetFieldReference(TypeDefinition type, FieldDefinition field)
    {
        FieldReference? fieldRef = null;
        if(type.ContainsGenericParameter || type.HasGenericParameters)
        {
            var genericType = type.MakeGenericInstanceType(type.GenericParameters.ToArray());
            fieldRef = new FieldReference(field.Name, field.FieldType, genericType);
        }
        else
        {
            fieldRef = (FieldReference)field;
        }
        fieldRef = type.Module.ImportReference(fieldRef);
        return fieldRef;
    }
}

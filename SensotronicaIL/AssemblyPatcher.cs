using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Reflection;

using MONOTypeAttributes = Mono.Cecil.TypeAttributes;
using MONOMethodAttributes = Mono.Cecil.MethodAttributes;
using Mono.Cecil.Rocks;
using Newtonsoft.Json;
using System.Text;

namespace SensotronicaIL;

public class AssemblyPatcher
{
    private List<PatchOperation> _operations = new List<PatchOperation>();
    private List<NativeObject> _nativeObjects = new List<NativeObject>();
    private AssemblyDefinition _assembly;
    public AssemblyPatcher(AssemblyDefinition assembly)
    {
        _assembly = assembly ?? throw new ArgumentNullException(nameof(assembly), "Assembly cannot be null.");
    }
    public void PatchAssembly()
    {
        foreach(var op in _operations)
        {
            if(TryPatchType(op.Type, out var field))
            {
                PatchConstructor(op.Type, field);
                PatchMethod(op.Method, field);
                var asm = CreateAssembly(op.Method);
                var nativeObject = new NativeObject
                {
                    Name = $"{op.Method.Module.Name}.{op.Method.Name}`{op.Method.GenericParameters.Count}",
                    Assembly = asm
                };
                _nativeObjects.Add(nativeObject);
            }
            else
            {
                throw new InvalidOperationException($"Type {op.Type.FullName} already has a provider field.");
            }
        }
        string nativeDll = JsonConvert.SerializeObject(_nativeObjects, Formatting.Indented);
        File.WriteAllBytes(Globals.NATIVEDLL, Encoding.UTF8.GetBytes(nativeDll));
        _assembly.Write(Globals.NEWDLLPATH(_assembly.Name.Name));
        _assembly.Dispose();
    }

    public void AddOperation(PatchOperation operation)
    {
        if(operation.Type.Module.Assembly != _assembly)
        {
            throw new InvalidOperationException($"Operation type {operation.Type.FullName} does not belong to the assembly being patched.");
        }
        _operations.Add(operation);
    }

    private void PatchConstructor(TypeDefinition type, FieldDefinition field)
    {
        var constructor = type.Methods.FirstOrDefault(m => m.Name == ".cctor" && m.IsStatic && m.IsSpecialName);
        if (constructor is not null)
        {
            var body = constructor.Body;
            var il = body.GetILProcessor();
            il.InsertBefore(body.Instructions.Last(), il.Create(OpCodes.Newobj, type.Module.ImportReference(ResolveMethod(typeof(DynamicMethodProvider), ".ctor", BindingFlags.Default | BindingFlags.Instance | BindingFlags.Public))));
            il.Emit(OpCodes.Stsfld, field);
        }
        else
        {
            constructor = new MethodDefinition(".cctor", MONOMethodAttributes.Private | MONOMethodAttributes.Static | MONOMethodAttributes.HideBySig | MONOMethodAttributes.SpecialName | MONOMethodAttributes.RTSpecialName, type.Module.TypeSystem.Void);
            type.Methods.Add(constructor);
            var body = constructor.Body;
            var il = body.GetILProcessor();
            il.Emit(OpCodes.Newobj, type.Module.ImportReference(ResolveMethod(typeof(DynamicMethodProvider), ".ctor", BindingFlags.Default | BindingFlags.Instance | BindingFlags.Public)));
            il.Emit(OpCodes.Stsfld, field);
            il.Emit(OpCodes.Ret);
        }
    }
    private bool TryPatchType(TypeDefinition type, out FieldDefinition field)
    {
        var providerField = type.Fields.FirstOrDefault(f => f.Name == "_provider");
        if(providerField is not null)
        {
            field = providerField;
            return false;
        }
        providerField = new FieldDefinition("_provider", Mono.Cecil.FieldAttributes.Private | Mono.Cecil.FieldAttributes.Static, type.Module.ImportReference(typeof(DynamicMethodProvider)));
        type.Fields.Add(providerField);
        field = providerField;
        return true;
    }
    private void PatchMethod(MethodDefinition method, FieldDefinition field)
    {
        var module = method.Module;
        var body = method.Body;
        body.Instructions.Clear();
        body.Variables.Clear();
        body.ExceptionHandlers.Clear();
        var il = body.GetILProcessor();

        var methodInfoVar = new VariableDefinition(method.Module.ImportReference(typeof(MethodInfo)));
        body.Variables.Add(methodInfoVar);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, module.ImportReference(ResolveMethod(typeof(object), "GetType", BindingFlags.Default | BindingFlags.Instance | BindingFlags.Public)));
        il.Emit(OpCodes.Ldstr, method.Name);
        il.Emit(OpCodes.Callvirt, module.ImportReference(ResolveMethod(typeof(System.Type), "GetMethod", BindingFlags.Default | BindingFlags.Instance | BindingFlags.Public, "System.String")));
        il.Emit(OpCodes.Stloc, methodInfoVar);

        var argTypes = new VariableDefinition(module.ImportReference(typeof(System.Type)).MakeArrayType());
        body.Variables.Add(argTypes);
        il.Emit(OpCodes.Ldc_I4, 1);
        il.Emit(OpCodes.Newarr, module.ImportReference(typeof(System.Type)));
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4, 0);
        il.Emit(OpCodes.Ldtoken, method.GenericParameters[0]);
        il.Emit(OpCodes.Call, module.ImportReference(ResolveMethod(typeof(System.Type), "GetTypeFromHandle", BindingFlags.Default | BindingFlags.Static | BindingFlags.Public, "System.RuntimeTypeHandle")));
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Stloc, argTypes);

        var del = new VariableDefinition(module.ImportReference(typeof(Delegate)));
        body.Variables.Add(del);
        il.Emit(OpCodes.Ldsfld, field);
        il.Emit(OpCodes.Ldloc, methodInfoVar);
        il.Emit(OpCodes.Ldloc, argTypes);
        il.Emit(OpCodes.Callvirt, module.ImportReference(ResolveMethod(typeof(DynamicMethodProvider), "GetDelegate", BindingFlags.Default | BindingFlags.Instance | BindingFlags.Public, "System.Reflection.MethodInfo", "System.Type[]")));
        il.Emit(OpCodes.Stloc, del);

        il.Emit(OpCodes.Ldloc, del);
        il.Emit(OpCodes.Ldc_I4, 2);
        il.Emit(OpCodes.Newarr, module.TypeSystem.Object);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4, 0);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4, 1);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Box, method.GenericParameters[0]);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Callvirt, module.ImportReference(ResolveMethod(typeof(Delegate), "DynamicInvoke", BindingFlags.Default | BindingFlags.Instance | BindingFlags.Public, "System.Object[]")));
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ret);
    }
    private byte[] CreateAssembly(MethodDefinition method)
    {
        string name = $"<NativeMethod>_{method.Module.Name}.{method.Name}`{method.GenericParameters.Count}";
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
                throw new InvalidOperationException($"Failed to resolve ctor [{declaringType}({string.Join(',', paramTypes)})");
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
            throw new InvalidOperationException($"Failed to resolve method {declaringType}.{methodName}({string.Join(',', paramTypes)})");
        }

        return resolvedMethod;
    }
}

using Mono.Cecil;

namespace SensotronicaIL;
public static class MonoAssemblyCreator
{
    public static void CreateAssembly(MethodDefinition method)
    {
        AssemblyNameDefinition asmName = new AssemblyNameDefinition("Test", new Version(1, 0, 0, 0));
        AssemblyDefinition asm = AssemblyDefinition.CreateAssembly(asmName, "Test", ModuleKind.Dll);
        TypeDefinition _type = new TypeDefinition("Test", "Test1", TypeAttributes.Public | TypeAttributes.Class, asm.MainModule.TypeSystem.Object);
        asm.MainModule.Types.Add(_type);
        MethodDefinition _method = new MethodDefinition("MethodB", method.Attributes, asm.MainModule.TypeSystem.Void);
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
        MethodDefinition constructor = new MethodDefinition(".ctor", MethodAttributes.Public | MethodAttributes.HideBySig |
            MethodAttributes.RTSpecialName | MethodAttributes.SpecialName, asm.MainModule.TypeSystem.Void);
        _type.Methods.Add(constructor);
        var il2 = constructor.Body.GetILProcessor();
        il2.Emit(Mono.Cecil.Cil.OpCodes.Ret);
        asm.Write(Globals.NEWPATH);
    }
}

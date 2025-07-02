using Mono.Cecil;
using System.Text;

namespace Protector.Patcher;

public static class PatcherHelper
{
    public static string GetIdentityNameFromMethodInfo(MethodDefinition method)
    {
        var sb = new StringBuilder(256);
        sb.Append("<NativeMethod>_");
        sb.Append(method.Module.Name);
        sb.Append('.');
        if (method.DeclaringType != null)
        {
            if(string.IsNullOrEmpty(method.DeclaringType.Namespace))
            {
                if(method.DeclaringType.DeclaringType != null)
                {
                    sb.Append(method.DeclaringType.DeclaringType.Namespace);
                }
            }
            else
            {
                sb.Append(method.DeclaringType.Namespace);
            }
            sb.Append('.');
            sb.Append(method.DeclaringType.Name);
        }
        else
        {
            sb.Append("GlobalMethods");
        }
        sb.Append('.');
        sb.Append(method.Name);
        sb.Append('`');
        sb.Append(method.GenericParameters.Count);
        sb.Append('(');
        sb.Append(string.Join(',', method.Parameters.Select(p => p.ParameterType.ToString().Replace('[', '<').Replace(']', '>'))));
        sb.Append(')');

        return sb.ToString();
    }
    public static string GetNewDllPath(string name)
    {
        return Path.ChangeExtension(name, ".Patched.dll");
    }

    public static TypeReference ResolveTypeReference(TypeReference typeRef, ModuleDefinition module, MethodDefinition sourceMethod, MethodDefinition targetMethod)
    {
        TypeReference newTypeRef;
        if (typeRef is GenericParameter genericParam)
        {
            if (genericParam.Owner == sourceMethod)
            {
                newTypeRef = targetMethod.GenericParameters[genericParam.Position];
            }
            else
            {
                newTypeRef = targetMethod.DeclaringType.GenericParameters[genericParam.Position];
            }
        }
        else if (typeRef is GenericInstanceType git)
        {
            var genericType = module.ImportReference(git.ElementType, targetMethod);
            var importedReturnType = new GenericInstanceType(genericType);

            foreach (var arg in git.GenericArguments)
            {
                TypeReference importedArg = ResolveTypeReference(arg, module, sourceMethod, targetMethod);
                importedReturnType.GenericArguments.Add(importedArg);
            }
            newTypeRef = importedReturnType;
        }
        else
        {
            newTypeRef = module.ImportReference(typeRef, targetMethod)
                ?? module.ImportReference(typeRef);
        }
        return newTypeRef;
    }
}
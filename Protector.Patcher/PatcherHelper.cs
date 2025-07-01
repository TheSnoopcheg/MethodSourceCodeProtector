using Mono.Cecil;
using System.Reflection;
using System.Text;

namespace Protector.Patcher;

public static class PatcherHelper
{
    public static string GetIdentityNameFromMethodInfo(MethodInfo method)
    {
        var sb = new StringBuilder(256);
        sb.Append("<NativeMethod>_");
        sb.Append(method.Module.Name);
        sb.Append('.');
        if (method.DeclaringType != null)
        {
            sb.Append(method.DeclaringType.Namespace);
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
        sb.Append(method.GetGenericArguments().Length);
        sb.Append('(');
        sb.Append(string.Join(',', method.GetParameters().Select(p => p.ParameterType.ToString().Replace('[', '<').Replace(']', '>'))));
        sb.Append(')');

        return sb.ToString();
    }
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
    public static string GetShortIdentityNameFromMethodInfo(MethodInfo method, Type?[]? typeGenericTypes, Type?[]? methodGenericTypes)
    {
        var sb = new StringBuilder(128);
        sb.Append("<NativeMethod>_");
        if (method.DeclaringType != null)
        {
            sb.Append(method.DeclaringType.Name);
            if (typeGenericTypes != null)
            {
                sb.Append('[');
                bool first = true;
                for (int i = 0; i < typeGenericTypes.Length; i++)
                {
                    if (!first)
                        sb.Append(',');
                    first = false;
                    sb.Append(typeGenericTypes[i]?.Name);
                }
                sb.Append(']');
            }
            sb.Append('.');
        }
        sb.Append(method.Name);
        sb.Append('`');
        sb.Append(method.GetGenericArguments().Length);
        if (methodGenericTypes != null)
        {
            sb.Append('[');
            bool first = true;
            for (int i = 0; i < methodGenericTypes.Length; i++)
            {
                if (!first)
                    sb.Append(',');
                first = false;
                sb.Append(methodGenericTypes[i]?.Name);
            }
            sb.Append(']');
        }
        sb.Append('(');
        sb.Append(string.Join(',', method.GetParameters().Select(p => p.ParameterType)));
        sb.Append(')');

        return sb.ToString();
    }
    public static string GetNewDllPath(string name)
    {
        return Path.ChangeExtension(name, ".Patched.dll");
    }
}
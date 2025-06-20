using Mono.Cecil;
using System.Reflection;
using System.Text;

namespace Protector.Patcher;

public static class PatcherHelper
{
    public static string GetIdentityNameFromMethodInfo(MethodInfo method)
    {
        var sb = new StringBuilder();
        sb.Append("<NativeMethod>_");
        sb.Append(method.Module.Name);
        sb.Append('.');
        if(method.DeclaringType != null)
        {
            sb.Append(method.DeclaringType.FullName);
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
        sb.Append(string.Join(',', method.GetParameters().Select(p => p.ParameterType)));
        sb.Append(')');

        return sb.ToString();
    }
    public static string GetIdentityNameFromMethodInfo(MethodDefinition method)
    {
        var sb = new StringBuilder();
        sb.Append("<NativeMethod>_");
        sb.Append(method.Module.Name);
        sb.Append('.');
        if(method.DeclaringType != null)
        {
            sb.Append(method.DeclaringType.FullName);
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
        sb.Append(string.Join(',', method.Parameters.Select(p => p.ParameterType)));
        sb.Append(')');

        return sb.ToString();
    }
    public static string GetShortIdentityNameFromMethodInfo(MethodInfo method)
    {
        var sb = new StringBuilder();
        sb.Append("<NativeMethod>_"); 
        sb.Append(method.Name);
        sb.Append('`');
        sb.Append(method.GetGenericArguments().Length);
        sb.Append('(');
        sb.Append(string.Join(',', method.GetParameters().Select(p => p.ParameterType)));
        sb.Append(')');

        return sb.ToString();
    }
}

using Mono.Cecil;

namespace Protector.Patcher.Extensions;

public static class CecilExtensions
{
    private const string CompilerGeneratedAttributeFullName = "System.Runtime.CompilerServices.CompilerGeneratedAttribute";

    public static bool IsCompilerGenerated(this IMemberDefinition member)
    {
        if (member == null)
            return false;

        return member.HasCustomAttributes &&
               member.CustomAttributes.Any(a => a.AttributeType.FullName == CompilerGeneratedAttributeFullName);
    }
}

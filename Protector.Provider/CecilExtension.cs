using Mono.Cecil;

namespace Protector.Provider.Extensions;

public static class CecilExtensions
{
    private const string CompilerGeneratedAttributeFullName = "System.Runtime.CompilerServices.CompilerGeneratedAttribute";
    private const string AsyncStateMachineAttributeFullName = "System.Runtime.CompilerServices.AsyncStateMachineAttribute";
    private const string AsyncStateMachineInterfaceName = "System.Runtime.CompilerServices.IAsyncStateMachine";

    public static bool IsCompilerGenerated(this IMemberDefinition member)
    {
        if (member == null)
            return false;

        return member.HasCustomAttributes &&
               member.CustomAttributes.Any(a => a.AttributeType.FullName == CompilerGeneratedAttributeFullName);
    }

    public static bool IsAsyncMethod(this IMemberDefinition member)
    {
        if (member == null)
            return false;
        
        return member.HasCustomAttributes &&
               member.CustomAttributes.Any(a => a.AttributeType.FullName == AsyncStateMachineAttributeFullName);
    }
    public static bool IsCompilerGenerated(this TypeReference typeRef)
    {
        if (typeRef == null)
        {
            return false;
        }

        var typeDef = typeRef.Resolve();

        return IsCompilerGenerated(typeDef as IMemberDefinition);
    }
    public static bool IsAsyncStateMachineType(this TypeReference typeRef)
    {
        if (typeRef == null)
        {
            return false;
        }
        var typeDef = typeRef.Resolve();
        return typeDef.HasInterfaces &&
                typeDef.Interfaces.Any(i => i.InterfaceType.FullName == AsyncStateMachineInterfaceName);
    }
}

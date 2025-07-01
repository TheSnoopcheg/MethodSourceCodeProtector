using Mono.Cecil;

namespace Protector.Patcher;

public record class PatchOperation
{
    public TypeDefinition Type { get; set; }
    public MethodDefinition Method { get; set; }
}

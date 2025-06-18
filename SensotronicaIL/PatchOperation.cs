using Mono.Cecil;

namespace SensotronicaIL;

public class PatchOperation
{
    public TypeDefinition Type { get; set; }
    public MethodDefinition Method { get; set; }
}

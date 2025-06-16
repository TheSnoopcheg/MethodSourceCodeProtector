using Mono.Cecil;

/// <summary>
/// Holds the shared state for a recursive cloning operation, mapping old members to new ones.
/// </summary>
public class CloningContext
{
    public ModuleDefinition TargetModule { get; }
    public Dictionary<TypeReference, TypeReference> TypeMap { get; } = new Dictionary<TypeReference, TypeReference>();
    public Dictionary<FieldReference, FieldReference> FieldMap { get; } = new Dictionary<FieldReference, FieldReference>();
    public Dictionary<MethodReference, MethodReference> MethodMap { get; } = new Dictionary<MethodReference, MethodReference>();

    public CloningContext(ModuleDefinition targetModule)
    {
        TargetModule = targetModule;
    }
}
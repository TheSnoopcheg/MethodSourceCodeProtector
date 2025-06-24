using Mono.Cecil;
using Mono.Collections.Generic;

namespace Protector.Provider;

public class GenericParameterContext : IGenericParameterContext
{
    public Collection<TypeReference>? MethodGenericParameters { get; set; }
    public Collection<TypeReference>? TypeGenericParameters { get; set; }
}
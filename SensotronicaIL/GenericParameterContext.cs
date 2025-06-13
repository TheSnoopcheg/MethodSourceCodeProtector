using Mono.Cecil;
using Mono.Collections.Generic;

namespace SensotronicaIL;

public class GenericParameterContext : IGenericParameterContext
{
    public Collection<TypeReference>? MethodGenericParameters { get; set; }
    public Collection<TypeReference>? TypeGenericParameters { get; set; }
}
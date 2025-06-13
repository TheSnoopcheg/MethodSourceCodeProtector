using Mono.Cecil;
using Mono.Collections.Generic;

namespace SensotronicaIL;

public interface IGenericParameterContext
{
    Collection<TypeReference>? MethodGenericParameters { get; set; }
    Collection<TypeReference>? TypeGenericParameters { get; set; }
}

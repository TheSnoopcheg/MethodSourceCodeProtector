using Mono.Cecil;
using Mono.Collections.Generic;

namespace Protector.Patcher;

public interface IGenericParameterContext
{
    Collection<TypeReference>? MethodGenericParameters { get; set; }
    Collection<TypeReference>? TypeGenericParameters { get; set; }
}

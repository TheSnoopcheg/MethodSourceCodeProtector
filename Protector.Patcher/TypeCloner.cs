using Mono.Cecil;

namespace Protector.Patcher;

// TODO: Add support for generic parameters, custom attributes, and other members as needed.

/// <summary>
/// Clones a TypeDefinition and its members from a source module to a target module.
/// </summary>
public class TypeCloner
{
    private readonly TypeDefinition _sourceType;
    private readonly CloningContext _context;

    public TypeCloner(TypeDefinition sourceType, CloningContext context)
    {
        _sourceType = sourceType;
        _context = context;
    }

    public TypeDefinition Clone()
    {
        if (_context.TypeMap.ContainsKey(_sourceType))
        {
            return (TypeDefinition)_context.TypeMap[_sourceType];
        }

        var newType = new TypeDefinition(
            _sourceType.Namespace,
            _sourceType.Name,
            TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit
        );

        _context.TypeMap[_sourceType] = newType;
        _context.TargetModule.Types.Add(newType);

        newType.BaseType = _context.TargetModule.ImportReference(_sourceType.BaseType, newType);

        CloneFields(newType);
        CloneMethods(newType);
        CloneEvents(newType);

        return newType;
    }
    private void CloneEvents(TypeDefinition newType)
    {
        foreach (var oldEvent in _sourceType.Events)
        {
            var newEvent = new EventDefinition(
                oldEvent.Name,
                oldEvent.Attributes,
                _context.TargetModule.ImportReference(oldEvent.EventType, newType)
            );

            if (oldEvent.AddMethod != null)
            {
                newEvent.AddMethod = (MethodDefinition)_context.MethodMap[oldEvent.AddMethod.Resolve()];
            }
            if (oldEvent.RemoveMethod != null)
            {
                newEvent.RemoveMethod = (MethodDefinition)_context.MethodMap[oldEvent.RemoveMethod.Resolve()];
            }

            newType.Events.Add(newEvent);
        }
    }
    private void CloneFields(TypeDefinition newType)
    {
        foreach (var oldField in _sourceType.Fields)
        {
            var newField = new FieldDefinition(
                oldField.Name,
                oldField.Attributes,
                _context.TargetModule.ImportReference(oldField.FieldType, newType)
            );
            newType.Fields.Add(newField);
            _context.FieldMap[oldField] = newField;
        }
    }

    private void CloneMethods(TypeDefinition newType)
    {
        foreach (var oldMethod in _sourceType.Methods)
        {
            var newMethod = new MethodDefinition(
                oldMethod.Name,
                oldMethod.Attributes,
                _context.TargetModule.ImportReference(oldMethod.ReturnType, newType)
            );

            foreach (var p in oldMethod.GenericParameters)
            {
                newMethod.GenericParameters.Add(new GenericParameter(p.Name, newMethod));
            }

            foreach (var p in oldMethod.Parameters)
            {
                newMethod.Parameters.Add(new ParameterDefinition(
                    p.Name, p.Attributes, _context.TargetModule.ImportReference(p.ParameterType, newMethod)
                ));
            }

            newType.Methods.Add(newMethod);
            _context.MethodMap[oldMethod] = newMethod;

            if (oldMethod.HasBody)
            {
                var bodyCloner = new CecilMethodBodyCloner(oldMethod, newMethod, _context);
                bodyCloner.Clone();
            }
        }
    }
}
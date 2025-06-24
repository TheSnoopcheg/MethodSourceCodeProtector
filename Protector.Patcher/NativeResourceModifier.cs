using Mono.Cecil;
using System.Collections;
using System.Resources;

namespace Protector.Patcher;

public class NativeResourceModifier : IDisposable
{
    private string _resourceName = "Protector.Provider.resources.resources";
    private AssemblyDefinition _assembly;
    private EmbeddedResource? _embeddedResource;
    private Dictionary<string, object> _resourcesDict = new Dictionary<string, object>();
    private List<NativeObjectInfo> _methodIdTable = new List<NativeObjectInfo>();

    public NativeResourceModifier(string assemblyPath)
    {
        _assembly = AssemblyDefinition.ReadAssembly(assemblyPath, new ReaderParameters { ReadWrite = true });

        _embeddedResource = _assembly.MainModule.Resources
            .OfType<EmbeddedResource>()
            .FirstOrDefault(r => r.Name == _resourceName);

        if (_embeddedResource == null)
        {
            throw new InvalidOperationException($"Resource '{_resourceName}' not found or is not an embedded resource.");
        }

        using (var resourceStream = _embeddedResource.GetResourceStream())
        using (var reader = new ResourceReader(resourceStream))
        {
            foreach (DictionaryEntry entry in reader)
            {
                _resourcesDict.Add((string)entry.Key, entry.Value);
            }
        }
        if (!_resourcesDict.ContainsKey("METHODIDTABLE"))
        {
            throw new InvalidOperationException("Resource 'METHODIDTABLE' not found in the resource dictionary.");
        }
        if (_resourcesDict["METHODIDTABLE"] is string methodIdTable)
        {
            var methodIdTableData = Convert.FromBase64String(methodIdTable);
            _methodIdTable = (List<NativeObjectInfo>)BinaryListSerializer<NativeObjectInfo>.Deserialize(methodIdTableData);
            foreach(var m in _methodIdTable)
            {
                Console.WriteLine($"{m.MethodName}\t{m.ResourceName}");
            }
        }
        else
        {
            throw new InvalidOperationException("Resource 'METHODIDTABLE' is not of type byte[].");
        }
    }

    public void Dispose()
    {
        _assembly.Dispose();
        GC.SuppressFinalize(this);
    }

    private void ModifyResource(NativeObjectInfo info, byte[] newData)
    {
        if (_resourcesDict.ContainsKey(info.ResourceName))
        {
            _resourcesDict[info.ResourceName] = newData;
        }
        else
        {
            throw new InvalidOperationException($"Resource '{info.ResourceName}' not found in the resource dictionary.");
        }
    }
    public void AddResource(string methodName, byte[] data)
    {
        var methodIdInfo = _methodIdTable.FirstOrDefault(m => m.MethodName == methodName);
        if (!methodIdInfo.Equals(default(NativeObjectInfo)))
        {
            ModifyResource(methodIdInfo, data);
        }
        else
        {
            methodIdInfo.MethodName = methodName;
            methodIdInfo.ResourceName = Guid.NewGuid().ToString();

            _methodIdTable.Add(methodIdInfo);
            _resourcesDict.Add(methodIdInfo.ResourceName, data);
        }
    }   

    public void Save()
    {
        var newResourceData = new MemoryStream();
        using (var writer = new ResourceWriter(newResourceData))
        {
            foreach (var entry in _resourcesDict)
            {
                if(entry.Key == "METHODIDTABLE")
                {
                    var serializedData = BinaryListSerializer<NativeObjectInfo>.Serialize(_methodIdTable);
                    writer.AddResource(entry.Key, Convert.ToBase64String(serializedData));
                    continue;
                }
                writer.AddResource(entry.Key, entry.Value);
            }
        }

        var newEmbeddedResource = new EmbeddedResource(_resourceName,
            ManifestResourceAttributes.Public, newResourceData.ToArray());

        _assembly.MainModule.Resources.Remove(_embeddedResource);
        _assembly.MainModule.Resources.Add(newEmbeddedResource);

        _assembly.Write();
    }
}

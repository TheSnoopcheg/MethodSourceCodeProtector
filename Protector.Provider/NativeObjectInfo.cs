namespace Protector.Provider;

public struct NativeObjectInfo : IBinarySerializable<NativeObjectInfo>
{
    public NativeObjectInfo()
    {
    }

    public string MethodName { get; set; } = string.Empty;
    public string ResourceName { get; set; } = string.Empty;

    public void Write(BinaryWriter writer)
    {
        writer.Write(MethodName ?? string.Empty);
        writer.Write(ResourceName ?? string.Empty);
    }

    public static NativeObjectInfo Read(BinaryReader reader)
    {
        return new NativeObjectInfo
        {
            MethodName = reader.ReadString(),
            ResourceName = reader.ReadString()
        };
    }
}

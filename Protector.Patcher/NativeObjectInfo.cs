using Protector.Patcher.Extensions;

namespace Protector.Patcher;

public struct NativeObjectInfo : IBinarySerializable<NativeObjectInfo>
{
    public NativeObjectInfo()
    {
    }

    public string MethodName { get; set; } = string.Empty;
    public nint ResourceID { get; set; }

    public void Write(BinaryWriter writer)
    {
        writer.Write(MethodName ?? string.Empty);
        writer.Write(ResourceID);
    }

    public static NativeObjectInfo Read(BinaryReader reader)
    {
        return new NativeObjectInfo
        {
            MethodName = reader.ReadString(),
            ResourceID = reader.ReadNInt()
        };
    }
}

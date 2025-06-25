namespace Protector.Patcher;

public static class BinaryListSerializer<T> where T : IBinarySerializable<T>
{
    public static byte[] Serialize(IList<T> list)
    {
        using var memoryStream = new MemoryStream();
        using var writer = new BinaryWriter(memoryStream);
        writer.Write(list.Count);
        foreach (var item in list)
        {
            item.Write(writer);
        }
        return memoryStream.ToArray();
    }
    public static IList<T> Deserialize(byte[] data)
    {
        using var memoryStream = new MemoryStream(data);
        if (memoryStream.Length < 4)
        {
            return new List<T>(0); // Return an empty list if the data is empty
        }
        using var reader = new BinaryReader(memoryStream);
        int count = reader.ReadInt32();
        var list = new List<T>(count);
        for (int i = 0; i < count; i++)
        {
            list.Add(T.Read(reader));
        }
        return list;
    }
}

public interface IBinarySerializable<T>
{
    void Write(BinaryWriter writer);
    static abstract T Read(BinaryReader reader);
}

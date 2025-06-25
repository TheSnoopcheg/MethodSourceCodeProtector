namespace Protector.Patcher.Extensions;

public static class BinaryReaderExtension
{
    public static nint ReadNInt(this BinaryReader reader)
    {
        if (IntPtr.Size == 8)
        {
            long val = reader.ReadInt64();
            return (nint)val;
        }
        else
        {
            int val = reader.ReadInt32();
            return (nint)val;
        }
    }
}

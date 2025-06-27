namespace ExternalDependency;

public static class Writer
{
    public static void Write<T>(T value)
    {
        Console.WriteLine(value);
    }
}

public static class Writer<T>
{
    public static void Write(T value)
    {
        Console.WriteLine(value);
    }
}
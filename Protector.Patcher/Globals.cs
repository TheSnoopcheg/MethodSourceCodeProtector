namespace Protector.Patcher;

public static class Globals
{
    public const string PATH = $"TestProject.dll";
    public static string FULLPATH => @$"{AppDomain.CurrentDomain.BaseDirectory}\{PATH}";
    public static string NEWPATH => @$"{AppDomain.CurrentDomain.BaseDirectory}\m\new.dll";
    public static string NEWSPATH => @$"{AppDomain.CurrentDomain.BaseDirectory}\m\1TestProject.dll";
    public static string NATIVEDLL => $@"{AppDomain.CurrentDomain.BaseDirectory}\output\Native.dll";
    public static string NEWDLLPATH (string name) => @$"{AppDomain.CurrentDomain.BaseDirectory}\output\{name}.dll";

    //6556325
}

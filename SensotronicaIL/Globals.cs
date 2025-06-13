namespace SensotronicaIL
{
    public static class Globals
    {
        public const string PATH = $"TestProject.dll";
        public static string FULLPATH => @$"{AppDomain.CurrentDomain.BaseDirectory}\{PATH}";
        public static string NEWPATH => @$"{AppDomain.CurrentDomain.BaseDirectory}\m\new.dll";

        //6556325
    }
}

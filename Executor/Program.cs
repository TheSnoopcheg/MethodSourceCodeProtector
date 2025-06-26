using TestProject;

namespace Executor;

internal class Program
{
    static void Main(string[] args)
    {
        try
        {
            Class1<int> class1 = new Class1<int>(3);
            class1.MethodB(1, "Hello world");
            class1.MethodB(4);
            class1.MethodB("Test string");
            Console.WriteLine(Class1<int>.MethodB());
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
            Console.WriteLine(ex.StackTrace);
        }
        finally
        {
            Console.ReadLine();
        }
    }
}

using TestProject;

namespace Executor;

internal class Program
{
    static void Main(string[] args)
    {
        try
        {
            Class1 class1 = new Class1(3);
            class1.MethodB(1, "Hello world");
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

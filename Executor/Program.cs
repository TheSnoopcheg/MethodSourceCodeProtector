using TestProject;

namespace Executor;

internal class Program
{
    static void Main(string[] args)
    {
        try
        {

            Class1<int,float> class1 = new Class1<int, float>(3);

            class1.MethodC("Test operation", 6);

            //class1.MethodB<string>(2, "Hello world");
            class1.MethodB<string, List<int>>(2.5F, [1,2,3], "Hello", 3);
            //class1.MethodB<string>("Test string");
            //class1.MethodB<List<int>>([5, 5, 5]);
            //Console.WriteLine(Class1<string,int>.MethodBS<int>("string", 8));
            //Console.WriteLine(Class1<int,int>.MethodBS<int>(8, 8));
        }
        //catch (Exception ex)
        //{
        //    Console.WriteLine(ex.Message);
        //    Console.WriteLine(ex.StackTrace);
        //}
        finally
        {
            Console.ReadLine();
        }
    }
}

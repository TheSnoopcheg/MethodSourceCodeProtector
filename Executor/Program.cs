using TestProject;

namespace Executor;

internal class Program
{
    static async Task Main(string[] args)
    {
        try
        {

            AsyncClass<int> asyncClass = new AsyncClass<int>();

            var task1 = asyncClass.GetDataAsync();
            var task2 = asyncClass.ProcessDataAsync<string>("Hello World");
            var task3 = asyncClass.LambdaTest("Lambda Test");
            var task4 = asyncClass.VAsync(42);

            await task1;
            await task2;
            await task3;
            await task4;

            Class1<int, float> class1 = new Class1<int, float>(3);

            var res = class1.MethodD([1, 2, 3], 4);
            foreach (var item in res)
                Console.WriteLine(item);

            class1.MethodC("Test operation", 6);

            class1.MethodB<string>(2, "Hello world");
            class1.MethodB<string, List<int>>(2.5F, [1, 2, 3], "Hello", 3);
            class1.MethodB<string>("Test string");
            class1.MethodB<List<int>>([5, 5, 5]);
            Console.WriteLine(Class1<string, int>.MethodBS<int>("string", 8));
            Console.WriteLine(Class1<int, int>.MethodBS<int>(8, 8));
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

namespace TestProject;

public class AsyncClass<V>
{
    [Protect]
    public async Task<string> GetDataAsync()
    {
        await Task.Delay(5000);
        Console.WriteLine("1st");
        return "Data from async method";
    }
    [Protect]
    public async Task<string> ProcessDataAsync<T>(T input)
    {
        await Task.Delay(2000);
        Console.WriteLine("2nd");
        return $"Processed: {input}";
    }
    [Protect]
    public async Task LambdaTest(string str)
    {
        var lambda = async () =>
        {
            await Task.Delay(1000);
            Console.WriteLine($"Lambda executed with input: {str}");
        };
        await lambda();
    }
    [Protect]
    public async Task<V> VAsync(V input)
    {
        await Task.Delay(3000);
        Console.WriteLine($"VAsync executed with input: {input}");
        return input;
    }
}

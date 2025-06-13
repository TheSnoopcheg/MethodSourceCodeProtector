namespace TestProject;

public class Person<T>
{
    public T Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public override string ToString()
    {
        return Name;
    }
    public void SetId(T id)
    {
        Id = id;
    }
    public void ShowSmth<V, U>(int n, U i, V smth, T smth1)
    {
        Console.WriteLine($"{smth} {smth1} {n} {i}");
    }
}

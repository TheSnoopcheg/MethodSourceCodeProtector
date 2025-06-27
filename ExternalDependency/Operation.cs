namespace ExternalDependency;

public class Operation
{
    public string Name { get; set; } = string.Empty;

    public override string ToString()
    {
        return Name;
    }
}

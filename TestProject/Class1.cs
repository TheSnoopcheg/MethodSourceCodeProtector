namespace TestProject
{
    public class Class1
    {
        private int i = 0;
        private event Action handler;
        private List<int> ints = new List<int>();
        public Class1(int I )
        {
            i = I;
        }
        public void MethodA()
        {
            MethodB<string>(2, "Hello world");
        }
        public T MethodB<T>(int n, object smth)
        {
            i++;
            ints.Add(i);
            int[] arr = new int[ints.Count];
            ints.CopyTo(0, arr, 0, ints.Count);

            if(i == 4)
                handler += MethodA;
            else
                handler -= MethodA;


            int b = 3;

            Person<int> p = new Person<int> { Name = "Test" };

            Func<int, string> func = (x) => $"Value: {++x}";

            Console.WriteLine(func(i));

            var f = () => $"Value lambda: {b}";
            Console.WriteLine(f());

            handler?.Invoke();

            p.SetId(i);
            p.ShowSmth<int, T>(n, (T)smth, i, p.Id);
            Console.WriteLine($"{smth} {i} {p.Name}");

            foreach (var item in ints)
            {
                Console.WriteLine(item);
            }
            return (T)smth;
        }
    }
}

using ExternalDependency;

namespace TestProject
{
    public class Class1<U, V>
    {
        private int i = 0;
        public event Action handler;
        private List<int> ints = new List<int>();
        public Class1(int I)
        {
            i = I;
        }
        public void MethodA()
        {
            //MethodB<string>(2, "Hello world");
        }
        [Protect]
        public void MethodB<T>(U n, T smth)
        {
            Console.WriteLine($"MethodB called with n={n} and smth={smth}");
        }

        [Protect]
        public U MethodB<T, W>(V a, W e, T smth, U d)
        {
            Func<int, string> func = (x) => $"Value: {x}";

            Console.WriteLine(func(i));

            long s = SquareProcessor.Square(i);

            Console.WriteLine($"Square of {i} is {s}");

            i++;
            ints.Add(i);
            int[] arr = new int[ints.Count];
            ints.CopyTo(0, arr, 0, ints.Count);

            if (i == 4)
                handler += MethodA;
            else
                handler -= MethodA;

            int b = 3;
            b += i;

            var f = () => $"Value lambda: {b}";
            Console.WriteLine(f());

            Person<U> p = new Person<U> { Name = "Test" };

            handler?.Invoke();

            Console.WriteLine(i);

            p.SetId(d);
            p.ShowSmth<U, T>(2, smth, d, p.Id);
            Console.WriteLine($"{smth} {i} {p.Name}");

            foreach (var item in ints)
            {
                Console.WriteLine(item);
            }
            return d;
        }
        [Protect]
        public void MethodB<T>(T smth)
        {
            Console.WriteLine($"MethodB called with smth={smth}");
        }
        [Protect]
        public void MethodB(List<int> n)
        {
            foreach (var item in n)
            {
                Console.WriteLine($"Item: {item}");
            }
        }
        [Protect]
        public void MethodC(string str, int n)
        {
            Writer.Write<int>(n);
            Writer<int>.Write(n);
            Writer.Write<string>(str);
            Writer<string>.Write(str);
            var operation = new Operation { Name = str };
            Writer.Write<Operation>(operation);
            Writer<Operation>.Write(operation);
        }

        [Protect]
        public static U MethodBS<T>(U n, V n1)
        {
            return n;
        }
        [Protect]
        public List<int> MethodD(List<int> list, U value)
        {
            return list;
        }
        [Protect]
        public static void MethodE(U[][,,][,] values)
        {
            Console.WriteLine("MethodE called with values:");
            foreach (var value in values)
            {
                Console.WriteLine(value);
            }
        }
    }
}

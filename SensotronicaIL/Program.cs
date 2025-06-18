using Mono.Cecil;
using Mono.Cecil.Rocks;
using System.Reflection;
using System.Reflection.Emit;
using TestProject;
using NETMethodAttributes = System.Reflection.MethodAttributes;
using NETTypeAttributes = System.Reflection.TypeAttributes;

namespace SensotronicaIL
{
    internal class Program
    {
        static void Main(string[] args)
        {
            MONO_VERSION(Globals.FULLPATH, true, true);
            //MONO_VERSION(Globals.NEWPATH, false, true);
            //MONO_VERSION(Globals.NEWSPATH, false, true);

            DynamicMethodProvider provider = new DynamicMethodProvider();

            Assembly asm = Assembly.LoadFile(Globals.FULLPATH);
            var type = asm.GetType("TestProject.Class1");
            var method = type.GetMethod("MethodB");
            var typeInstance = Activator.CreateInstance(type, 3);
            var del = provider.GetDelegate(method, typeof(Person<int>), new Type[] { typeof(Person<int>) });
            var parameters = new object[] { typeInstance, 1, new Person<int> { Name = "Test1" } };
            del.DynamicInvoke(parameters);

            //AssemblyDefinition asm = AssemblyDefinition.ReadAssembly(Globals.NEWPATH);
            //var orType = asm.MainModule.GetType("Test.Test1");
            //var orMethod = orType.Methods.FirstOrDefault(m => m.Name == "MethodB");
            //DynamicMethod method = new DynamicMethod(
            //    "TestMethodB",
            //    typeof(Person<int>),
            //    new Type[] { typeof(Class1), typeof(int), typeof(Person<int>) },
            //    typeof(Class1).Module,
            //    true);
            //var il = method.GetILGenerator();
            //CecilToRefMethodBodyCloner cloner = new CecilToRefMethodBodyCloner(il, orMethod, null, new Type[] { typeof(Person<int>) });
            //cloner.EmitBody();

            ////var smth = (Action<Class1>)method.CreateDelegate(typeof(Action<Class1>));
            //var sm = typeof(Func<Class1, int, Person<int>, Person<int>>);
            //var smth = (Func<Class1, int, Person<int>, Person<int>>)method.CreateDelegate(typeof(Func<Class1, int, Person<int>, Person<int>>));
            //Class1 c = new Class1(3);
            //Person<int> p = new Person<int> { Name = "Test1" };
            //try
            //{
            //    Console.WriteLine(smth(c, 1, p));
            //    //smth(c);
            //}
            //catch (InvalidProgramException ex)
            //{
            //    Console.WriteLine(ex.Message);
            //}
            //catch (Exception ex)
            //{
            //    Console.WriteLine(ex.Message);
            //}
            Console.ReadLine();
        }
        static void MONO_VERSION(string path, bool proc, bool showCode)
        {
            ModuleDefinition module = ModuleDefinition.ReadModule(path);
            foreach (var type in module.GetAllTypes())
            {
                Console.WriteLine($"{type.Name}\t{type.BaseType}");
                foreach (var method in type.Methods)
                {
                    Console.WriteLine($"\t {method.Name}");
                    if (method.Name == "MethodB")
                    {
                        //foreach (var prop in method.GetType().GetProperties())
                        //{
                        //    Console.WriteLine($"{prop.Name}\t{prop.GetValue(method)}");
                        //}
                        if (showCode)
                        {
                            foreach (var instruction in method.Body.Instructions)
                            {
                                Console.WriteLine(instruction);
                            }
                        }
                        if (proc)
                        {
                            MonoAssemblyCreator.CreateAssembly(method);

                            EraseMethodAnsSave(method, module);

                        }
                    }
                }
            }
        }
        static void EraseMethodAnsSave(MethodDefinition method, ModuleDefinition module)
        {
            method.Body.Instructions.Clear();
            //method.Body.Variables.Clear();
            method.Body.ExceptionHandlers.Clear();
            var il = method.Body.GetILProcessor();
            il.Append(il.Create(Mono.Cecil.Cil.OpCodes.Ret));

            module.Write(Globals.NEWSPATH);
        }

        static void WriteByteArrays()
        {
            byte[] raw = File.ReadAllBytes(Globals.FULLPATH);
            Assembly asm1 = Assembly.Load(raw);
            var method1 = asm1.GetType("TestProject.Class1").GetMethod("MethodB");
            Console.WriteLine("Original byte array:");
            foreach (var b in method1.GetMethodBody().GetILAsByteArray())
            {
                Console.Write(b);
            }
            Console.WriteLine();
            byte[] raw1 = File.ReadAllBytes(Globals.NEWPATH);
            Assembly asm11 = Assembly.Load(raw1);
            var method11 = asm11.GetType("Test.Test1").GetMethod("MethodB");
            Console.WriteLine("Copy (after load) byte array:");
            foreach (var b in method11.GetMethodBody().GetILAsByteArray())
            {
                Console.Write(b);
            }
            Console.WriteLine();
        }
        static void NET_CreateAssembly(MethodDefinition methodDef)
        {
            AssemblyName asmName = new AssemblyName("1TestProject");
            PersistedAssemblyBuilder asm = new PersistedAssemblyBuilder(asmName, typeof(object).Assembly);
            var module = asm.DefineDynamicModule("Module");
            var type = module.DefineType("Type", NETTypeAttributes.Public | NETTypeAttributes.Class);
            var method = type.DefineMethod("method", NETMethodAttributes.Public, null, null);
            var il = method.GetILGenerator();
            CecilToRefMethodBodyCloner cloner = new CecilToRefMethodBodyCloner(il, methodDef, null, null);
            cloner.EmitBody();
            type.CreateType();

            using var stream = new MemoryStream();
            //asm.Save(stream);
            asm.Save(Globals.NEWSPATH);
            //stream.Seek(0, SeekOrigin.Begin);
            //AssemblyDefinition asm1 = AssemblyDefinition.ReadAssembly(stream);
            //var method1 = asm1.MainModule.GetType("Type").Methods.FirstOrDefault(m => m.Name == "method");
            //foreach(var i in method1.Body.Instructions)
            //{
            //    Console.WriteLine(i);
            //}
            //Console.WriteLine();
            //Assembly assembly = AssemblyLoadContext.Default.LoadFromStream(stream);
            //var method1 = assembly.GetType("Type").GetMethod("method");

            //Console.WriteLine("Created assembly byte array:");
            //foreach (var b in method1.GetMethodBody().GetILAsByteArray())
            //{
            //    Console.Write(b);
            //}
            //Console.WriteLine();
        }
    }
}
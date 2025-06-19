using Mono.Cecil;
using Protector.Patcher;

namespace Protector.Visual;

public enum MenuOptionType
{
    None,
    Assembly,
    Module,
    Type
}
public record class MenuOption(object Reference);

internal class Program
{
    static MenuOptionType currentState = MenuOptionType.None;
    static Stack<MenuOption> menuStack = new Stack<MenuOption>();
    static List<object> options = new List<object>();
    static object? selectedMember;
    static AssemblyPatcher patcher;
    static List<MethodDefinition> methodsToPatch = new List<MethodDefinition>();

    static void Main(string[] args)
    {
        string path = string.Empty;
        int selected = 0;
        bool isOut = false;
        if (args.Length == 0)
        {
            Console.Write("Enter the path to the directory containing the dll to process: ");
            path = Console.ReadLine();
        }
        else
        {
            path = args[0];
        }
        try
        {
            UpdateOptionsByState(path);
            ConsoleKey key;
            do
            {
                Console.Clear();
                Console.WriteLine(selectedMember);
                Console.WriteLine();
                for (int i = 0; i < options.Count; i++)
                {
                    if (i == selected)
                    {
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine("> " + options[i]);
                        Console.ResetColor();
                    }
                    else if (options[i] is MethodDefinition method && methodsToPatch.Contains(method))
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine("  " + options[i]);
                        Console.ResetColor();
                    }
                    else
                    {
                        Console.WriteLine("  " + options[i]);
                    }
                }
                Console.WriteLine("\nPress Enter to select, Up/Down to navigate, Backspace to go back, F to patch, Escape to exit.");

                key = Console.ReadKey(true).Key;
                switch (key)
                {
                    case ConsoleKey.UpArrow:
                        selected = (selected == 0) ? options.Count - 1 : selected - 1;
                        break;
                    case ConsoleKey.DownArrow:
                        if(options.Count == 0) continue; // Prevents error if options is empty
                        selected = (selected + 1) % options.Count;
                        break;
                    case ConsoleKey.Enter:
                        if (currentState == MenuOptionType.Type)
                        {
                            patcher.AddOperation(new PatchOperation
                            {
                                Method = (MethodDefinition)options[selected],
                                Type = (TypeDefinition)selectedMember!
                            });
                            methodsToPatch.Add((MethodDefinition)options[selected]);
                            continue;
                        }
                        menuStack.Push(new MenuOption(options[selected]));
                        selected = 0;
                        currentState++;
                        UpdateOptionsByState(path);
                        break;
                    case ConsoleKey.Backspace:
                        if(menuStack.Count > 1)
                        {
                            menuStack.Pop();
                            selected = 0;
                            currentState--;
                            UpdateOptionsByState(path);
                        }
                        break;
                    case ConsoleKey.F:
                        if (methodsToPatch.Count > 0)
                        {
                            patcher.PatchAssembly();
                            isOut = true;
                        }
                        break;
                }

            } while (key != ConsoleKey.Escape || !isOut);
        }
        finally
        {

        }
        Console.WriteLine("Exiting...");
        Console.ReadLine();
    }
    static void UpdateOptionsByState(string path)
    {
        switch (currentState)
        {
            case MenuOptionType.None:
                AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(path);
                patcher = new AssemblyPatcher(assembly);
                menuStack.Push(new MenuOption(assembly));
                options = assembly.Modules.Cast<object>().ToList();
                currentState = MenuOptionType.Assembly;
                break;
            case MenuOptionType.Assembly:
                var asm = (AssemblyDefinition)menuStack.Peek().Reference;
                selectedMember = asm;
                options = asm.Modules.Cast<object>().ToList();
                break;
            case MenuOptionType.Module:
                var module = (ModuleDefinition)menuStack.Peek().Reference;
                selectedMember = module;
                options = module.Types.Cast<object>().ToList();
                break;
            case MenuOptionType.Type:
                var type = (TypeDefinition)menuStack.Peek().Reference;
                selectedMember = type;
                options = type.Methods.Cast<object>().ToList();
                break;
            default:
                throw new InvalidOperationException("Invalid menu state.");

        }
    }
}
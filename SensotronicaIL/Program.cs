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
public record class MenuOption(object Reference, int Page, int Selected);

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
        int page = 0;
        int pagesiize = 15;
        bool isOut = false;
        if (args.Length == 0)
        {
            Console.Write("Enter the path to the directory containing the dll to process: ");
            path = Console.ReadLine();
        }
        else
        {
            path = args[0];
            Console.WriteLine(path);
            Console.ReadLine();
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
                var pageOptions = options.Skip(page * pagesiize).Take(pagesiize).ToList();
                for (int i = 0; i < pageOptions.Count; i++)
                {
                    if (i == selected)
                    {
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine("> " + pageOptions[i]);
                        Console.ResetColor();
                    }
                    else if (options[i] is MethodDefinition method && methodsToPatch.Contains(method))
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine("  " + pageOptions[i]);
                        Console.ResetColor();
                    }
                    else
                    {
                        Console.WriteLine("  " + pageOptions[i]);
                    }
                }
                Console.WriteLine("\nUse arrow keys to navigate, Enter to select, Backspace to go back, F to finish patching, Escape to exit.");

                key = Console.ReadKey(true).Key;
                switch (key)
                {
                    case ConsoleKey.UpArrow:
                        selected = (selected == 0) ? pageOptions.Count - 1 : selected - 1;
                        break;
                    case ConsoleKey.DownArrow:
                        if(options.Count == 0) continue; // Prevents error if options is empty
                        selected = (selected + 1) % pageOptions.Count;
                        break;
                    case ConsoleKey.RightArrow:
                        if(page * pagesiize < options.Count - 1)
                        {
                            page++;
                            selected = 0;
                        }
                        break;
                    case ConsoleKey.LeftArrow:
                        if(page > 0)
                        {
                            page--;
                            selected = 0;
                        }
                        break;
                    case ConsoleKey.Enter:
                        if (currentState == MenuOptionType.Type)
                        {
                            patcher.AddOperation(new PatchOperation
                            {
                                Method = (MethodDefinition)pageOptions[selected],
                                Type = (TypeDefinition)selectedMember!
                            });
                            methodsToPatch.Add((MethodDefinition)pageOptions[selected]);
                            continue;
                        }
                        menuStack.Push(new MenuOption(pageOptions[selected], page, selected));
                        selected = 0;
                        page = 0;
                        currentState++;
                        UpdateOptionsByState(path);
                        break;
                    case ConsoleKey.Backspace:
                        if(menuStack.Count > 1)
                        {
                            var option = menuStack.Pop();
                            selected = option.Selected;
                            page = option.Page;
                            currentState--;
                            UpdateOptionsByState(path);
                        }
                        break;
                    case ConsoleKey.F:
                        if (methodsToPatch.Count > 0)
                        {
                            Console.Clear();
                            patcher.PatchAssembly();
                            isOut = true;
                        }
                        break;
                }
            } while (key != ConsoleKey.Escape && !isOut);
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
        }
        finally
        {
            Console.WriteLine("Press any key to leave");
            Console.ReadKey();
        }
    }
    static void UpdateOptionsByState(string path)
    {
        switch (currentState)
        {
            case MenuOptionType.None:
                AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(path);
                patcher = new AssemblyPatcher(assembly);
                menuStack.Push(new MenuOption(assembly, 0 ,0));
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
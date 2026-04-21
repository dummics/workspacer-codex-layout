using Mono.Cecil;
using Mono.Cecil.Cil;

var options = RuntimePatchOptions.Parse(args);

if (!File.Exists(options.AssemblyPath))
{
    throw new FileNotFoundException("workspacer.dll not found.", options.AssemblyPath);
}

Directory.CreateDirectory(options.BackupDirectory);

var backupPath = Path.Combine(options.BackupDirectory, "workspacer.dll");
if (!File.Exists(backupPath))
{
    File.Copy(options.AssemblyPath, backupPath);
}

var tempPath = options.AssemblyPath + ".codex-patch.tmp";
var resolver = new DefaultAssemblyResolver();
resolver.AddSearchDirectory(Path.GetDirectoryName(options.AssemblyPath) ?? Environment.CurrentDirectory);

using var assembly = AssemblyDefinition.ReadAssembly(options.AssemblyPath, new ReaderParameters
{
    AssemblyResolver = resolver,
    ReadWrite = false,
    InMemory = true
});

var keybindManager = assembly.MainModule.Types.FirstOrDefault(type => type.FullName == "workspacer.KeybindManager");
if (keybindManager is null)
{
    throw new InvalidOperationException("Type workspacer.KeybindManager was not found.");
}

var constructors = keybindManager.Methods.Where(method => method.IsConstructor && !method.IsStatic).ToArray();
if (constructors.Length == 0)
{
    throw new InvalidOperationException("No instance constructor was found for workspacer.KeybindManager.");
}

var patchedCalls = 0;
foreach (var constructor in constructors)
{
    patchedCalls += PatchSubscribeDefaultsCalls(constructor);
}

if (patchedCalls == 0)
{
    Console.WriteLine("Runtime already patched: no KeybindManager.SubscribeDefaults constructor call found.");
    return;
}

assembly.Write(tempPath);
File.Copy(tempPath, options.AssemblyPath, overwrite: true);
File.Delete(tempPath);

Console.WriteLine($"Patched {patchedCalls} KeybindManager.SubscribeDefaults constructor call(s). Backup: {backupPath}");

static int PatchSubscribeDefaultsCalls(MethodDefinition method)
{
    if (!method.HasBody)
    {
        return 0;
    }

    var instructions = method.Body.Instructions;
    var patched = 0;

    for (var index = 0; index < instructions.Count; index++)
    {
        var instruction = instructions[index];
        if ((instruction.OpCode != OpCodes.Call && instruction.OpCode != OpCodes.Callvirt) ||
            instruction.Operand is not MethodReference methodReference ||
            methodReference.Name != "SubscribeDefaults")
        {
            continue;
        }

        // The stable 0.9.11 constructor pushes "this" and the default modifier before
        // calling SubscribeDefaults. NOP the whole sequence so the evaluation stack stays valid.
        Nop(instruction);
        if (index >= 1)
        {
            Nop(instructions[index - 1]);
        }

        if (index >= 2)
        {
            Nop(instructions[index - 2]);
        }

        patched++;
    }

    return patched;
}

static void Nop(Instruction instruction)
{
    instruction.OpCode = OpCodes.Nop;
    instruction.Operand = null;
}

internal sealed record RuntimePatchOptions(string AssemblyPath, string BackupDirectory)
{
    public static RuntimePatchOptions Parse(string[] args)
    {
        string? assemblyPath = null;
        string? backupDirectory = null;

        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];
            if (arg.Equals("--assembly", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
            {
                assemblyPath = args[++index];
                continue;
            }

            if (arg.Equals("--backup-directory", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
            {
                backupDirectory = args[++index];
                continue;
            }

            if (assemblyPath is null)
            {
                assemblyPath = arg;
            }
        }

        if (string.IsNullOrWhiteSpace(assemblyPath))
        {
            throw new ArgumentException("Usage: workspacer.RuntimePatcher --assembly <path-to-workspacer.dll> [--backup-directory <path>]");
        }

        assemblyPath = Path.GetFullPath(assemblyPath);
        backupDirectory = string.IsNullOrWhiteSpace(backupDirectory)
            ? Path.Combine(Path.GetDirectoryName(assemblyPath) ?? Environment.CurrentDirectory, "codex-runtime-backups", DateTime.Now.ToString("yyyyMMdd-HHmmss"))
            : Path.GetFullPath(backupDirectory);

        return new RuntimePatchOptions(assemblyPath, backupDirectory);
    }
}

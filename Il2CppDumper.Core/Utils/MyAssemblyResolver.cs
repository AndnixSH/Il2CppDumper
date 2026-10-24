using Mono.Cecil;
using System.Collections.Generic;

namespace Il2CppDumper
{
    public class MyAssemblyResolver : DefaultAssemblyResolver
    {
        // Registered assemblies are looked up from many threads while the dummy
        // DLLs are written in parallel; the base resolver's cache is not thread
        // safe, so serve registered names from a read-only map and lock the rest.
        private readonly Dictionary<string, AssemblyDefinition> registered = new();

        public MyAssemblyResolver()
        {
            // Never probe the working directory: it is the DummyDll output folder
            // while exporting, so a lookup could load a half-written dummy DLL.
            RemoveSearchDirectory(".");
            RemoveSearchDirectory("bin");
        }

        public void Register(AssemblyDefinition assembly)
        {
            RegisterAssembly(assembly);
            registered[assembly.Name.FullName] = assembly;
        }

        public override AssemblyDefinition Resolve(AssemblyNameReference name)
        {
            if (registered.TryGetValue(name.FullName, out var assembly))
            {
                return assembly;
            }
            lock (registered)
            {
                return base.Resolve(name);
            }
        }

        public override AssemblyDefinition Resolve(AssemblyNameReference name, ReaderParameters parameters)
        {
            if (registered.TryGetValue(name.FullName, out var assembly))
            {
                return assembly;
            }
            lock (registered)
            {
                return base.Resolve(name, parameters);
            }
        }
    }
}

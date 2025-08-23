using System.Reflection;
using System.Runtime.Loader;
using System.Runtime.InteropServices;

namespace Monovera
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            BootstrapLibraries();
            PreloadSQLiteAssemblies();

            // Late-bind to avoid early JIT resolution before our resolvers are attached
            var t = Type.GetType("SQLitePCL.Batteries_V2, SQLitePCLRaw.batteries_v2", throwOnError: true)!;
            t.GetMethod("Init", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, null);

            ApplicationConfiguration.Initialize();
            Application.Run(new frmMain());
        }

        static void BootstrapLibraries()
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var libs = Path.Combine(baseDir, "Libraries");
            if (!Directory.Exists(libs)) return;

            // Add Libraries and all subfolders to PATH for native (P/Invoke) loads
            void AddToPath(string dir)
            {
                var current = Environment.GetEnvironmentVariable("PATH") ?? "";
                var parts = current.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (!parts.Any(p => string.Equals(p, dir, StringComparison.OrdinalIgnoreCase)))
                {
                    Environment.SetEnvironmentVariable("PATH", dir + ";" + current);
                }
            }

            AddToPath(libs);
            foreach (var sub in Directory.EnumerateDirectories(libs, "*", SearchOption.AllDirectories))
                AddToPath(sub);

            // Resolve managed assemblies (including satellite resource assemblies) from Libraries
            AssemblyLoadContext.Default.Resolving += (context, asmName) =>
            {
                // Prefer culture-specific resource assemblies if requested
                if (!string.IsNullOrEmpty(asmName.CultureName) &&
                    asmName.Name != null &&
                    asmName.Name.EndsWith(".resources", StringComparison.OrdinalIgnoreCase))
                {
                    string resFile = asmName.Name + ".dll"; // e.g., MyLib.resources.dll
                    var cultureDir = Path.Combine(libs, asmName.CultureName);
                    if (Directory.Exists(cultureDir))
                    {
                        var path = Directory.EnumerateFiles(cultureDir, resFile, SearchOption.AllDirectories).FirstOrDefault();
                        if (path != null) return context.LoadFromAssemblyPath(path);
                    }
                }

                // Fallback to normal managed assembly probing in Libraries
                if (!string.IsNullOrEmpty(asmName.Name))
                {
                    string file = asmName.Name + ".dll";
                    var candidate = Directory.EnumerateFiles(libs, file, SearchOption.AllDirectories).FirstOrDefault();
                    if (candidate != null) return context.LoadFromAssemblyPath(candidate);
                }

                return null;
            };

            // Resolve unmanaged/native libraries from Libraries
            AssemblyLoadContext.Default.ResolvingUnmanagedDll += (requestingAssembly, libraryName) =>
            {
                string file = libraryName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                    ? libraryName
                    : libraryName + ".dll";

                var candidate = Directory.EnumerateFiles(libs, file, SearchOption.AllDirectories).FirstOrDefault();
                if (candidate != null && NativeLibrary.TryLoad(candidate, out var handle))
                {
                    return handle;
                }

                return IntPtr.Zero;
            };
        }

        // Preload SQLitePCL managed assemblies and native e_sqlite3 from Libraries
        static void PreloadSQLiteAssemblies()
        {
            try
            {
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var libs = Path.Combine(baseDir, "Libraries");
                if (!Directory.Exists(libs)) return;

                // Load managed assemblies explicitly so Batteries_V2.Init() finds them
                foreach (var name in new[] { "SQLitePCLRaw.batteries_v2.dll", "SQLitePCLRaw.core.dll" })
                {
                    var dll = Directory.EnumerateFiles(libs, name, SearchOption.AllDirectories).FirstOrDefault();
                    if (dll != null)
                    {
                        try { AssemblyLoadContext.Default.LoadFromAssemblyPath(dll); } catch { /* already loaded or incompatible */ }
                    }
                }

                // Load native e_sqlite3 if present
                var native = Directory.EnumerateFiles(libs, "e_sqlite3.dll", SearchOption.AllDirectories).FirstOrDefault();
                if (native != null)
                {
                    try { NativeLibrary.TryLoad(native, out _); } catch { /* ignore */ }
                }
            }
            catch { /* best effort */ }
        }
    }
}
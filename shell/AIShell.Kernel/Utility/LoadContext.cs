using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

namespace AIShell.Kernel;

internal class AgentAssemblyLoadContext : AssemblyLoadContext
{
    private readonly string _dependencyDir;
    private readonly string _nativeLibExt;
    private readonly List<string> _runtimeLibDir;
    private readonly List<string> _runtimeNativeDir;
    private readonly ConcurrentDictionary<string, Assembly> _cache;

    internal AgentAssemblyLoadContext(string name, string dependencyDir)
        : base($"{name.Replace(' ', '.')}-ALC", isCollectible: false)
    {
        if (!Directory.Exists(dependencyDir))
        {
            throw new ArgumentException($"The agent home directory '{dependencyDir}' doesn't exist.", nameof(dependencyDir));
        }

        // Save the full path to the dependencies directory when creating the context.
        _dependencyDir = dependencyDir;
        _runtimeLibDir = [];
        _runtimeNativeDir = [];
        _cache = new()
        {
            // Contracts exposed from 'AIShell.Abstraction' depend on these assemblies,
            // so agents have to depend on the same assemblies from the default ALC.
            // Otherwise, the contracts will break due to mis-match type identities.
            ["System.CommandLine"] = null,
            ["Microsoft.Extensions.AI.Abstractions"] = null,
        };

        if (OperatingSystem.IsWindows())
        {
            _nativeLibExt = ".dll";
            AddToList(_runtimeLibDir, Path.Combine(dependencyDir, "runtimes", "win", "lib"));
            AddToList(_runtimeNativeDir, Path.Combine(dependencyDir, "runtimes", "win", "native"));
        }
        else if (OperatingSystem.IsLinux())
        {
            _nativeLibExt = ".so";
            AddToList(_runtimeLibDir, Path.Combine(dependencyDir, "runtimes", "unix", "lib"));
            AddToList(_runtimeLibDir, Path.Combine(dependencyDir, "runtimes", "linux", "lib"));

            AddToList(_runtimeNativeDir, Path.Combine(dependencyDir, "runtimes", "unix", "native"));
            AddToList(_runtimeNativeDir, Path.Combine(dependencyDir, "runtimes", "linux", "native"));
        }
        else if (OperatingSystem.IsMacOS())
        {
            _nativeLibExt = ".dylib";
            AddToList(_runtimeLibDir, Path.Combine(dependencyDir, "runtimes", "unix", "lib"));
            AddToList(_runtimeLibDir, Path.Combine(dependencyDir, "runtimes", "osx", "lib"));

            AddToList(_runtimeNativeDir, Path.Combine(dependencyDir, "runtimes", "unix", "native"));
            AddToList(_runtimeNativeDir, Path.Combine(dependencyDir, "runtimes", "osx", "native"));
        }

        AddToList(_runtimeLibDir, Path.Combine(dependencyDir, "runtimes", RuntimeInformation.RuntimeIdentifier, "lib"));
        AddToList(_runtimeNativeDir, Path.Combine(dependencyDir, "runtimes", RuntimeInformation.RuntimeIdentifier, "native"));

        ResolvingUnmanagedDll += ResolveUnmanagedDll;
    }

    protected override Assembly Load(AssemblyName assemblyName)
    {
        if (_cache.TryGetValue(assemblyName.Name, out Assembly assembly))
        {
            return assembly;
        }

        lock (this)
        {
            if (_cache.TryGetValue(assemblyName.Name, out assembly))
            {
                return assembly;
            }

            // Create a path to the assembly in the dependencies directory.
            string assemblyFile = $"{assemblyName.Name}.dll";
            string path = Path.Combine(_dependencyDir, assemblyFile);

            if (File.Exists(path))
            {
                // If the assembly exists in our dependency directory, then load it into this load context.
                assembly = LoadFromAssemblyPath(path);
            }
            else
            {
                foreach (string dir in _runtimeLibDir)
                {
                    IEnumerable<string> result = Directory.EnumerateFiles(dir, assemblyFile, SearchOption.AllDirectories);
                    path = result.FirstOrDefault();

                    if (path is not null)
                    {
                        assembly = LoadFromAssemblyPath(path);
                        break;
                    }
                }
            }

            // Add the probing result to cache, regardless of whether we found it.
            // If we didn't find it, we will add 'null' to the cache so that we don't probe
            // again in case another loading request comes for the same assembly.
            _cache.TryAdd(assemblyName.Name, assembly);

            // Return the assembly if we found it, or return 'null' otherwise to depend on the default load context to resolve the request.
            return assembly;
        }
    }

    private nint ResolveUnmanagedDll(Assembly assembly, string libraryName)
    {
        string libraryFile = $"{libraryName}{_nativeLibExt}";

        foreach (string dir in _runtimeNativeDir)
        {
            IEnumerable<string> result = Directory.EnumerateFiles(dir, libraryFile, SearchOption.AllDirectories);
            string path = result.FirstOrDefault();

            if (path is not null)
            {
                return NativeLibrary.Load(path);
            }
        }

        return nint.Zero;
    }

    private static void AddToList(List<string> depList, string dirPath)
    {
        if (Directory.Exists(dirPath))
        {
            depList.Add(dirPath);
        }
    }
}

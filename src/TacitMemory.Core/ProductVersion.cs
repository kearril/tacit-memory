using System.Reflection;

namespace TacitMemory.Core;

/// <summary>The version of TacitMemory as built: the informational version when there is one, else the assembly version.</summary>
internal static class ProductVersion
{
    public static string Current { get; } =
        typeof(ProductVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(ProductVersion).Assembly.GetName().Version?.ToString()
        ?? "unknown";
}

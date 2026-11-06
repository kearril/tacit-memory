namespace TacitMemory.Core.Clients;

/// <summary>
/// Which other bindings use the same context file or skills folder as a given one. Clients are compared by name, paths by
/// <see cref="BindingPath"/>.
/// </summary>
internal static class SharedPaths
{
    /// <summary>The other bindings whose context file is <paramref name="contextFile"/>.</summary>
    public static IReadOnlyList<BindingRecord> ContextFileUsers(IEnumerable<BindingRecord> bindings, string client, string contextFile) =>
        bindings.Where(binding => binding.Client != client && BindingPath.Same(binding.ContextFile, contextFile)).ToList();

    /// <summary>The other bindings whose skills folder is <paramref name="skillsFolder"/>.</summary>
    public static IReadOnlyList<BindingRecord> SkillsFolderUsers(IEnumerable<BindingRecord> bindings, string client, string skillsFolder) =>
        bindings.Where(binding => binding.Client != client && BindingPath.Same(binding.SkillsFolder, skillsFolder)).ToList();

    /// <summary>The names of the other bindings that use either path of <paramref name="binding"/>, in record order.</summary>
    public static IReadOnlyList<string> SharedWith(IEnumerable<BindingRecord> bindings, BindingRecord binding) =>
        bindings
            .Where(other => other.Client != binding.Client
                && (BindingPath.Same(other.ContextFile, binding.ContextFile) || BindingPath.Same(other.SkillsFolder, binding.SkillsFolder)))
            .Select(other => other.Client)
            .ToList();

    /// <summary>The client names of <paramref name="users"/>, joined for a message.</summary>
    public static string Names(IEnumerable<BindingRecord> users) => string.Join(", ", users.Select(user => user.Client));
}

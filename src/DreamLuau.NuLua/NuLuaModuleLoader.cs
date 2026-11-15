using System.Text;
using System.Diagnostics.CodeAnalysis;
using NuLua;

namespace DreamLuau.NuLua;

/// <summary>NuLua module loader restricted to explicit sources and a configured directory.</summary>
sealed class NuLuaModuleLoader : LuaModuleLoader
{
    readonly IReadOnlyDictionary<string, string> inMemoryModules;
    readonly string? fileSystemRoot;
    readonly string extension;
    readonly int maxSourceBytes;

    public NuLuaModuleLoader(DreamModulePolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        if (policy.MaxSourceBytes <= 0 || policy.MaxSourceBytes == int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(policy), "Module size limit must be positive and below Int32.MaxValue.");
        if (string.IsNullOrWhiteSpace(policy.SourceExtension)
            || policy.SourceExtension[0] != '.'
            || policy.SourceExtension.Skip(1).Any(character => !char.IsAsciiLetterOrDigit(character)))
            throw new ArgumentException("Module extension must be a dot followed by ASCII letters or digits.", nameof(policy));

        inMemoryModules = policy.InMemoryModules.ToDictionary(
            pair => ValidateName(pair.Key),
            pair => pair.Value ?? throw new ArgumentException("Module source cannot be null.", nameof(policy)),
            StringComparer.Ordinal);
        extension = policy.SourceExtension;
        maxSourceBytes = policy.MaxSourceBytes;

        if (policy.FileSystemRoot is { } root)
        {
            var directory = new DirectoryInfo(Path.GetFullPath(root));
            if (!directory.Exists)
                throw new DirectoryNotFoundException($"Module root '{directory.FullName}' does not exist.");
            fileSystemRoot = ResolveDirectoryPath(directory.FullName);
        }

        if (inMemoryModules.Count == 0 && fileSystemRoot is null)
            throw new ArgumentException("Enabled module loading requires in-memory modules or a filesystem root.", nameof(policy));

        foreach (var source in inMemoryModules.Values)
            EnsureSize(source, maxSourceBytes);
    }

    protected override bool TryLoadModule(ILuaState state, string fullPath, string requireArgument)
    {
        var moduleName = ValidateNameOrNull(requireArgument);
        if (moduleName is null) return false;

        if (inMemoryModules.TryGetValue(moduleName, out var source))
        {
            LoadSource(state, source, moduleName);
            return true;
        }

        if (fileSystemRoot is null || !TryResolveFile(moduleName, out var modulePath))
            return false;

        source = ReadSource(modulePath);
        LoadSource(state, source, moduleName);
        return true;
    }

    protected override bool TryGetAliasPath(string alias, [NotNullWhen(true)] out string? path)
    {
        path = null;
        return false;
    }

    protected override string GetCacheKey(string path) =>
        ValidateNameOrNull(path) is { } moduleName ? $"dreamluau:{moduleName}" : path;

    bool TryResolveFile(string moduleName, out string path)
    {
        var segments = moduleName.Split('/');
        path = Path.GetFullPath(Path.Combine([fileSystemRoot!, .. segments]) + extension);
        var relative = Path.GetRelativePath(fileSystemRoot!, path);
        if (Path.IsPathRooted(relative)
            || relative == ".."
            || relative.StartsWith($"..{Path.DirectorySeparatorChar}", PathComparison)
            || relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", PathComparison))
            throw new InvalidOperationException("Resolved module path escaped its configured root.");

        var current = fileSystemRoot!;
        foreach (var segment in segments.Take(segments.Length - 1))
        {
            current = Path.Combine(current, segment);
            if (!Directory.Exists(current)) return false;
            RejectReparsePoint(current);
        }

        path = Path.GetFullPath(path);
        if (!File.Exists(path)) return false;
        RejectReparsePoint(path);
        return true;
    }

    string ReadSource(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var bytes = new byte[maxSourceBytes + 1];
        var length = 0;
        while (length < bytes.Length)
        {
            var read = stream.Read(bytes, length, bytes.Length - length);
            if (read == 0) break;
            length += read;
        }
        if (length > maxSourceBytes)
            throw new InvalidDataException($"Module '{path}' exceeds the {maxSourceBytes}-byte source limit.");
        return Encoding.UTF8.GetString(bytes, 0, length);
    }

    static void LoadSource(ILuaState state, string source, string moduleName)
    {
        state.LoadString(source, moduleName);
        state.Call(0, 1);
    }

    static void EnsureSize(string source, int limit)
    {
        if (Encoding.UTF8.GetByteCount(source) > limit)
            throw new ArgumentException($"In-memory module exceeds the {limit}-byte source limit.", nameof(source));
    }

    static string ValidateName(string name) =>
        ValidateNameOrNull(name) ?? throw new ArgumentException($"Invalid module name '{name}'.", nameof(name));

    static string? ValidateNameOrNull(string? name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        foreach (var segment in name.Split('/'))
        {
            if (segment.Length == 0 || !(IsAsciiLetter(segment[0]) || segment[0] == '_')) return null;
            if (segment.Skip(1).Any(character =>
                    !IsAsciiLetter(character) && !char.IsAsciiDigit(character) && character != '_' && character != '-'))
                return null;
        }
        return name;
    }

    static bool IsAsciiLetter(char value) => value is >= 'A' and <= 'Z' or >= 'a' and <= 'z';

    static void RejectReparsePoint(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException($"Module path '{path}' cannot include a symbolic link or reparse point.");
    }

    static string ResolveDirectoryPath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var current = Path.GetPathRoot(fullPath)!;
        var segments = fullPath[current.Length..]
            .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);

        foreach (var segment in segments)
        {
            var directory = new DirectoryInfo(Path.Combine(current, segment));
            var resolved = directory.ResolveLinkTarget(returnFinalTarget: true);
            current = resolved?.FullName ?? directory.FullName;
        }

        return Path.GetFullPath(current);
    }

    static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;
}

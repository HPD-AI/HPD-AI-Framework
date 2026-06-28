using System.Reflection;
using System.Runtime.Loader;
using HPD.Agent.Packages;

namespace HPD.Agent.Packages.DynamicDotNet;

public sealed class HpdDotNetPackageLoader
{
    public HpdDotNetPackageLoadResult Load(
        HpdPackageManifest manifest,
        string? baseDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        var entrypoint = manifest.Entrypoints.DotNet
            ?? throw new HpdDotNetPackageLoadException(
                $"Package '{manifest.Id}' does not declare a .NET entrypoint.");

        var assemblyPath = ResolveAssemblyPath(entrypoint.Assembly, baseDirectory);
        if (!File.Exists(assemblyPath))
        {
            throw new HpdDotNetPackageLoadException(
                $"Package assembly '{assemblyPath}' was not found.");
        }

        Assembly assembly;
        try
        {
            assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(assemblyPath);
        }
        catch (Exception ex)
        {
            throw new HpdDotNetPackageLoadException(
                $"Package assembly '{assemblyPath}' could not be loaded.",
                ex);
        }

        return Load(manifest, assembly);
    }

    public HpdDotNetPackageLoadResult Load(
        HpdPackageManifest manifest,
        Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(assembly);

        var entrypoint = manifest.Entrypoints.DotNet
            ?? throw new HpdDotNetPackageLoadException(
                $"Package '{manifest.Id}' does not declare a .NET entrypoint.");

        var packageType = assembly.GetType(entrypoint.PackageType, throwOnError: false)
            ?? throw new HpdDotNetPackageLoadException(
                $"Package type '{entrypoint.PackageType}' was not found in '{assembly.FullName}'.");

        if (!typeof(IHpdPackage).IsAssignableFrom(packageType))
        {
            throw new HpdDotNetPackageLoadException(
                $"Package type '{entrypoint.PackageType}' does not implement {nameof(IHpdPackage)}.");
        }

        IHpdPackage package;
        try
        {
            package = (IHpdPackage)(Activator.CreateInstance(packageType)
                ?? throw new HpdDotNetPackageLoadException(
                    $"Package type '{entrypoint.PackageType}' did not create an instance."));
        }
        catch (HpdDotNetPackageLoadException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new HpdDotNetPackageLoadException(
                $"Package type '{entrypoint.PackageType}' could not be created.",
                ex);
        }

        ValidateManifestIdentity(manifest, package);
        return new HpdDotNetPackageLoadResult(package, assembly);
    }

    private static string ResolveAssemblyPath(
        string assembly,
        string? baseDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assembly);
        return Path.GetFullPath(
            Path.IsPathFullyQualified(assembly) || string.IsNullOrWhiteSpace(baseDirectory)
                ? assembly
                : Path.Combine(baseDirectory, assembly));
    }

    private static void ValidateManifestIdentity(
        HpdPackageManifest manifest,
        IHpdPackage package)
    {
        if (!string.Equals(manifest.Id, package.Id, StringComparison.Ordinal))
        {
            throw new HpdDotNetPackageLoadException(
                $"Loaded package id '{package.Id}' does not match manifest id '{manifest.Id}'.");
        }

        if (!string.Equals(manifest.DisplayName, package.DisplayName, StringComparison.Ordinal))
        {
            throw new HpdDotNetPackageLoadException(
                $"Loaded package display name '{package.DisplayName}' does not match manifest display name '{manifest.DisplayName}'.");
        }

        if (manifest.Version != package.Version)
        {
            throw new HpdDotNetPackageLoadException(
                $"Loaded package version '{package.Version}' does not match manifest version '{manifest.Version}'.");
        }
    }
}

public sealed record HpdDotNetPackageLoadResult(
    IHpdPackage Package,
    Assembly Assembly);

public sealed class HpdDotNetPackageLoadException : InvalidOperationException
{
    public HpdDotNetPackageLoadException(string message)
        : base(message)
    {
    }

    public HpdDotNetPackageLoadException(
        string message,
        Exception innerException)
        : base(message, innerException)
    {
    }
}

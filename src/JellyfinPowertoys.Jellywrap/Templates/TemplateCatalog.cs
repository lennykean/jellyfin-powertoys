using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;

namespace JellyfinPowertoys.Jellywrap.Templates;

public sealed class TemplateCatalog
{
    private const string ResourceRoot = "Templates/Builtin/";

    private TemplateCatalog(IReadOnlyList<Template> all)
    {
        All = all;
    }

    public IReadOnlyList<Template> All { get; }
    public static TemplateCatalog Load(string? dropInFolder)
    {
        var packages = LoadBuiltin();
        var unreadable = new List<Template>();
        if (dropInFolder is not null && Directory.Exists(dropInFolder))
        {
            foreach (var folder in Directory.GetDirectories(dropInFolder))
            {
                var root = Path.GetFullPath(folder);
                var package = new TemplatePackage(path => ReadFile(root, path));
                package.Load();
                if (package.Id is null)
                {
                    unreadable.Add(Unreadable(Path.GetFileName(folder), package));
                }
                else
                {
                    packages[package.Id] = package;
                }
            }
        }

        var templates = packages.Select(package => BuildTemplate(package.Key, package.Value, packages)).Concat(unreadable);
        return new TemplateCatalog([.. templates.OrderBy(template => template.Id, StringComparer.Ordinal)]);
    }

    public Template? Find(string? id, string kind, string audience)
    {
        return Pool(kind, audience).FirstOrDefault(template => template.Id == id);
    }

    public Template Pick(string kind, string audience, IReadOnlyCollection<Template> excluded)
    {
        var pool = Pool(kind, audience).Where(template => !excluded.Contains(template)).ToList();
        return pool.Count > 0
            ? pool[Random.Shared.Next(pool.Count)]
            : throw new InvalidOperationException($"No valid {audience} template has a {kind} video.");
    }

    private IEnumerable<Template> Pool(string kind, string audience)
    {
        return All.Where(template => template.IsValid && template.Audience == audience && template.Bases.ContainsKey(kind));
    }

    private static Dictionary<string, TemplatePackage> LoadBuiltin()
    {
        var assembly = typeof(TemplateCatalog).Assembly;
        var resources = assembly.GetManifestResourceNames()
            .Select(name => (Name: name, Path: name.Replace('\\', '/')))
            .Where(resource => resource.Path.StartsWith(ResourceRoot, StringComparison.Ordinal))
            .ToList();
        var packages = new Dictionary<string, TemplatePackage>();
        foreach (var folder in resources.GroupBy(resource => resource.Path[ResourceRoot.Length..].Split('/')[0]))
        {
            var files = folder.ToDictionary(resource => resource.Path[(ResourceRoot.Length + folder.Key.Length + 1)..], resource => resource.Name);
            var package = new TemplatePackage(path => files.TryGetValue(path, out var resource) ? ReadResource(assembly, resource) : null);
            package.Load();
            packages[package.Id ?? folder.Key] = package;
        }

        return packages;
    }

    private static byte[] ReadResource(Assembly assembly, string name)
    {
        using var stream = assembly.GetManifestResourceStream(name)!;
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    private static byte[]? ReadFile(string root, string path)
    {
        var fullPath = Path.GetFullPath(Path.Combine(root, path));
        var rootPrefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase) && File.Exists(fullPath) ? File.ReadAllBytes(fullPath) : null;
    }

    private static Template Unreadable(string id, TemplatePackage package)
    {
        return new Template(id, id, string.Empty, new Dictionary<string, JsonObject>(), new Dictionary<string, string>(), package.Errors, _ => null);
    }

    private static Template BuildTemplate(string id, TemplatePackage package, Dictionary<string, TemplatePackage> packages)
    {
        if (package.Json is not { } json)
        {
            return Unreadable(id, package);
        }

        var errors = new List<string>();
        var merged = new Dictionary<string, JsonObject>();
        var bases = new Dictionary<string, string>();
        string? Text(string property)
        {
            if (json[property] is { } node && !PropertyReader.IsString(node, out _))
            {
                errors.Add($"\"{property}\" must be a text, not {node.ToJsonString()}");
            }

            return PropertyReader.IsString(json[property], out var text) ? text : null;
        }

        var name = Text("name") ?? id;
        var audience = Text("audience") ?? string.Empty;
        try
        {
            var chain = ExtendsChain(id, package, packages);
            foreach (var (kind, baseName) in chain[^1].Json!["extends"] as JsonObject ?? [])
            {
                if (kind is not (Template.IndividualKind or Template.ServerKind))
                {
                    errors.Add($"\"extends\" names an unknown kind of video \"{kind}\"; the kinds are \"{Template.IndividualKind}\" and \"{Template.ServerKind}\"");
                    continue;
                }

                var baseId = PropertyReader.IsString(baseName, out var written) ? written : throw new InvalidDataException($"\"extends\" must name the base of the {kind} video.");
                var baseTemplate = packages.GetValueOrDefault(baseId)?.Json ?? throw new InvalidDataException($"The base \"{baseId}\" does not exist.");
                var result = TemplateMerge.OntoBase(baseTemplate, baseId, chain);
                merged[kind] = result;
                bases[kind] = baseId;
                errors.AddRange(TemplateValidator.Validate(result, key => ReadPackageFile(key, packages)).Select(error => $"{kind}: {error}"));
            }
        }
        catch (Exception exception)
        {
            errors.Add(exception.Message);
        }

        if (audience is not (Template.GeneralAudience or Template.KidsAudience or Template.BaseAudience))
        {
            errors.Add($"\"audience\" must be \"{Template.GeneralAudience}\", \"{Template.KidsAudience}\" or \"{Template.BaseAudience}\", not \"{audience}\"");
        }
        else if (audience != Template.BaseAudience && errors.Count == 0 && !bases.Keys.Any(kind => audience == Template.GeneralAudience || kind == Template.IndividualKind))
        {
            var kinds = audience == Template.GeneralAudience ? $"\"{Template.IndividualKind}\" or the \"{Template.ServerKind}\"" : $"\"{Template.IndividualKind}\"";
            errors.Add($"the template has no video that it is picked for: \"extends\" must name the base of the {kinds} video");
        }

        return new Template(
            id,
            name,
            audience,
            merged,
            bases,
            [.. errors.Distinct()],
            key => ReadPackageFile(key, packages));
    }

    private static List<TemplatePackage> ExtendsChain(string id, TemplatePackage package, Dictionary<string, TemplatePackage> packages)
    {
        var chain = new List<TemplatePackage> { package };
        while (PropertyReader.IsString(chain[^1].Json!["extends"], out var parent))
        {
            var next = packages.GetValueOrDefault(parent) ?? throw new InvalidDataException($"\"extends\" names an unknown template \"{parent}\".");
            if (chain.Contains(next) || next.Json is null)
            {
                throw new InvalidDataException($"\"extends\" of \"{id}\" loops or reaches an invalid template.");
            }

            chain.Add(next);
        }

        return chain;
    }

    private static byte[]? ReadPackageFile(string key, Dictionary<string, TemplatePackage> packages)
    {
        if (!key.StartsWith(Template.PackagePrefix, StringComparison.Ordinal))
        {
            return null;
        }

        var slash = key.IndexOf('/', StringComparison.Ordinal);
        return slash < 0 ? null : packages.GetValueOrDefault(key[Template.PackagePrefix.Length..slash])?.ReadFile(key[(slash + 1)..]);
    }
}

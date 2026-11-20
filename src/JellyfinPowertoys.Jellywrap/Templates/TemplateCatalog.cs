using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security;
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
        var builtin = packages.Values.ToHashSet();
        var rejected = new List<Template>();
        if (dropInFolder is not null && Directory.Exists(dropInFolder))
        {
            try
            {
                AddCustom(dropInFolder, packages, builtin, rejected);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or SecurityException)
            {
                rejected.Add(Rejected(dropInFolder, [$"The custom template folder cannot be read: {exception.Message}"]));
            }
        }

        var templates = packages.Select(package => BuildTemplate(package.Key, package.Value, packages, builtin)).Concat(rejected);
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

    private static void AddCustom(
        string dropInFolder,
        Dictionary<string, TemplatePackage> packages,
        HashSet<TemplatePackage> builtin,
        List<Template> rejected)
    {
        var folderOfId = new Dictionary<string, string>();
        foreach (var folder in Directory.GetDirectories(dropInFolder).Order(StringComparer.Ordinal))
        {
            var root = Path.GetFullPath(folder);
            if (!File.Exists(Path.Combine(root, TemplatePackage.TemplateFile)))
            {
                continue;
            }

            var name = Path.GetFileName(folder);
            var package = new TemplatePackage(path => ReadFile(root, path));
            package.Load();
            if (package.Id is null)
            {
                rejected.Add(Rejected(name, package.Errors));
            }
            else if (packages.TryGetValue(package.Id, out var existing) && builtin.Contains(existing))
            {
                rejected.Add(Rejected(name, [$"The id \"{package.Id}\" is the id of a built-in template; give the template another id."]));
            }
            else if (folderOfId.TryGetValue(package.Id, out var first))
            {
                rejected.Add(Rejected(name, [$"The id \"{package.Id}\" is also the id of the template in the folder \"{first}\"; only that template is used."]));
            }
            else
            {
                packages[package.Id] = package;
                folderOfId[package.Id] = name;
            }
        }
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
        return fullPath.StartsWith(rootPrefix, StringComparison.Ordinal) && File.Exists(fullPath) ? TemplatePackage.ReadAllBytes(fullPath) : null;
    }

    private static Template Rejected(string id, IReadOnlyList<string> errors)
    {
        return new Template(id, id, string.Empty, new Dictionary<string, JsonObject>(), new Dictionary<string, string>(), errors, _ => null);
    }

    private static Template BuildTemplate(
        string id,
        TemplatePackage package,
        Dictionary<string, TemplatePackage> packages,
        HashSet<TemplatePackage> builtin)
    {
        if (package.Json is not { } json)
        {
            return Rejected(id, package.Errors);
        }

        var errors = new List<string>();
        var merged = new Dictionary<string, JsonObject>();
        var bases = new Dictionary<string, string>();
        var readable = new HashSet<TemplatePackage>(builtin) { package };
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
            readable.UnionWith(chain);
            foreach (var (kind, baseName) in chain[^1].Json!["extends"] as JsonObject ?? [])
            {
                if (kind is not (Template.IndividualKind or Template.ServerKind))
                {
                    errors.Add($"\"extends\" names an unknown kind of video \"{kind}\"; the kinds are \"{Template.IndividualKind}\" and \"{Template.ServerKind}\"");
                    continue;
                }

                var baseId = PropertyReader.IsString(baseName, out var written) ? written : throw new InvalidDataException($"\"extends\" must name the base of the {kind} video.");
                var basePackage = packages.GetValueOrDefault(baseId);
                var baseTemplate = basePackage?.Json ?? throw new InvalidDataException($"The base \"{baseId}\" does not exist.");
                readable.Add(basePackage);
                var result = TemplateMerge.OntoBase(baseTemplate, baseId, chain);
                merged[kind] = result;
                bases[kind] = baseId;
                errors.AddRange(TemplateValidator.Validate(result, key => ReadPackageFile(key, packages, readable)).Select(error => $"{kind}: {error}"));
            }

            errors.AddRange(UnknownSlides(chain, bases, packages));
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
            key => ReadPackageFile(key, packages, readable));
    }

    private static IEnumerable<string> UnknownSlides(
        List<TemplatePackage> chain,
        Dictionary<string, string> bases,
        Dictionary<string, TemplatePackage> packages)
    {
        if (bases.Count == 0)
        {
            yield break;
        }

        var slidesOfBase = bases.Values.Distinct().ToDictionary(
            baseId => baseId,
            baseId => (packages[baseId].Json!["slides"] as JsonObject ?? []).Select(slide => slide.Key).ToList());
        var slidesOfAnyBase = slidesOfBase.Values.SelectMany(slides => slides).Distinct().ToList();
        foreach (var layer in chain.Select(package => package.Json!))
        {
            foreach (var (slide, _) in layer["slides"] as JsonObject ?? [])
            {
                if (!slidesOfAnyBase.Contains(slide))
                {
                    yield return $"\"slides\" names an unknown slide \"{slide}\"; the slides are {string.Join(", ", slidesOfAnyBase)}";
                }
            }

            foreach (var (baseId, overrides) in layer["byBase"] as JsonObject ?? [])
            {
                if (!slidesOfBase.TryGetValue(baseId, out var slides))
                {
                    continue;
                }

                foreach (var (slide, _) in (overrides as JsonObject)?["slides"] as JsonObject ?? [])
                {
                    if (!slides.Contains(slide))
                    {
                        yield return $"\"byBase.{baseId}.slides\" names an unknown slide \"{slide}\"; the slides are {string.Join(", ", slides)}";
                    }
                }
            }
        }
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

    private static byte[]? ReadPackageFile(string key, Dictionary<string, TemplatePackage> packages, HashSet<TemplatePackage> readable)
    {
        if (!key.StartsWith(Template.PackagePrefix, StringComparison.Ordinal))
        {
            return null;
        }

        var slash = key.IndexOf('/', StringComparison.Ordinal);
        var package = slash < 0 ? null : packages.GetValueOrDefault(key[Template.PackagePrefix.Length..slash]);
        return package is not null && readable.Contains(package) ? package.ReadFile(key[(slash + 1)..]) : null;
    }
}

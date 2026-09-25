#:property PublishAot=false
// Writes the CycloneDX SBOM of one published build:
//   dotnet run packaging/sbom.cs -- <rid> <version> <out.cdx.json>
// NuGet packages come from the publish deps.json, licences and hashes from the local
// package cache, models from Models.targets. Everything no manifest states - vendored
// code, native code inside packages, model licences - is declared in sbom.json.
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;

if (args.Length != 3) { Console.Error.WriteLine("sbom.cs <rid> <version> <out.cdx.json>"); return 2; }
var (rid, version, output) = (args[0], args[1].TrimStart('v'), args[2]);
var root = Path.GetFullPath(Path.Combine((string)AppContext.GetData("EntryPointFileDirectoryPath")!, ".."));
var declared = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "packaging", "sbom.json")))!.AsObject();
var obj = Path.Combine(root, "src", "Umsatzschaetzung.App", "obj");
var depsFile = Directory.GetFiles(Path.Combine(obj, "Release", "net10.0", rid), "*.deps.json").Single();
var appName = Path.GetFileName(depsFile)[..^".deps.json".Length];
var deps = JsonNode.Parse(File.ReadAllText(depsFile))!;
var cache = JsonNode.Parse(File.ReadAllText(Path.Combine(obj, "project.assets.json")))!["packageFolders"]!.AsObject().First().Key;
var target = deps["targets"]![$".NETCoreApp,Version=v10.0/{rid}"]!.AsObject();

var refs = new Dictionary<string, string>();
var components = new JsonArray();
foreach (var (key, library) in deps["libraries"]!.AsObject())
{
    var (name, ver) = (key[..key.IndexOf('/')], key[(key.IndexOf('/') + 1)..]);
    switch ((string)library!["type"]!)
    {
        case "project" when name == appName:
            refs[name] = "app";
            break;
        case "project":
            var project = Declared("projects", name);
            project["type"] ??= "library";
            project["bom-ref"] = refs[name] = $"project:{name}";
            project["name"] = name;
            project["version"] ??= version;
            components.Add(project);
            break;
        case "package" or "runtimepack":
            var package = Package(name.StartsWith("runtimepack.") ? name["runtimepack.".Length..] : name, ver, (string)library["sha512"]!);
            refs[name] = (string)package["bom-ref"]!;
            components.Add(package);
            break;
        default:
            throw new InvalidDataException($"{key}: library type {library["type"]} unknown");
    }
}

var models = Models();
foreach (var model in models) components.Add(model.DeepClone());

var dependencies = new JsonArray();
foreach (var (key, entry) in target)
{
    var dependsOn = new JsonArray();
    foreach (var (dependency, _) in entry!["dependencies"]?.AsObject() ?? [])
        dependsOn.Add(refs[dependency]);
    var self = refs[key[..key.IndexOf('/')]];
    if (self == "app")
        foreach (var model in models)
            dependsOn.Add((string)model["bom-ref"]!);
    dependencies.Add(new JsonObject { ["ref"] = self, ["dependsOn"] = dependsOn });
}

var application = declared["application"]!.DeepClone().AsObject();
application["bom-ref"] = "app";
application["version"] = version;
application["purl"] = $"{application["purl"]}@v{version}";
application["properties"] = new JsonArray(new JsonObject { ["name"] = "rid", ["value"] = rid });

var bom = new JsonObject
{
    ["$schema"] = "http://cyclonedx.org/schema/bom-1.6.schema.json",
    ["bomFormat"] = "CycloneDX",
    ["specVersion"] = "1.6",
    ["serialNumber"] = $"urn:uuid:{Guid.NewGuid()}",
    ["version"] = 1,
    ["metadata"] = new JsonObject
    {
        ["timestamp"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
        ["lifecycles"] = new JsonArray(new JsonObject { ["phase"] = "build" }),
        ["tools"] = new JsonObject { ["components"] = new JsonArray(new JsonObject { ["type"] = "application", ["name"] = "packaging/sbom.cs" }) },
        ["authors"] = new JsonArray(new JsonObject { ["name"] = application["supplier"]!["name"]!.DeepClone() }),
        ["component"] = application,
    },
    ["components"] = components,
    ["dependencies"] = dependencies,
};
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
File.WriteAllText(output, bom.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }) + "\n");
Console.WriteLine($"{output}: {components.Count} components");
return 0;

JsonObject Declared(string section, string name) =>
    declared[section]?[name]?.DeepClone().AsObject() ?? throw new InvalidDataException($"{name} is not declared under {section} in packaging/sbom.json");

JsonObject Package(string id, string ver, string sha512)
{
    var dir = Path.Combine(cache, id.ToLowerInvariant(), ver.ToLowerInvariant());
    var nuspec = XDocument.Load(Path.Combine(dir, $"{id.ToLowerInvariant()}.nuspec")).Root!.Elements().Single(e => e.Name.LocalName == "metadata");
    string? Field(string name) => nuspec.Elements().FirstOrDefault(e => e.Name.LocalName == name)?.Value;
    var license = nuspec.Elements().FirstOrDefault(e => e.Name.LocalName == "license" && (string?)e.Attribute("type") == "expression")?.Value;
    license = (string?)declared["licenses"]?[id] ?? license ?? throw new InvalidDataException($"{id} {ver} states no licence expression; declare it under licenses in packaging/sbom.json");
    if (sha512.Length == 0) sha512 = File.ReadAllText(Path.Combine(dir, $"{id.ToLowerInvariant()}.{ver.ToLowerInvariant()}.nupkg.sha512")).Trim();

    var purl = $"pkg:nuget/{id}@{ver}";
    var component = new JsonObject
    {
        ["type"] = "library",
        ["bom-ref"] = purl,
        ["name"] = id,
        ["version"] = ver,
        ["purl"] = purl,
        ["hashes"] = new JsonArray(new JsonObject { ["alg"] = "SHA-512", ["content"] = Convert.ToHexStringLower(Convert.FromBase64String(sha512.Replace("sha512-", ""))) }),
        ["licenses"] = new JsonArray(license.Contains(' ') ? new JsonObject { ["expression"] = license } : new JsonObject { ["license"] = new JsonObject { ["id"] = license } }),
    };
    if (Field("authors") is { } authors)
        component["authors"] = new JsonArray([.. authors.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Select(a => new JsonObject { ["name"] = a })]);
    var references = new JsonArray();
    if (nuspec.Elements().FirstOrDefault(e => e.Name.LocalName == "repository")?.Attribute("url")?.Value is { Length: > 0 } repository)
    {
        var vcs = new JsonObject { ["type"] = "vcs", ["url"] = repository };
        if (nuspec.Elements().First(e => e.Name.LocalName == "repository").Attribute("commit")?.Value is { } commit) vcs["comment"] = $"commit {commit}";
        references.Add(vcs);
    }
    if (Field("projectUrl") is { Length: > 0 } website) references.Add(new JsonObject { ["type"] = "website", ["url"] = website });
    references.Add(new JsonObject { ["type"] = "distribution", ["url"] = $"https://www.nuget.org/packages/{id}/{ver}" });
    component["externalReferences"] = references;

    var embedded = new JsonArray();
    foreach (var node in declared["embedded"]?[id]?.AsArray() ?? [])
    {
        var inner = node!.DeepClone().AsObject();
        if (inner.Remove("rids", out var rids) && !rids!.AsArray().Any(r => (string)r! == rid)) continue;
        inner["bom-ref"] = $"{purl}#{inner["name"]}";
        embedded.Add(inner);
    }
    if (embedded.Count > 0) component["components"] = embedded;
    return component;
}

List<JsonObject> Models()
{
    var project = XDocument.Load(Path.Combine(root, "Models.targets")).Root!;
    var properties = project.Elements("PropertyGroup").Elements().GroupBy(p => p.Name.LocalName).ToDictionary(g => g.Key, g => g.First().Value);
    string Expand(string value) => Regex.Replace(value, @"\$\((\w+)\)", m => properties[m.Groups[1].Value]);

    var files = new List<(string File, string Dir, string Release, string? Sha256, string? Url)>();
    foreach (var item in project.Elements("ItemGroup").Elements("ModelFile"))
    {
        if (item.Attribute("Include") is { } include)
            files.AddRange(include.Value.Split(';').Select(f => (f, item.Element("Dir")!.Value, item.Element("Release")!.Value, (string?)null, (string?)null)));
        else if (item.Attribute("Update") is { } update)
        {
            var i = files.FindIndex(f => f.File == update.Value);
            files[i] = files[i] with { Sha256 = (string?)item.Attribute("Sha256"), Url = item.Attribute("Url") is { } url ? Expand(url.Value) : null };
        }
    }

    return [.. files.GroupBy(f => f.Release).Select(release =>
    {
        var dir = release.First().Dir;
        var model = Declared("models", dir);
        model["type"] = "machine-learning-model";
        model["bom-ref"] = $"model:{release.Key}";
        model["version"] ??= Regex.Match(release.Key, @"-v?(\d[\w.-]*)$").Groups[1].Value;
        model["components"] = new JsonArray([.. release.Select(f => new JsonObject
        {
            ["type"] = "file",
            ["bom-ref"] = $"model:{release.Key}/{f.File}",
            ["name"] = $"models/{dir}/{f.File}",
            ["hashes"] = new JsonArray(new JsonObject { ["alg"] = "SHA-256", ["content"] = f.Sha256 ?? throw new InvalidDataException($"{f.File} has no Sha256 in Models.targets") }),
            ["externalReferences"] = new JsonArray(new JsonObject { ["type"] = "distribution", ["url"] = f.Url ?? $"{Expand(properties["ModelRepository"])}/{release.Key}/{f.File}" }),
        })]);
        return model;
    })];
}

#:package JsonSchema.Net@9.5.0

using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Json.Schema;

try
{
    Options options = Options.Parse(args);
    string root = Path.GetFullPath(options.Root);
    string output = Contained(root, options.Output);
    if (new[] { "apps", "assets", "schemas", "scripts", "recipes", "examples", ".github" }
        .Any(name => output.Equals(Path.Combine(root, name), StringComparison.OrdinalIgnoreCase) ||
            output.StartsWith(Path.Combine(root, name) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
        throw new InvalidDataException("Output cannot replace directory sources or tooling.");

    DateTimeOffset generated = DateTimeOffset.ParseExact(options.GeneratedAt, "yyyy-MM-ddTHH:mm:ssZ",
        CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
    if (options.Revision.Length is < 1 or > 100 || options.Revision.Any(char.IsControl))
        throw new InvalidDataException("The catalog revision must be a nonempty, bounded commit or local provenance label.");

    string schemaPath = Path.Combine(root, "schemas", "catalog.schema.json");
    RefuseExistingLinks(root, schemaPath);
    using JsonDocument schemaJson = ParseFile(schemaPath, 128 * 1024);
    JsonSchema schema = JsonSchema.Build(schemaJson.RootElement, new BuildOptions { Dialect = Dialect.Draft202012 });
    string appsPath = Path.Combine(root, "apps");
    string assetsPath = Path.Combine(root, "assets");
    if (!Directory.Exists(appsPath) || !Directory.Exists(assetsPath))
        throw new InvalidDataException("The apps and assets source directories are required.");
    RefuseExistingLinks(root, appsPath);
    RefuseExistingLinks(root, assetsPath);

    string approvalsPath = Path.Combine(root, "recipes", "approved.json");
    RefuseExistingLinks(root, approvalsPath);
    Dictionary<string, ApprovedRecipe> approved = ReadApprovedRecipes(approvalsPath);
    string[] files = Directory.GetFiles(appsPath, "*.json", SearchOption.TopDirectoryOnly);
    Array.Sort(files, (left, right) => StringComparer.Ordinal.Compare(Path.GetFileName(left), Path.GetFileName(right)));
    if (files.Length is 0 or > 2000) throw new InvalidDataException("The source directory needs 1–2000 app records.");
    if (Directory.EnumerateFileSystemEntries(appsPath).Count() != files.Length)
        throw new InvalidDataException("Only one JSON source file per app is permitted in apps.");

    List<JsonElement> apps = [];
    HashSet<string> ids = new(StringComparer.OrdinalIgnoreCase);
    HashSet<string> referencedAssets = new(StringComparer.OrdinalIgnoreCase);
    long assetBytes = 0;
    foreach (string file in files)
    {
        RefuseLink(file);
        using JsonDocument source = ParseFile(file, 128 * 1024);
        JsonElement app = source.RootElement;
        if (app.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException($"{Path.GetFileName(file)}: an app must be a JSON object.");
        RejectDuplicateProperties(app);
        string id = RequiredString(app, "id");
        if (!ids.Add(id) || !string.Equals(Path.GetFileNameWithoutExtension(file), id, StringComparison.Ordinal))
            throw new InvalidDataException($"{Path.GetFileName(file)}: the filename must match a unique permanent ID.");
        ValidateApp(app, root, id, approved, referencedAssets, ref assetBytes);
        apps.Add(app.Clone());
    }
    if (approved.Keys.Except(apps.Where(app => app.TryGetProperty("remix", out JsonElement remix) &&
            remix.ValueKind == JsonValueKind.Object).Select(app => app.GetProperty("id").GetString()!),
        StringComparer.OrdinalIgnoreCase).Any())
        throw new InvalidDataException("Recipe approvals must correspond to exact source records with remix metadata.");

    byte[] index = WriteIndex(options.Revision, generated, apps);
    if (index.Length > 2 * 1024 * 1024)
        throw new InvalidDataException("The complete catalog exceeds the client's 2 MiB limit.");
    using JsonDocument assembled = JsonDocument.Parse(index, new JsonDocumentOptions { MaxDepth = 32 });
    if (!schema.Evaluate(assembled.RootElement,
        new EvaluationOptions { RequireFormatValidation = true, OutputFormat = OutputFormat.Flag }).IsValid)
        throw new InvalidDataException("The generated index violates schemas/catalog.schema.json. Check required fields, URLs, and conditional channel/remix rules.");

    string license = Path.Combine(assetsPath, "LICENSE");
    string attribution = Path.Combine(assetsPath, "ATTRIBUTION.md");
    if (referencedAssets.Count > 0 && (!File.Exists(license) || !File.Exists(attribution) ||
        new FileInfo(license).Length is < 1 or > 65536 || new FileInfo(attribution).Length is < 1 or > 65536))
        throw new InvalidDataException("Artworks require the bounded assets/LICENSE and assets/ATTRIBUTION.md notices.");
    foreach (string source in EnumerateSafeFiles(assetsPath))
    {
        string relative = Path.GetRelativePath(root, source);
        if (!OperatingSystem.IsWindows() && relative.Contains('\\'))
            throw new InvalidDataException("Backslash-named assets cannot be published across platforms.");
        relative = relative.Replace(Path.DirectorySeparatorChar, '/');
        if (relative is not ("assets/LICENSE" or "assets/ATTRIBUTION.md") && !referencedAssets.Contains(relative))
            throw new InvalidDataException($"Unreviewed or unreferenced asset: {relative}.");
    }

    string? parent = Path.GetDirectoryName(output);
    RefuseExistingLinks(root, parent!);
    Directory.CreateDirectory(parent!);
    string staging = Path.Combine(parent!, $".{Path.GetFileName(output)}.{Guid.NewGuid():N}.building");
    string backup = Path.Combine(parent!, $".{Path.GetFileName(output)}.{Guid.NewGuid():N}.previous");
    try
    {
        Directory.CreateDirectory(staging);
        File.WriteAllBytes(Path.Combine(staging, "catalog.json"), index);
        File.Copy(schemaPath, Path.Combine(staging, "catalog.schema.json"));
        if (referencedAssets.Count > 0)
        {
            Directory.CreateDirectory(Path.Combine(staging, "assets"));
            File.Copy(license, Path.Combine(staging, "assets", "LICENSE"));
            File.Copy(attribution, Path.Combine(staging, "assets", "ATTRIBUTION.md"));
            foreach (string relative in referencedAssets.Order(StringComparer.Ordinal))
            {
                string destination = Contained(staging, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(Contained(root, relative), destination);
            }
        }

        if (Directory.Exists(output))
        {
            foreach (string _ in EnumerateSafeFiles(output)) { }
            Directory.Move(output, backup);
        }
        try { Directory.Move(staging, output); }
        catch
        {
            if (Directory.Exists(backup)) Directory.Move(backup, output);
            throw;
        }
        if (Directory.Exists(backup)) Directory.Delete(backup, recursive: true);
    }
    finally
    {
        if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
    }
    Console.WriteLine($"Validated {apps.Count} apps and {referencedAssets.Count} images; built {Path.GetRelativePath(root, output)}.");
}
catch (Exception error) when (error is InvalidDataException or JsonException or IOException or ArgumentException or FormatException)
{
    Console.Error.WriteLine($"Catalog build failed: {error.Message}");
    Environment.ExitCode = 1;
}

static void ValidateApp(JsonElement app, string root, string id, Dictionary<string, ApprovedRecipe> approved,
    HashSet<string> referencedAssets, ref long totalBytes)
{
    RequiredString(app, "displayName");
    RequiredString(app, "description");
    JsonElement verification = RequiredObject(app, "verification");
    RequiredString(verification, "notes");
    string checkedAtText = RequiredString(verification, "checkedAtUtc");
    if (!checkedAtText.EndsWith('Z') || !DateTimeOffset.TryParse(checkedAtText,
        CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset checkedAt) || checkedAt == default)
        throw new InvalidDataException($"{id}: verification time is invalid.");
    RequireHttps(RequiredString(verification, "sourceUrl"));
    JsonElement compatibility = RequiredObject(app, "compatibility");
    string[] windows = RequiredString(compatibility, "minimumWindowsVersion").Split('.');
    if (windows.Length != 4 || windows.Any(part => !ushort.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out _)))
        throw new InvalidDataException($"{id}: Windows version must have four unsigned 16-bit components.");
    if (RequiredArray(compatibility, "architectures").GetArrayLength() is < 1 or > 3)
        throw new InvalidDataException($"{id}: select at least one supported package architecture.");
    string status = RequiredString(app, "status");
    if (status is "deprecated" or "withdrawn" &&
        (!app.TryGetProperty("statusReason", out JsonElement reason) || reason.ValueKind != JsonValueKind.String ||
         string.IsNullOrWhiteSpace(reason.GetString())))
        throw new InvalidDataException($"{id}: inactive listings require a reason.");

    Uri? source = app.TryGetProperty("repositoryUrl", out JsonElement repository) &&
        repository.ValueKind == JsonValueKind.String ? RequireHttps(repository.GetString()!) : null;
    if (app.TryGetProperty("license", out JsonElement license) && license.ValueKind == JsonValueKind.Object)
    {
        RequireHttps(RequiredString(license, "fileUrl"));
        RequiredString(license, "attribution");
    }
    JsonElement channels = RequiredArray(app, "channels");
    HashSet<string> channelIds = new(StringComparer.OrdinalIgnoreCase);
    foreach (JsonElement channel in channels.EnumerateArray())
    {
        string channelId = RequiredString(channel, "id");
        if (!channelIds.Add(channelId)) throw new InvalidDataException($"{id}: duplicate channel ID {channelId}.");
        string kind = RequiredString(channel, "kind");
        if (kind == "gitHub")
        {
            if (!channel.TryGetProperty("identityVerified", out JsonElement verified) ||
                verified.ValueKind != JsonValueKind.True)
                throw new InvalidDataException($"{id}: a GitHub channel requires independently checked package identity.");
            JsonElement identity = RequiredObject(channel, "identity");
            if (!Regex.IsMatch(RequiredString(identity, "name"), "^[A-Za-z0-9][A-Za-z0-9.-]{2,49}$") ||
                !RequiredString(identity, "publisher").StartsWith("CN=", StringComparison.Ordinal))
                throw new InvalidDataException($"{id}: invalid Windows package identity.");
            Uri feed = RequireHttps(RequiredString(channel, "appInstallerUrl"));
            string[] segments = feed.AbsolutePath.TrimStart('/').Split('/');
            if (feed.Host != "github.com" || feed.Query.Length != 0 || segments.Length != 6 ||
                segments[2] != "releases" || segments[3] != "latest" || segments[4] != "download" ||
                !segments[5].EndsWith(".appinstaller", StringComparison.OrdinalIgnoreCase) ||
                segments.Any(part => part.Length == 0 || part.Contains('%') || part.Contains('\\')) ||
                source is null || source.Host != "github.com" ||
                !string.Equals(source.AbsolutePath.TrimEnd('/'),
                    "/" + segments[0] + "/" + segments[1], StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"{id}: GitHub feed must be a stable URL in the verified source repository.");
        }
        else if (kind == "store")
        {
            string productId = RequiredString(channel, "storeProductId");
            Uri store = RequireHttps(RequiredString(channel, "storeUrl"));
            if (store.Host is not ("apps.microsoft.com" or "www.microsoft.com") ||
                !string.Equals(store.Segments.Last().Trim('/'), productId, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"{id}: the Store URL must end with its product ID.");
        }
        else throw new InvalidDataException($"{id}: unsupported channel kind {kind}.");
    }

    if (app.TryGetProperty("remix", out JsonElement remix) && remix.ValueKind == JsonValueKind.Object)
    {
        string recipeId = RequiredString(remix, "recipeId");
        string version = RequiredString(remix, "recipeVersion");
        string revision = RequiredString(remix, "sourceRevision");
        string projectPath = RequiredString(remix, "projectPath");
        if (license.ValueKind != JsonValueKind.Object ||
            !license.TryGetProperty("redistributionVerified", out JsonElement redistribution) ||
            redistribution.ValueKind != JsonValueKind.True ||
            !license.TryGetProperty("remixVerified", out JsonElement remixRights) ||
            remixRights.ValueKind != JsonValueKind.True ||
            RequiredString(license, "spdxId") is not ("MIT" or "Apache-2.0" or "BSD-2-Clause" or "BSD-3-Clause" or "ISC") ||
            !Regex.IsMatch(revision, "^[a-fA-F0-9]{40}$") || !SafeRelative(projectPath) ||
            !approved.TryGetValue(id, out ApprovedRecipe? entry) ||
            (recipeId, version, revision, projectPath) != (entry.RecipeId, entry.RecipeVersion, entry.SourceRevision, entry.ProjectPath))
            throw new InvalidDataException($"{id}: remix metadata needs verified rights and an approved exact recipe/revision.");
    }
    if (app.TryGetProperty("icon", out JsonElement icon) && icon.ValueKind == JsonValueKind.Object)
        CheckAsset(icon, root, id, referencedAssets, ref totalBytes);
    if (app.TryGetProperty("screenshots", out JsonElement screenshots) && screenshots.ValueKind == JsonValueKind.Array)
        foreach (JsonElement screenshot in screenshots.EnumerateArray())
            CheckAsset(screenshot, root, id, referencedAssets, ref totalBytes);
}

static void CheckAsset(JsonElement reference, string root, string id,
    HashSet<string> referenced, ref long totalBytes)
{
    string relative = RequiredString(reference, "path");
    RequiredString(reference, "description");
    if (!SafeRelative(relative) || relative.Contains('\\') ||
        relative.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('/' or '.' or '-' or '_')) ||
        !relative.StartsWith($"assets/{id}/", StringComparison.Ordinal) ||
        Path.GetExtension(relative).ToLowerInvariant() is not (".png" or ".jpg" or ".jpeg" or ".webp" or ".ico"))
        throw new InvalidDataException($"{id}: images must be static raster files beneath assets/{id}/.");
    string path = Contained(root, relative);
    if (!File.Exists(path) || !referenced.Add(relative))
        throw new InvalidDataException($"{id}: a referenced image is missing or listed more than once: {relative}.");
    RefuseExistingLinks(root, path);
    byte[] bytes = File.ReadAllBytes(path);
    if (bytes.Length is < 12 or > 1024 * 1024 || !ValidImage(bytes, Path.GetExtension(relative)))
        throw new InvalidDataException($"{id}: an image is too large or does not match its static image type: {relative}.");
    totalBytes += bytes.Length;
    if (totalBytes > 16 * 1024 * 1024)
        throw new InvalidDataException("Published artwork exceeds the 16 MiB aggregate budget.");
}

static bool ValidImage(ReadOnlySpan<byte> bytes, string extension)
{
    extension = extension.ToLowerInvariant();
    if (extension == ".png")
        return bytes.Length >= 24 && bytes[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) &&
            bytes[12..16].SequenceEqual("IHDR"u8) &&
            BinaryPrimitives.ReadUInt32BigEndian(bytes[16..20]) is > 0 and <= 4096 &&
            BinaryPrimitives.ReadUInt32BigEndian(bytes[20..24]) is > 0 and <= 4096;
    if (extension is ".jpg" or ".jpeg")
        return bytes.Length >= 16 && bytes[..3].SequenceEqual(new byte[] { 255, 216, 255 }) &&
            bytes[^2..].SequenceEqual(new byte[] { 255, 217 });
    if (extension == ".webp")
        return bytes.Length >= 16 && bytes[..4].SequenceEqual("RIFF"u8) && bytes[8..12].SequenceEqual("WEBP"u8);
    return extension == ".ico" && bytes.Length >= 16 &&
        bytes[..4].SequenceEqual(new byte[] { 0, 0, 1, 0 }) && bytes[4] > 0;
}

static Dictionary<string, ApprovedRecipe> ReadApprovedRecipes(string path)
{
    using JsonDocument document = ParseFile(path, 128 * 1024);
    if (document.RootElement.ValueKind != JsonValueKind.Array)
        throw new InvalidDataException("recipes/approved.json must be an array of curator-approved recipes.");
    RejectDuplicateProperties(document.RootElement);
    Dictionary<string, ApprovedRecipe> recipes = new(StringComparer.OrdinalIgnoreCase);
    foreach (JsonElement item in document.RootElement.EnumerateArray())
    {
        string id = RequiredString(item, "appId");
        ApprovedRecipe recipe = new(RequiredString(item, "recipeId"), RequiredString(item, "recipeVersion"),
            RequiredString(item, "sourceRevision"), RequiredString(item, "projectPath"),
            RequireHttps(RequiredString(item, "kitReleaseUrl")));
        if (!Regex.IsMatch(recipe.SourceRevision, "^[a-fA-F0-9]{40}$") ||
            !SafeRelative(recipe.ProjectPath) || recipe.KitReleaseUrl.Host != "github.com" ||
            !Regex.IsMatch(recipe.KitReleaseUrl.AbsolutePath,
                "^/Tray-Land/tray-app-kit/releases/tag/[^/]+$", RegexOptions.IgnoreCase) ||
            !recipes.TryAdd(id, recipe))
            throw new InvalidDataException($"{id}: recipe approvals require a unique pinned source and kit release.");
    }
    return recipes;
}

static JsonDocument ParseFile(string path, int maxBytes)
{
    if (!File.Exists(path) || new FileInfo(path).Length is < 1 || new FileInfo(path).Length > maxBytes)
        throw new InvalidDataException($"{Path.GetFileName(path)} is missing, empty, or too large.");
    return JsonDocument.Parse(File.ReadAllBytes(path), new JsonDocumentOptions { MaxDepth = 32 });
}

static byte[] WriteIndex(string revision, DateTimeOffset generated, IReadOnlyList<JsonElement> apps)
{
    using MemoryStream stream = new();
    using (Utf8JsonWriter writer = new(stream, new JsonWriterOptions { Indented = true }))
    {
        writer.WriteStartObject();
        writer.WriteNumber("schemaVersion", 1);
        writer.WriteString("revision", revision);
        writer.WriteString("generatedAtUtc", generated.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
        writer.WritePropertyName("apps");
        writer.WriteStartArray();
        foreach (JsonElement app in apps) app.WriteTo(writer);
        writer.WriteEndArray();
        writer.WriteEndObject();
    }
    return stream.ToArray();
}

static Uri RequireHttps(string value)
{
    if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) ||
        uri.Scheme != Uri.UriSchemeHttps || uri.HostNameType != UriHostNameType.Dns ||
        uri.IsLoopback || !uri.IsDefaultPort || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0)
        throw new InvalidDataException("Catalog URLs must use public HTTPS without credentials, fragments, or custom ports.");
    return uri;
}

static string RequiredString(JsonElement value, string name)
{
    if (value.ValueKind != JsonValueKind.Object ||
        !value.TryGetProperty(name, out JsonElement property) || property.ValueKind != JsonValueKind.String ||
        string.IsNullOrWhiteSpace(property.GetString()) || property.GetString()!.Any(char.IsControl))
        throw new InvalidDataException($"Required text field {name} is missing or invalid.");
    return property.GetString()!;
}

static JsonElement RequiredObject(JsonElement parent, string name)
{
    if (parent.ValueKind != JsonValueKind.Object ||
        !parent.TryGetProperty(name, out JsonElement value) || value.ValueKind != JsonValueKind.Object)
        throw new InvalidDataException($"Required object field {name} is missing or invalid.");
    return value;
}

static JsonElement RequiredArray(JsonElement parent, string name)
{
    if (parent.ValueKind != JsonValueKind.Object ||
        !parent.TryGetProperty(name, out JsonElement value) || value.ValueKind != JsonValueKind.Array)
        throw new InvalidDataException($"Required array field {name} is missing or invalid.");
    return value;
}

static bool SafeRelative(string? path)
{
    if (string.IsNullOrWhiteSpace(path) || path.Length > 240 || path.StartsWith('/') || path.StartsWith('\\') ||
        path.Contains('%') || path.Contains(':') || path.Contains('~') || path.Any(char.IsControl))
        return false;
    return path.Split(['/', '\\']).All(part => part.Length > 0 && part is not ("." or "..") &&
        part == part.TrimEnd(' ', '.') && part.IndexOfAny(['*', '?', '"', '<', '>', '|']) < 0 &&
        !Regex.IsMatch(part, "^(CON|PRN|AUX|NUL|COM[0-9]|LPT[0-9])(?:\\.|$)", RegexOptions.IgnoreCase));
}

static string Contained(string root, string relative)
{
    string basePath = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
    string path = Path.GetFullPath(Path.Combine(basePath, relative.Replace('/', Path.DirectorySeparatorChar)));
    if (!path.StartsWith(basePath, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        throw new InvalidDataException("The requested output or asset path escapes the directory checkout.");
    return path;
}

static void RefuseLink(string path)
{
    if (File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
        throw new InvalidDataException($"Links are not accepted in directory sources: {Path.GetFileName(path)}.");
}

static void RefuseExistingLinks(string root, string path)
{
    string current = Path.GetFullPath(root);
    if (File.Exists(current) || Directory.Exists(current)) RefuseLink(current);
    string relative = Path.GetRelativePath(current, path);
    foreach (string part in relative.Split(Path.DirectorySeparatorChar))
    {
        current = Path.Combine(current, part);
        if (File.Exists(current) || Directory.Exists(current)) RefuseLink(current);
    }
}

static IEnumerable<string> EnumerateSafeFiles(string root)
{
    Stack<string> pending = new();
    pending.Push(root);
    while (pending.TryPop(out string? directory))
    {
        RefuseLink(directory);
        foreach (string entry in Directory.EnumerateFileSystemEntries(directory))
        {
            RefuseLink(entry);
            if (Directory.Exists(entry)) pending.Push(entry);
            else if (File.Exists(entry)) yield return entry;
            else throw new InvalidDataException("Directory assets contain an unsupported filesystem object.");
        }
    }
}

static void RejectDuplicateProperties(JsonElement element)
{
    if (element.ValueKind == JsonValueKind.Object)
    {
        HashSet<string> names = new(StringComparer.Ordinal);
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (!names.Add(property.Name)) throw new InvalidDataException($"Duplicate JSON property: {property.Name}.");
            RejectDuplicateProperties(property.Value);
        }
    }
    else if (element.ValueKind == JsonValueKind.Array)
        foreach (JsonElement item in element.EnumerateArray()) RejectDuplicateProperties(item);
}

sealed record ApprovedRecipe(string RecipeId, string RecipeVersion, string SourceRevision, string ProjectPath, Uri KitReleaseUrl);

sealed record Options(string Root, string Output, string Revision, string GeneratedAt)
{
    public static Options Parse(string[] args)
    {
        Dictionary<string, string> values = new(StringComparer.Ordinal);
        for (int i = 0; i < args.Length; i += 2)
        {
            if (i + 1 == args.Length || !args[i].StartsWith("--", StringComparison.Ordinal) ||
                !values.TryAdd(args[i], args[i + 1]))
                throw new ArgumentException("Expected --root, --output, --revision, and --generated-at values.");
        }
        if (values.Keys.Except(["--root", "--output", "--revision", "--generated-at"]).Any() ||
            !values.TryGetValue("--output", out string? output) ||
            !values.TryGetValue("--revision", out string? revision) ||
            !values.TryGetValue("--generated-at", out string? generatedAt))
            throw new ArgumentException("Usage: dotnet run --file scripts/BuildCatalog.cs -- --output _site/v1 --revision <commit> --generated-at <UTC YYYY-MM-DDTHH:MM:SSZ> [--root .]");
        return new(values.GetValueOrDefault("--root", Directory.GetCurrentDirectory()), output, revision, generatedAt);
    }
}

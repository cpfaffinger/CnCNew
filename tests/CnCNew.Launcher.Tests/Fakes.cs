using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using CnCNew.Launcher;

/// <summary>A GitHub stand-in: serves the sample release JSON and the assets from memory. No network is touched.</summary>
sealed class FakeServer : HttpMessageHandler
{
    public const string Base = "https://fake.test";
    private readonly Dictionary<string, Func<HttpResponseMessage>> _routes = new(StringComparer.Ordinal);
    public List<string> Requests { get; } = [];

    public void Serve(string path, byte[] body, HttpStatusCode status = HttpStatusCode.OK) =>
        _routes[Base + path] = () => new HttpResponseMessage(status) { Content = new ByteArrayContent(body) };

    public void ServeText(string path, string body, HttpStatusCode status = HttpStatusCode.OK) => Serve(path, Encoding.UTF8.GetBytes(body), status);

    public void Route(string path, Func<HttpResponseMessage> response) => _routes[Base + path] = response;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request.RequestUri!.ToString());
        if (!_routes.TryGetValue(request.RequestUri.ToString(), out var route)) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        return Task.FromResult(route());
    }
}

sealed class ThrowingHandler(Exception exception) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => throw exception;
}

/// <summary>A patcher that does what the real one does to the folder - it writes the content and the install record - and prints the lines the real one prints.</summary>
sealed class FakePatcher(int exitCode = 0, string[]? lines = null) : IToolRunner
{
    public List<string> Calls { get; } = [];

    public Task<ToolResult> RunAsync(string executable, IReadOnlyList<string> arguments, string workingDirectory, Action<string> onLine, CancellationToken cancellation)
    {
        Calls.Add(Path.GetFileName(executable) + " " + string.Join(' ', arguments));
        var output = lines ?? ["Found the game through Steam: C:\\fake\\steam", "check:  50% (5/10)", "patch: 100% (3/3)", "import: 100% (9/9)", "Installed: done"];
        foreach (var line in output) onLine(line);
        if (exitCode == 0 && arguments.Count >= 3 && arguments[0] == "install")
        {
            var target = arguments[2];
            Directory.CreateDirectory(Path.Combine(target, "content"));
            File.WriteAllText(Path.Combine(target, "content", "content-manifest.json"), "{}");
            File.WriteAllText(Path.Combine(target, "install.json"), "{}");
        }
        return Task.FromResult(new ToolResult(exitCode, output));
    }
}

static class Fixtures
{
    public static string Temp(string name) => Path.Combine(Path.GetTempPath(), $"cncnew-launcher-tests-{Guid.NewGuid():N}", name);

    public static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    /// <summary>A synthetic client package: three required files and a licence. Nothing of the game.</summary>
    public static byte[] ClientZip(string marker, string? rootFolder = null, params (string Name, string Text)[] extra)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Add(string name, string text)
            {
                var entry = zip.CreateEntry((rootFolder is null ? "" : rootFolder + "/") + name);
                using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
                writer.Write(text);
            }
            Add("bin/CnCNew.Client.Stride.exe", "client " + marker);
            Add("patcher/CnCNew.AssetTool.exe", "patcher " + marker);
            Add("Start-CnCNew.cmd", "@echo off");
            Add("LICENSE.md", "GPL " + marker);
            foreach (var (name, text) in extra) Add(name, text);
        }
        return buffer.ToArray();
    }

    public static string ReleaseJson(string version, bool withHash = true, bool withClient = true, bool withLauncher = true, bool prerelease = false)
    {
        var assets = new List<string>();
        if (withClient) assets.Add(Asset($"CnCNew-Client-{version}-win-x64.zip", $"/dl/{version}/client.zip", 1000));
        if (withClient && withHash) assets.Add(Asset($"CnCNew-Client-{version}-win-x64.zip.sha256", $"/dl/{version}/client.sha256", 100));
        if (withLauncher) assets.Add(Asset($"CnCNew-Launcher-{version}.exe", $"/dl/{version}/launcher.exe", 5000));
        if (withLauncher && withHash) assets.Add(Asset($"CnCNew-Launcher-{version}.exe.sha256", $"/dl/{version}/launcher.sha256", 100));
        return "{\"tag_name\":\"v" + version + "\",\"name\":\"CnCNew " + version + "\",\"body\":\"Notes of " + version + "\",\"prerelease\":" + (prerelease ? "true" : "false") +
               ",\"published_at\":\"2026-11-09T10:00:00Z\",\"html_url\":\"https://github.com/cpfaffinger/CnCNew/releases/tag/v" + version + "\",\"assets\":[" + string.Join(',', assets) + "]}";
    }

    private static string Asset(string name, string path, long size) =>
        "{\"name\":\"" + name + "\",\"size\":" + size + ",\"browser_download_url\":\"" + FakeServer.Base + path + "\"}";

    /// <summary>Publishes a release on the fake server; returns the client package bytes.</summary>
    public static byte[] Publish(FakeServer server, string version, string marker, bool corruptHash = false, byte[]? launcher = null)
    {
        var zip = ClientZip(marker);
        server.ServeText($"/repos/{ReleaseClient.DefaultRepository}/releases/latest", ReleaseJson(version));
        server.ServeText($"/repos/{ReleaseClient.DefaultRepository}/releases/tags/v{version}", ReleaseJson(version));
        server.Serve($"/dl/{version}/client.zip", zip);
        server.ServeText($"/dl/{version}/client.sha256", (corruptHash ? new string('0', 64) : Sha256(zip)) + $"  CnCNew-Client-{version}-win-x64.zip\n");
        var exe = launcher ?? Encoding.UTF8.GetBytes("launcher " + version);
        server.Serve($"/dl/{version}/launcher.exe", exe);
        server.ServeText($"/dl/{version}/launcher.sha256", Sha256(exe) + $"  CnCNew-Launcher-{version}.exe\n");
        return zip;
    }

    public static (LauncherEngine Engine, FakeServer Server, FakePatcher Patcher, string Data) Engine(FakePatcher? patcher = null, Func<InstallLayout, bool>? running = null)
    {
        var server = new FakeServer();
        var http = new HttpClient(server);
        var data = Temp("data");
        var tools = patcher ?? new FakePatcher();
        var engine = new LauncherEngine(http, tools, data, new ReleaseClient(http, apiBase: FakeServer.Base), running ?? (_ => false));
        return (engine, server, tools as FakePatcher ?? new FakePatcher(), data);
    }
}

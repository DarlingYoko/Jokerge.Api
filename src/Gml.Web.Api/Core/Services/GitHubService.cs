using System.Collections.Frozen;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO.Compression;
using GmlCore.Interfaces;
using Newtonsoft.Json.Linq;

namespace Gml.Web.Api.Core.Services;

public class GitHubService : IGitHubService
{
    private readonly IGmlManager _gmlManager;
    private readonly HttpClient _httpClient;
    private ICollection<string> _versions;
    private ICollection<string> _branches;

    public GitHubService(IHttpClientFactory httpClientFactory, IGmlManager gmlManager)
    {
        _gmlManager = gmlManager;
        _httpClient = httpClientFactory.CreateClient();
    }

    public async Task<IEnumerable<string>> GetRepositoryBranches(string user, string repository)
    {
        var url = $"https://api.github.com/repos/{user}/{repository}/branches";

        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("request");

        var response = await _httpClient.GetAsync(url);

        if (response.IsSuccessStatusCode)
        {
            var responseString = await response.Content.ReadAsStringAsync();

            var branches = JArray.Parse(responseString);

            _branches = branches.Select(c => c["name"].ToString()).ToArray();
        }

        return _branches;
    }

    public async Task<IEnumerable<string>> GetRepositoryTags(string user, string repository)
    {
        var url = "https://api.github.com/repos/Gml-Launcher/Gml.Launcher/tags";

        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("request");

        var response = await _httpClient.GetAsync(url);

        if (response.IsSuccessStatusCode)
        {
            var responseString = await response.Content.ReadAsStringAsync();

            var branches = JArray.Parse(responseString);

            _versions = branches.Select(c => c["name"].ToString()).ToArray();
        }

        return _versions;
    }

    public async Task<string> DownloadProject(string projectPath, string branchName, string repoUrl)
    {
        var directory = new DirectoryInfo(Path.Combine(projectPath, branchName));

        if (!directory.Exists)
        {
            directory.Create();
        }

        var processInfo = new ProcessStartInfo
        {
            FileName = "git",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        // ArgumentList passes each value through the process API untouched (no shell
        // involved), so branchName/repoUrl can't inject extra git flags or arguments.
        processInfo.ArgumentList.Add("clone");
        processInfo.ArgumentList.Add("--recursive");
        processInfo.ArgumentList.Add("--branch");
        processInfo.ArgumentList.Add(branchName);
        processInfo.ArgumentList.Add("--");
        processInfo.ArgumentList.Add(repoUrl);
        processInfo.ArgumentList.Add(directory.FullName);

        using var process = new Process();
        process.StartInfo = processInfo;
        process.Start();

        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"An error occurred while cloning the repository: {error}");
        }

        Console.WriteLine(output);

        return directory.FullName;
    }

    private string NormalizePath(string directory, string fileDirectory)
    {
        directory = directory
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);
        // .TrimStart(Path.DirectorySeparatorChar);

        fileDirectory = fileDirectory
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar)
            .TrimStart(Path.DirectorySeparatorChar);

        return Path.Combine(directory, fileDirectory);
    }

    public async Task EditLauncherFiles(string projectPath, string host, string folder)
    {
        var keys = new Dictionary<string, string>
        {
            { "{{HOST}}", host },
            { "{{FOLDER_NAME}}", folder }
        };

        var settingsTemplateFile =
            Path.Combine(projectPath, "src", "Gml.Launcher", "Assets", "Resources",
                "ResourceKeysDictionary.Template.cs");

        var settingsFile =
            Path.Combine(projectPath, "src", "Gml.Launcher", "Assets", "Resources", "ResourceKeysDictionary.cs");

        if (File.Exists(settingsFile))
            File.Delete(settingsFile);

        var content = await File.ReadAllTextAsync(settingsTemplateFile);

        foreach (var key in keys) content = content.Replace(key.Key, key.Value);

        await File.WriteAllTextAsync(settingsFile, content);
    }
}

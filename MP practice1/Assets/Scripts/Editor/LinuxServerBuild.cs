using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;

public static class LinuxServerBuild
{
    private const string DefaultBuildPath = "Builds/LinuxServer/MPPracticeServer.x86_64";

    public static void Build()
    {
        string outputPath = GetArgumentValue("-buildPath") ?? DefaultBuildPath;
        string outputDirectory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        BuildPlayerOptions options = new()
        {
            scenes = EditorBuildSettings.scenes
                .Where(scene => scene.enabled)
                .Select(scene => scene.path)
                .ToArray(),
            locationPathName = outputPath,
            target = BuildTarget.StandaloneLinux64,
            subtarget = (int)StandaloneBuildSubtarget.Server
        };

        BuildReport report = BuildPipeline.BuildPlayer(options);
        if (report.summary.result != BuildResult.Succeeded)
        {
            throw new Exception($"Linux server build failed: {report.summary.result}");
        }
    }

    private static string GetArgumentValue(string key)
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == key)
            {
                return args[i + 1];
            }
        }

        return null;
    }
}

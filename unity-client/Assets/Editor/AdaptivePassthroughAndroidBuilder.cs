using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class AdaptivePassthroughAndroidBuilder
{
    public const string ApkPath =
        "Builds/Android/AdaptivePassthrough.apk";

    [MenuItem("TeamVR/Adaptive Passthrough/Build Quest 3 APK")]
    public static void BuildQuest3Apk()
    {
        Build(BuildOptions.Development);
    }

    // Batch-mode entry point for CI or a local command-line build.
    public static void BuildQuest3ApkBatch()
    {
        Build(BuildOptions.Development);
    }

    private static void Build(BuildOptions options)
    {
        AdaptivePassthroughBoundarySetup.ConfigureForQuestBuild();

        string[] scenes = EditorBuildSettings.scenes
            .Where(
                scene =>
                    scene.enabled
                    && scene.path
                        == AdaptivePassthroughSceneBuilder.QuestScenePath)
            .Select(scene => scene.path)
            .ToArray();

        if (scenes.Length == 0)
        {
            throw new InvalidOperationException(
                "The Quest scene is not enabled in Editor Build Settings: "
                + AdaptivePassthroughSceneBuilder.QuestScenePath);
        }

        string outputDirectory = Path.GetDirectoryName(ApkPath);
        if (!string.IsNullOrEmpty(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        var buildOptions = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = ApkPath,
            target = BuildTarget.Android,
            options = options
        };

        Debug.Log(
            $"[AdaptivePassthrough] Building Quest 3 APK: {ApkPath}");
        BuildReport report = BuildPipeline.BuildPlayer(buildOptions);
        BuildSummary summary = report.summary;
        if (summary.result != BuildResult.Succeeded)
        {
            throw new InvalidOperationException(
                "Quest 3 APK build failed. "
                + $"Result={summary.result}, Errors={summary.totalErrors}, "
                + $"Warnings={summary.totalWarnings}");
        }

        Debug.Log(
            "[AdaptivePassthrough] Quest 3 APK build completed. "
            + $"Size={summary.totalSize} bytes, "
            + $"Duration={summary.totalTime}");
    }
}

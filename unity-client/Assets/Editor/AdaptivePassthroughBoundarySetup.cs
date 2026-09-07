using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

public static class AdaptivePassthroughBoundarySetup
{
    public const string FullBoundarylessFeature =
        "com.oculus.feature.BOUNDARYLESS_APP";
    public const string BoundaryVisibilityPermission =
        "com.oculus.permission.BOUNDARY_VISIBILITY";
    public const string ManifestAssetPath =
        "Assets/Plugins/Android/AndroidManifest.xml";

    [MenuItem(
        "TeamVR/Adaptive Passthrough/Configure Runtime Boundary Visibility")]
    public static void ConfigureForQuestBuild()
    {
        OVRProjectConfig config = OVRProjectConfig.CachedProjectConfig;
        config.allowOptional3DofHeadTracking = false;
        config.boundaryVisibilitySupport =
            OVRProjectConfig.FeatureSupport.Supported;
        if (config.insightPassthroughSupport
            == OVRProjectConfig.FeatureSupport.None)
        {
            config.insightPassthroughSupport =
                OVRProjectConfig.FeatureSupport.Supported;
        }

        EditorUtility.SetDirty(config);
        AssetDatabase.SaveAssets();

        // Meta manages the contextual Boundary API permission. Generate the
        // manifest first, then remove the full-app Boundaryless declaration:
        // rounds switch Guardian visibility through the runtime API instead.
        OVRManifestPreprocessor.GenerateOrUpdateAndroidManifest(true);
        RemoveFullBoundarylessManifestFeature();
        AssetDatabase.ImportAsset(ManifestAssetPath);

        Debug.Log(
            "[AdaptivePassthrough] Runtime Guardian visibility is enabled; "
            + "the forced full-app Boundaryless feature is absent.");
    }

    private static void RemoveFullBoundarylessManifestFeature()
    {
        string manifestPath = Path.Combine(
            Application.dataPath,
            "Plugins",
            "Android",
            "AndroidManifest.xml");
        if (!File.Exists(manifestPath))
        {
            throw new FileNotFoundException(
                "Meta XR did not generate AndroidManifest.xml.",
                manifestPath);
        }

        string manifest = File.ReadAllText(manifestPath);
        string updated = RemoveFullBoundarylessFeature(manifest);
        if (string.Equals(manifest, updated, StringComparison.Ordinal))
        {
            return;
        }

        File.WriteAllText(
            manifestPath,
            updated,
            new UTF8Encoding(false));
    }

    public static string RemoveFullBoundarylessFeature(string manifest)
    {
        if (string.IsNullOrEmpty(manifest))
        {
            return manifest ?? string.Empty;
        }

        string escapedFeature = Regex.Escape(FullBoundarylessFeature);
        string pattern =
            "^[ \\t]*<uses-feature\\b(?=[^>]*\\bandroid:name\\s*=\\s*[\"']"
            + escapedFeature
            + "[\"'])[^>]*/>[ \\t]*(?:\\r?\\n)?";
        return Regex.Replace(
            manifest,
            pattern,
            string.Empty,
            RegexOptions.Multiline | RegexOptions.CultureInvariant);
    }
}

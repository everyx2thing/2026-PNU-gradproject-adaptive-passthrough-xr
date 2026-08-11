using System;
using System.IO;
using System.Text;
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
        "TeamVR/Adaptive Passthrough/Configure Boundaryless Priority")]
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

        // Meta manages the contextual Boundary API permission. Generate it
        // first, then add the full-app feature that SDK 203 does not manage.
        OVRManifestPreprocessor.GenerateOrUpdateAndroidManifest(true);
        EnsureFullBoundarylessManifestFeature();
        AssetDatabase.ImportAsset(ManifestAssetPath);

        Debug.Log(
            "[AdaptivePassthrough] Full Boundaryless is the primary mode; "
            + "contextual boundary suppression is configured as fallback.");
    }

    private static void EnsureFullBoundarylessManifestFeature()
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
        if (manifest.IndexOf(
                FullBoundarylessFeature,
                StringComparison.Ordinal) >= 0)
        {
            return;
        }

        int closingTag = manifest.LastIndexOf(
            "</manifest>",
            StringComparison.Ordinal);
        if (closingTag < 0)
        {
            throw new InvalidDataException(
                "AndroidManifest.xml has no closing manifest tag.");
        }

        string newline = manifest.Contains("\r\n") ? "\r\n" : "\n";
        string feature =
            "  <uses-feature android:name=\""
            + FullBoundarylessFeature
            + "\" android:required=\"true\" />"
            + newline;
        manifest = manifest.Insert(closingTag, feature);
        File.WriteAllText(
            manifestPath,
            manifest,
            new UTF8Encoding(false));
    }
}

// MetaQuestVRSetup.cs
// Simplified Unity setup for Meta Quest VR development.
//
// Usage:
//   1. Copy this file into your Unity project at: Assets/Editor/MetaQuestVRSetup.cs
//   2. In Unity, click: Meta > Simplified VR Setup
//   3. Click "Run Setup" and wait for Unity to reload.
//
// What it does:
//   - Validates Unity version (2022.3.15f1+ required, 6.1+ recommended)
//   - Checks Android Build Support is installed (prompts to install via Unity Hub if missing)
//   - Switches build target to Meta Quest via Build Profiles (Unity 6.2+), BuildTarget enum (Unity 6.1), or Android (older)
//   - Installs XR Plugin Management, Unity OpenXR Plugin, Meta XR Core SDK
//   - Configures OpenXR with Meta Quest feature, Hand Tracking, and controller profiles
//   - Sets up VR scene (removes Main Camera, adds OVRCameraRig) — prompts before modifying
//   - Runs Meta Project Setup Tool "Fix All" for Android + Standalone (two passes each)
//   - Offers optional SDK packages (Interaction SDK, MRUK, Haptics, Audio, Platform)
//
// Architecture note:
//   Platform switching and package installation can trigger Unity domain reloads,
//   which destroy all static state. This script uses SessionState to persist
//   progress across reloads and [InitializeOnLoad] to auto-resume. EditorPrefs
//   provides a second persistence layer for full editor restarts (e.g. Input
//   System pre-configure on 2022.3.x, or graphics API changes on Windows).
//
// Distribution:
//   Export as a .unitypackage (right-click Editor folder > Export Package)
//   and share with developers. They import it and click one menu item.

using UnityEngine;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using System.IO;
using System.Collections.Generic;

// [InitializeOnLoad] tells Unity to call this class's static constructor after every
// domain reload (script recompilation). We use this to auto-resume setup if a platform
// switch or package install caused Unity to reload mid-setup.
[InitializeOnLoad]
public class MetaQuestVRSetup : EditorWindow
{
    private const string SCRIPT_VERSION = "1.0.0";

    private static AddRequest _currentRequest;
    private static int _stepIndex = 0;
    private static string _statusMessage = "Ready to configure.";
    // Derived from SessionState — no separate static bool that can drift on domain reload.
    private static bool _isRunning
    {
        get => SessionState.GetInt(KEY_PHASE, PHASE_IDLE) != PHASE_IDLE;
    }
    private static List<string> _log = new List<string>();
    private static Vector2 _scrollPos;
    private static bool _logLoaded = false;

    // Cached Core SDK detection — refreshed automatically on domain reload
    // (static fields reset). Used to conditionally show "Install Optional SDKs".
    private static bool _coreSDKInstalled = false;
    private static bool _coreSDKChecked = false;

    // Path to persistent log file (survives domain reloads unlike Console).
    // Written to Library/ so it's ignored by version control.
    private static readonly string _logFilePath = Path.Combine(
        Application.dataPath, "..", "Library", "MetaQuestSetup_Log.txt");

    // Path to the deferred config script generated at runtime.
    // Used to detect whether the deferred script is active (Project Setup Tool in progress).
    private static readonly string _deferredScriptPath = Path.Combine(
        Application.dataPath, "Editor", "MetaQuestXRConfig_AutoRun.cs");

    // SessionState keys for persisting setup progress across domain reloads.
    // SessionState survives reloads but resets when Unity is closed — unlike
    // EditorPrefs, it won't leave stale state between editor sessions.
    private const string KEY_PHASE = "MetaQuestVRSetup_Phase";
    private const string KEY_PKG_INDEX = "MetaQuestVRSetup_PkgIndex";
    private const string KEY_CLOSE_PKG_MGR = "MetaQuestVRSetup_ClosePkgMgr";
    private const string KEY_REACTIVATE_PROFILE = "MetaQuestVRSetup_ReactivateProfile";

    // EditorPrefs keys — survive editor restarts (unlike SessionState).
    // Used on 2022.3.x where Input System pre-configure triggers a full restart.
    // Prefixed with a project-specific hash so EditorPrefs from one project
    // don't interfere with another (EditorPrefs are machine-global).
    internal static readonly string PROJECT_HASH = StablePathHash(Application.dataPath);

    // Deterministic hash for EditorPrefs key prefix.
    // Cannot use string.GetHashCode() — it's randomized per process on CoreCLR (Unity 6+),
    // so EditorPrefs keys would change after every editor restart.
    private static string StablePathHash(string s)
    {
        unchecked
        {
            int hash = 5381;
            for (int i = 0; i < s.Length; i++)
                hash = hash * 33 + s[i];
            return (hash & 0x7FFFFFFF).ToString("X");
        }
    }
    private static readonly string KEY_EP_PHASE =
        "MetaQuestVRSetup_" + PROJECT_HASH + "_EP_Phase";
    private static readonly string KEY_EP_PKG_INDEX =
        "MetaQuestVRSetup_" + PROJECT_HASH + "_EP_PkgIndex";
    private static readonly string KEY_EP_TIMESTAMP =
        "MetaQuestVRSetup_" + PROJECT_HASH + "_EP_Timestamp";
    private static readonly string KEY_EP_RESTART_COUNT =
        "MetaQuestVRSetup_" + PROJECT_HASH + "_EP_RestartCount";

    // EditorPrefs safety limits
    private const int MAX_RESTART_COUNT = 3;           // Stop auto-resuming after 3 restarts
    private const double STALENESS_TIMEOUT_SECONDS = 3600; // 1 hour — don't resume stale setups

    // Setup phases (persisted via SessionState + EditorPrefs)
    private const int PHASE_IDLE = 0;
    private const int PHASE_RUNNING = 1;               // Setup in progress (pre-platform switch)
    private const int PHASE_POST_PLATFORM_SWITCH = 2;  // Resume: packages onward
    private const int PHASE_INSTALLING_PACKAGES = 3;   // Resume: package install at index
    private const int PHASE_POST_PACKAGES = 4;         // Resume: XR config
    private const int PHASE_PRE_INPUT_SYSTEM = 5;      // Resume: after Input System restart

    // Packages installed via UPM in order
    private static readonly string[] RequiredPackages = new string[]
    {
        "com.unity.xr.management",
        "com.unity.xr.openxr",
        "com.meta.xr.sdk.core"
    };

    // Human-readable names for UI
    private static readonly string[] PackageLabels = new string[]
    {
        "XR Plugin Management",
        "Unity OpenXR Plugin",
        "Meta XR Core SDK"
    };

    internal static readonly string KEY_SHOW_OPTIONAL_SDKS =
        "MetaQuestVRSetup_" + PROJECT_HASH + "_ShowOptionalSDKs";

    // ── Auto-Resume on Domain Reload ─────────────────────────────────

    static MetaQuestVRSetup()
    {
        // Called after every domain reload. Check if setup was in progress.
        EditorApplication.delayCall += CheckForResume;
        // Close Package Manager window if flagged from a previous reload.
        // Unity can re-open it during domain reloads triggered by package
        // install or script deletion, so we check on every reload.
        EditorApplication.delayCall += () =>
        {
            if (SessionState.GetInt(KEY_CLOSE_PKG_MGR, 0) == 1)
            {
                ClosePackageManagerWindow();
                SessionState.EraseInt(KEY_CLOSE_PKG_MGR);
            }
            // Auto-dismiss "Enable Meta XR Feature Set" dialogs from the SDK's
            // [InitializeOnLoad]. Runs here (main script) in addition to the
            // deferred script because the dialog can appear after Core SDK installs
            // — before the deferred script exists.
            DismissFeatureSetDialogs();
            // Show optional SDKs window if flagged by the deferred script.
            // EditorPrefs (not SessionState) so it survives editor restarts
            // triggered by graphics API changes on Windows.
            // SessionState flag tracks whether we've already shown the window
            // in this editor session. On the second domain reload, we clear
            // the EditorPrefs flag so the window doesn't keep re-appearing.
            // On editor restart, SessionState resets — so the window re-appears.
            if (EditorPrefs.GetBool(KEY_SHOW_OPTIONAL_SDKS, false))
            {
                if (SessionState.GetBool("MetaQuestVRSetup_OptionalSDKsShown", false))
                {
                    // Already shown in this session — clear the flag
                    EditorPrefs.DeleteKey(KEY_SHOW_OPTIONAL_SDKS);
                }
                else
                {
                    SessionState.SetBool("MetaQuestVRSetup_OptionalSDKsShown", true);
                    MetaQuestOptionalSDKs.ShowWindow();
                    EditorApplication.delayCall += () =>
                    {
                        var windows = Resources.FindObjectsOfTypeAll<MetaQuestVRSetup>();
                        foreach (var w in windows) w.Close();
                    };
                }
            }
            if (SessionState.GetInt(KEY_REACTIVATE_PROFILE, 0) == 1)
            {
                SessionState.EraseInt(KEY_REACTIVATE_PROFILE);
                var activeTarget = EditorUserBuildSettings.activeBuildTarget;
                bool isOnQuest = activeTarget.ToString().Contains("Android") ||
                                 activeTarget.ToString().Contains("Quest");
                if (!isOnQuest)
                {
                    Debug.LogWarning("[Meta Quest VR Setup] Build target reverted to " +
                        activeTarget + " after reload. Re-activating Meta Quest profile...");
                    ReactivateMetaQuestProfile();
                }
            }
        };
    }

    private static void CheckForResume()
    {
        int phase = SessionState.GetInt(KEY_PHASE, PHASE_IDLE);

        // SessionState is empty — check EditorPrefs (survives editor restarts)
        if (phase == PHASE_IDLE)
        {
            CheckEditorPrefsForResume(); // may populate SessionState and re-call us
            return;
        }

        Debug.Log("[Meta Quest VR Setup] Resuming setup after domain reload...");

        switch (phase)
        {
            case PHASE_RUNNING:
                // Domain reload during pre-platform-switch checks — stale, clear it.
                Log("Stale PHASE_RUNNING detected after reload — clearing.");
                ClearPhase();
                break;

            case PHASE_POST_PLATFORM_SWITCH:
                Log("Resumed after platform switch.");
                ContinueAfterPlatformSwitch();
                break;

            case PHASE_INSTALLING_PACKAGES:
                int pkgIdx = SessionState.GetInt(KEY_PKG_INDEX, 0);
                _stepIndex = pkgIdx;
                Log($"Resumed package installation from index {pkgIdx}.");
                InstallNextPackage();
                break;

            case PHASE_POST_PACKAGES:
                // Guard: if the deferred config script already exists on disk,
                // we're in a reload loop — clear phase and stop instead of
                // re-calling OnAllPackagesInstalled() which would rewrite it.
                if (File.Exists(_deferredScriptPath))
                {
                    Log("Deferred config script already exists — clearing stale phase to break reload loop.");
                    ClearPhase();
                    break;
                }
                Log("Resumed after package installation.");
                OnAllPackagesInstalled();
                break;

            case PHASE_PRE_INPUT_SYSTEM:
                Log("Resumed after Input System pre-configure restart.");
                ContinueWithPlatformSwitchAndPackages();
                break;

            default:
                ClearPhase();
                break;
        }
    }

    private static void SavePhase(int phase, int pkgIndex = 0)
    {
        SessionState.SetInt(KEY_PHASE, phase);
        SessionState.SetInt(KEY_PKG_INDEX, pkgIndex);
        // Mirror to EditorPrefs so state survives editor restarts (not just domain reloads)
        SavePhaseToEditorPrefs(phase, pkgIndex);
    }

    private static void ClearPhase()
    {
        SessionState.EraseInt(KEY_PHASE);
        SessionState.EraseInt(KEY_PKG_INDEX);
        ClearEditorPrefs();
    }

    // ── EditorPrefs Persistence (survives editor restarts) ──────────

    private static void SavePhaseToEditorPrefs(int phase, int pkgIndex)
    {
        EditorPrefs.SetInt(KEY_EP_PHASE, phase);
        EditorPrefs.SetInt(KEY_EP_PKG_INDEX, pkgIndex);
        // Store timestamp as epoch seconds for staleness detection
        double now = System.DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        EditorPrefs.SetString(KEY_EP_TIMESTAMP,
            now.ToString("F0", System.Globalization.CultureInfo.InvariantCulture));
    }

    private static void ClearEditorPrefs()
    {
        EditorPrefs.DeleteKey(KEY_EP_PHASE);
        EditorPrefs.DeleteKey(KEY_EP_PKG_INDEX);
        EditorPrefs.DeleteKey(KEY_EP_TIMESTAMP);
        EditorPrefs.DeleteKey(KEY_EP_RESTART_COUNT);
    }

    private static void CheckEditorPrefsForResume()
    {
        int epPhase = EditorPrefs.GetInt(KEY_EP_PHASE, PHASE_IDLE);
        if (epPhase == PHASE_IDLE)
            return; // Nothing to resume

        // Staleness check — don't resume setups older than the timeout
        string timestampStr = EditorPrefs.GetString(KEY_EP_TIMESTAMP, "");
        if (!string.IsNullOrEmpty(timestampStr) &&
            double.TryParse(timestampStr,
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture,
                out double savedTimestamp))
        {
            double now = System.DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            double age = now - savedTimestamp;
            if (age > STALENESS_TIMEOUT_SECONDS)
            {
                Debug.LogWarning($"[Meta Quest VR Setup] Setup state expired " +
                    $"({age / 60:F0} minutes old). Clearing stale EditorPrefs. " +
                    "Run setup again if needed.");
                ClearEditorPrefs();
                return;
            }
        }

        // Restart counter — stop after MAX_RESTART_COUNT to prevent loops
        int restartCount = EditorPrefs.GetInt(KEY_EP_RESTART_COUNT, 0);
        restartCount++;
        if (restartCount > MAX_RESTART_COUNT)
        {
            Debug.LogWarning($"[Meta Quest VR Setup] Exceeded max restart attempts " +
                $"({MAX_RESTART_COUNT}). Clearing EditorPrefs. " +
                "Click 'Reset (stuck?)' and run setup again.");
            ClearEditorPrefs();
            return;
        }
        EditorPrefs.SetInt(KEY_EP_RESTART_COUNT, restartCount);

        // Restore SessionState from EditorPrefs so existing resume logic works
        int epPkgIndex = EditorPrefs.GetInt(KEY_EP_PKG_INDEX, 0);
        SessionState.SetInt(KEY_PHASE, epPhase);
        SessionState.SetInt(KEY_PKG_INDEX, epPkgIndex);

        Debug.Log($"[Meta Quest VR Setup] Resuming setup after editor restart " +
            $"(attempt {restartCount}/{MAX_RESTART_COUNT}, phase {epPhase})...");

        // Re-invoke CheckForResume — SessionState now has the phase
        CheckForResume();
    }

    // ── Window UI ────────────────────────────────────────────────────

    [MenuItem("Meta/Simplified VR Setup")]
    public static void ShowWindow()
    {
        var window = GetWindow<MetaQuestVRSetup>("Meta Quest VR Setup");
        window.minSize = new Vector2(450, 400);
    }

    private void OnGUI()
    {
        // Reload log from file after domain reload (static _log is wiped)
        if (!_logLoaded && _log.Count == 0 && File.Exists(_logFilePath))
        {
            var lines = File.ReadAllLines(_logFilePath);
            if (lines.Length > 0)
            {
                _log = new List<string>(lines.Length);
                foreach (var line in lines)
                {
                    // Strip "[HH:mm:ss] " prefix written by Log() so UI format
                    // matches entries added in the current session.
                    if (line.Length > 11 && line[0] == '[' && line[9] == ']')
                        _log.Add(line.Substring(11));
                    else
                        _log.Add(line);
                }
            }
            _logLoaded = true;
        }

        GUILayout.Label("Meta Quest VR Setup", EditorStyles.boldLabel);
        GUILayout.Space(5);
        GUILayout.Label(
            "Automates the full \"Set up Unity for VR development\" guide.",
            EditorStyles.wordWrappedLabel);
        GUILayout.Space(10);

        // Checklist
        GUILayout.Label("This will:", EditorStyles.boldLabel);
        string[] steps = {
            "1. Check Unity version (2022.3.15f1+ required, 6.1+ recommended)",
            "2. Verify Android Build Support is installed (prompts to install via Hub if missing)",
            "3. Switch build target to Meta Quest (Build Profiles on 6.2+, enum on 6.1) or Android",
            "4. Install XR Plugin Management, Unity OpenXR Plugin, Meta XR Core SDK",
            "5. Configure OpenXR for Meta Quest (+ Hand Tracking, controller profiles)",
            "6. Set up VR scene (remove Main Camera, add OVRCameraRig) — you'll be asked first",
            "7. Run Project Setup Tool Fix All — Android + Standalone (two passes each)",
            "8. Offer optional SDK packages (Interaction SDK, MRUK, Haptics, Audio, Platform)"
        };
        foreach (var step in steps)
            GUILayout.Label("  " + step, EditorStyles.wordWrappedLabel);
        GUILayout.Space(3);
        GUILayout.Label(
            "  Note: If prompted about \"Meta XR Feature Set\" or \"Android Manifest\", click OK/Enable.",
            EditorStyles.miniLabel);

        GUILayout.Space(10);

        // Status
        EditorGUILayout.HelpBox(_statusMessage, MessageType.Info);

        // Log output
        if (_log.Count > 0)
        {
            GUILayout.Label("Log:", EditorStyles.boldLabel);
            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos, GUILayout.Height(100));
            foreach (var entry in _log)
                GUILayout.Label(entry, EditorStyles.miniLabel);
            EditorGUILayout.EndScrollView();
        }

        GUILayout.Space(10);

        // Disable button while setup is running OR deferred script is active
        // (deferred script runs Project Setup Tool after the finally block clears _isRunning).
        bool deferredScriptActive = File.Exists(_deferredScriptPath);
        bool setupBusy = _isRunning || deferredScriptActive;
        GUI.enabled = !setupBusy;
        string setupLabel = IsCoreSDKInstalled() ? "Run Setup Again" : "Run Setup";
        if (GUILayout.Button(setupLabel, GUILayout.Height(40)))
        {
            _log.Clear();
            _stepIndex = 0;
            ClearPhase();
            RunSetup();
        }
        GUI.enabled = true;

        // Safety escape hatch: if the button has been stuck (busy) due to a
        // stale SessionState phase, interrupted domain reload, or leftover
        // deferred script, let the user force-clear the state and try again.
        if (setupBusy)
        {
            GUILayout.Space(5);
            if (GUILayout.Button("Reset (stuck?)", GUILayout.Height(25)))
            {
                ClearPhase();
                if (deferredScriptActive)
                {
                    File.Delete(_deferredScriptPath);
                    string metaFile = _deferredScriptPath + ".meta";
                    if (File.Exists(metaFile))
                        File.Delete(metaFile);
                    string upstKey = "MetaQuestVRSetup_" + PROJECT_HASH + "_UPST_Running";
                    EditorPrefs.DeleteKey(upstKey);
                    AssetDatabase.Refresh();
                    Log("Deleted in-progress deferred config script.");
                }
                _statusMessage = "Reset complete. You can run setup again.";
                Log("Manual reset — cleared stuck state.");
            }
        }

        // Show "Install Optional SDKs" when Core SDK is installed and setup isn't running.
        if (!setupBusy && IsCoreSDKInstalled())
        {
            GUILayout.Space(10);
            if (GUILayout.Button("Install Optional SDKs", GUILayout.Height(30)))
            {
                MetaQuestOptionalSDKs.ShowWindow();
            }
        }
    }

    private static bool IsCoreSDKInstalled()
    {
        if (!_coreSDKChecked)
        {
            string manifestPath = Path.Combine(
                Application.dataPath, "..", "Packages", "manifest.json");
            _coreSDKInstalled = File.Exists(manifestPath) &&
                File.ReadAllText(manifestPath).Contains("com.meta.xr.sdk.core");
            _coreSDKChecked = true;
        }
        return _coreSDKInstalled;
    }

    // ── Main Setup Flow ──────────────────────────────────────────────

    private static void RunSetup()
    {
        // Guard: prevent double-invocation while setup is running.
        if (_isRunning)
        {
            Log("Setup already in progress.");
            return;
        }

        // _isRunning is derived from this phase.
        SavePhase(PHASE_RUNNING);

        // Reset the "already shown" flag so the optional SDKs window appears after this run
        SessionState.EraseBool("MetaQuestVRSetup_OptionalSDKsShown");

        // Clear persistent log file and in-memory log for this run
        _log.Clear();
        _logLoaded = true; // prevent reloading stale data
        try { File.WriteAllText(_logFilePath, ""); } catch { }

        Log($"Starting Meta Quest VR setup (v{SCRIPT_VERSION})...");

        // Clean up any leftover deferred script from a previous failed run.
        // If it has a compile error, Unity won't compile anything until it's removed.
        if (File.Exists(_deferredScriptPath))
        {
            File.Delete(_deferredScriptPath);
            string metaFile = _deferredScriptPath + ".meta";
            if (File.Exists(metaFile))
                File.Delete(metaFile);
            Log("Removed leftover deferred config script from previous run.");
            AssetDatabase.Refresh();
        }

        // ── Step 0a: Validate Unity version ────────────────────────
        if (!CheckUnityVersion())
        {
            _statusMessage = "Unity version too old. See log for details.";
            ClearPhase();
            return;
        }

        // ── Step 0b: Check Android Build Support ───────────────────
        if (!IsAndroidBuildSupportInstalled())
        {
            Log("Android Build Support not installed.");
            _statusMessage = "Action required: Install Android Build Support manually.";
            ClearPhase();

            EditorUtility.DisplayDialog(
                "Android Build Support Required",
                "Android Build Support (with SDK, NDK, and OpenJDK) is not installed.\n\n" +
                "To install:\n" +
                "1. Open Unity Hub\n" +
                "2. Go to Installs\n" +
                "3. Click the gear icon on Unity " + Application.unityVersion + "\n" +
                "4. Select \"Add Modules\"\n" +
                "5. Check \"Android Build Support\" (including SDK & NDK Tools and OpenJDK)\n" +
                "6. Click Install\n" +
                "7. Restart Unity and run this setup again.",
                "OK");
            return;
        }
        else
        {
            Log("Android Build Support: OK");
        }

        // ── Step 0c: Pre-configure Input System ─────────────────────
        // Must happen BEFORE package installation. On 2022.3.x, OpenXR pulls
        // in Input System, which shows a dialog that restarts the editor.
        // Pre-setting to "Both" avoids the dialog. No-op on Unity 6.
        PreConfigureInputSystem();
        // If the setter triggered an immediate editor restart, we never reach
        // the next line — EditorPrefs has PHASE_PRE_INPUT_SYSTEM and will resume.
        // If no restart happened (common on many Unity versions), continue normally.

        // ── Step 1+: Platform switch and packages ───────────────────
        ContinueWithPlatformSwitchAndPackages();
    }

    /// <summary>
    /// Pre-configure Input System to "Both" (Old + New) before installing packages.
    /// On 2022.3.x, activeInputHandler defaults to 0 (Old). When OpenXR installs and
    /// pulls in Input System, Unity shows a modal dialog that triggers a full editor
    /// restart — killing SessionState. Pre-setting to "Both" prevents the dialog.
    /// On Unity 6, activeInputHandler already defaults to 2 — this is a no-op.
    ///
    /// Does not return a value — if the setter triggers an immediate restart, execution
    /// never returns. If no restart, the caller continues to platform switch + packages.
    /// SavePhase is called BEFORE the setter as a safety net for the restart case.
    /// </summary>
    private static void PreConfigureInputSystem()
    {
        try
        {
            // PlayerSettings.activeInputHandler: 0=Old, 1=New, 2=Both
            var prop = typeof(PlayerSettings).GetProperty("activeInputHandler",
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Static);

            if (prop == null)
            {
                // Property may not exist on Unity 6+ (defaults to "Both" anyway) or very old versions.
                Log("Input System: activeInputHandler property not found. Skipping pre-configure (Unity 6 defaults to 'Both').");
                return;
            }

            int current = (int)prop.GetValue(null);

            if (current == 2)
            {
                Log("Input System: already set to 'Both'. No change needed.");
                return;
            }

            Log($"Input System: activeInputHandler is {current}, setting to 2 (Both)...");

            // CRITICAL: Save phase BEFORE setting activeInputHandler.
            // The setter can trigger an immediate synchronous restart on some
            // Unity versions — if SavePhase runs after, it never executes.
            SavePhase(PHASE_PRE_INPUT_SYSTEM);

            prop.SetValue(null, 2);

            // If we reach here, the setter did NOT trigger an immediate restart.
            // Continue normally — the phase will be overwritten by subsequent
            // SavePhase calls in the platform switch / package install flow.
            Log("Input System set to 'Both'. No immediate restart — continuing.");
        }
        catch (System.Exception ex)
        {
            Log($"Input System pre-configure failed: {ex.Message}. Continuing without it.");
        }
    }

    /// <summary>
    /// Platform switch + package installation. Extracted from RunSetup() so it can be
    /// called both from RunSetup() (normal flow) and from CheckForResume() after an
    /// Input System restart (PHASE_PRE_INPUT_SYSTEM).
    /// </summary>
    private static void ContinueWithPlatformSwitchAndPackages()
    {
        // ── Step 1: Switch platform ──────────────────────────────────
        _statusMessage = "Switching build platform...";
        bool platformSwitchNeeded = false;

        if (IsUnity6OrLater())
        {
            Log("Unity 6+ detected — checking for Meta Quest build target...");

            bool usedMetaQuest = false;
            try
            {
                // Find the Meta Quest BuildTarget enum value.
                // The exact name varies across Unity versions (e.g. "MetaQuest",
                // "Quest", etc.), so search all enum fields for a match.
                var metaQuestTargetField = typeof(BuildTarget).GetField("MetaQuest");
                if (metaQuestTargetField == null)
                {
                    foreach (var field in typeof(BuildTarget).GetFields(
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
                    {
                        string name = field.Name;
                        if (name.IndexOf("Quest", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                            name == "MetaQuest")
                        {
                            metaQuestTargetField = field;
                            Log($"Found Quest build target enum: {name}");
                            break;
                        }
                    }
                }

                if (metaQuestTargetField != null)
                {
                    var metaQuestTarget = (BuildTarget)metaQuestTargetField.GetValue(null);

                    // Also resolve the matching BuildTargetGroup via reflection.
                    // Search the same way — name may vary across Unity versions.
                    var metaQuestGroupField = typeof(BuildTargetGroup).GetField("MetaQuest");
                    if (metaQuestGroupField == null)
                    {
                        foreach (var field in typeof(BuildTargetGroup).GetFields(
                            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
                        {
                            string name = field.Name;
                            if (name.IndexOf("Quest", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                                name == "MetaQuest")
                            {
                                metaQuestGroupField = field;
                                Log($"Found Quest build target group enum: {name}");
                                break;
                            }
                        }
                    }
                    BuildTargetGroup metaQuestGroup = metaQuestGroupField != null
                        ? (BuildTargetGroup)metaQuestGroupField.GetValue(null)
                        : BuildTargetGroup.Android;  // fallback for edge cases

                    Log($"Resolved: BuildTarget.MetaQuest={metaQuestTarget}, " +
                        $"BuildTargetGroup.MetaQuest={metaQuestGroup}");

                    // Check if the Meta Quest platform is actually installed/enabled
                    bool isSupported = BuildPipeline.IsBuildTargetSupported(
                        metaQuestGroup, metaQuestTarget);
                    Log($"Meta Quest platform supported: {isSupported}");

                    if (!isSupported)
                    {
                        Log("Meta Quest platform is not enabled. Prompting user...");
                        bool useAndroid = EditorUtility.DisplayDialog(
                            "Meta Quest Platform Not Enabled",
                            "The Meta Quest build target exists in this Unity version " +
                            "but the platform profile is not enabled.\n\n" +
                            "To enable it:\n" +
                            "1. Go to File > Build Profiles\n" +
                            "2. Select Meta Quest\n" +
                            "3. Click 'Enable Platform'\n" +
                            "4. Wait for installation to complete\n" +
                            "5. Run this setup again\n\n" +
                            "Or click 'Use Android' to continue with the Android " +
                            "build target instead (works but less optimal).",
                            "Use Android", "Cancel Setup");

                        if (useAndroid)
                        {
                            Log("User chose Android fallback.");
                        }
                        else
                        {
                            Log("User cancelled setup to enable Meta Quest platform first.");
                            _statusMessage = "Setup cancelled. Enable Meta Quest platform in Build Profiles, then try again.";
                            ClearPhase();
                            return;
                        }
                    }
                    else if (EditorUserBuildSettings.activeBuildTarget == metaQuestTarget)
                    {
                        Log("Already on Meta Quest platform.");
                        usedMetaQuest = true;
                    }
                    else
                    {
                        // Platform switch will trigger domain reload.
                        // Save state so we can resume after reload.
                        SavePhase(PHASE_POST_PLATFORM_SWITCH);
                        Log($"Switching to Meta Quest platform...");

                        bool switched = EditorUserBuildSettings.SwitchActiveBuildTarget(
                            metaQuestGroup, metaQuestTarget);
                        if (switched)
                        {
                            Log("Build target switched to Meta Quest.");
                            usedMetaQuest = true;
                            platformSwitchNeeded = true;
                        }
                        else
                        {
                            Log("WARNING: Meta Quest platform switch returned false " +
                                "despite being reported as supported. Falling back to Android.");
                            ClearPhase();
                        }
                    }
                }
                else
                {
                    // Unity 6.2+ does not have a MetaQuest BuildTarget enum.
                    // Meta Quest is handled via the Build Profiles system instead.
                    // Try to activate a Meta Quest Build Profile via reflection.
                    Log("No Quest BuildTarget enum found. Trying Build Profiles API (Unity 6.2+)...");
                    int profileResult = TrySwitchViaBuildProfile();
                    if (profileResult >= 0)
                    {
                        usedMetaQuest = true;
                        if (profileResult == 1)
                            platformSwitchNeeded = true;
                        // profileResult == 0 means already on Meta Quest — no switch needed
                    }
                    else
                    {
                        Log("Build Profiles API did not succeed. Using Android as fallback.");
                    }
                }
            }
            catch (System.Exception ex)
            {
                Log($"Meta Quest platform not available ({ex.GetType().Name}: {ex.Message}), using Android.");
                ClearPhase();
            }

            if (!usedMetaQuest)
            {
                platformSwitchNeeded = SwitchToAndroidWithResumeSupport();
            }
        }
        else
        {
            Log("Switching build target to Android...");
            platformSwitchNeeded = SwitchToAndroidWithResumeSupport();
        }

        // If a platform switch was initiated, Unity will domain-reload.
        // The [InitializeOnLoad] constructor will resume from PHASE_POST_PLATFORM_SWITCH.
        if (platformSwitchNeeded)
        {
            _statusMessage = "Platform switch in progress — Unity is reloading. Setup will resume automatically.";
            Log("Waiting for Unity to reload after platform switch...");
            return;
        }

        // ── No reload needed — continue immediately ─────────────────
        ContinueAfterPlatformSwitch();
    }

    private static void ContinueAfterPlatformSwitch()
    {
        // ── Step 2a: Install packages sequentially ─────────────────────
        SavePhase(PHASE_INSTALLING_PACKAGES, 0);
        SessionState.SetInt(KEY_CLOSE_PKG_MGR, 1); // Close Package Manager after all reloads
        _stepIndex = 0;
        InstallNextPackage();
    }

    // ── Version Check ────────────────────────────────────────────────

    private static bool CheckUnityVersion()
    {
        string version = Application.unityVersion;
        Log($"Unity version: {version}");

        // Parse version components once for all checks below
        string[] parts = version.Split('.', 'f', 'a', 'b', 'p');
        if (parts.Length > 0 && int.TryParse(parts[0], out int major))
        {
            // Unity 6+ uses version numbers like 6000.x, 7000.x — always valid
            if (major >= 6000)
            {
                Log("Unity 6+ detected — meets minimum requirements.");
                return true;
            }

            // Check major.minor.patch for 2022.x / 2023.x
            if (parts.Length >= 3 &&
                int.TryParse(parts[1], out int minor) &&
                int.TryParse(parts[2], out int patch))
            {
                if (major > 2022 || (major == 2022 && minor > 3) ||
                    (major == 2022 && minor == 3 && patch >= 15))
                {
                    Log("Unity version meets minimum requirements.");
                    return true;
                }
            }
        }

        Log($"WARNING: Unity {version} is below the minimum required version (2022.3.15f1).");
        Log("Meta Quest development requires Unity 2022.3.15f1 or later (6.1+ recommended).");

        EditorUtility.DisplayDialog(
            "Unity Version Too Old",
            $"Unity {version} does not meet the minimum requirement for Meta Quest development.\n\n" +
            "Required: Unity 2022.3.15f1 or later (6.1 or later recommended).\n\n" +
            "Please upgrade your Unity installation via Unity Hub.",
            "OK");
        return false;
    }

    private static bool IsUnity6OrLater()
    {
        // Unity 6+ uses version numbers like 6000.x, 7000.x, etc.
        // Numeric parse handles future versions (10000.x+) correctly.
        string v = Application.unityVersion;
        string[] parts = v.Split('.', 'f', 'a', 'b', 'p');
        if (parts.Length > 0 && int.TryParse(parts[0], out int major))
            return major >= 6000;
        return false;
    }

    // ── Android Build Support ────────────────────────────────────────

    private static bool IsAndroidBuildSupportInstalled()
    {
        return BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Android, BuildTarget.Android);
    }

    // ── Platform Switching ───────────────────────────────────────────

    /// <summary>
    /// Switches to Android and returns true if a domain reload is expected.
    /// </summary>
    private static bool SwitchToAndroidWithResumeSupport()
    {
        if (EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android)
        {
            Log("Already on Android platform.");
            return false;
        }

        // Save state before switch — domain reload will happen
        SavePhase(PHASE_POST_PLATFORM_SWITCH);
        Log("Switching to Android platform (Unity will reload)...");

        bool switched = EditorUserBuildSettings.SwitchActiveBuildTarget(
            BuildTargetGroup.Android, BuildTarget.Android);
        if (!switched)
        {
            Log("WARNING: Platform switch returned false. " +
                "Android Build Support may not be fully installed.");
            ClearPhase();
            return false;
        }

        Log("Build target switched to Android.");
        return true; // Domain reload expected
    }

    /// <summary>
    /// Unity 6.2+ uses Build Profiles instead of BuildTarget enum for Meta Quest.
    /// This method discovers and activates a Meta Quest build profile via reflection,
    /// writing diagnostic info to the log file for debugging.
    /// </summary>
    /// Returns: -1 = failed, 0 = already on Meta Quest, 1 = switch initiated (domain reload expected).
    private static int TrySwitchViaBuildProfile()
    {
        var flags = System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Static |
                    System.Reflection.BindingFlags.Instance;

        try
        {
            // Step 1: Find required types
            System.Type buildProfileType = null;
            System.Type moduleUtilType = null;
            System.Type contextType = null;
            foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = asm.GetType("UnityEditor.Build.Profile.BuildProfile");
                if (t != null) buildProfileType = t;
                t = asm.GetType("UnityEditor.Build.Profile.BuildProfileModuleUtil");
                if (t != null) moduleUtilType = t;
                t = asm.GetType("UnityEditor.Build.Profile.BuildProfileContext");
                if (t != null) contextType = t;
            }

            if (buildProfileType == null || moduleUtilType == null || contextType == null)
            {
                Log("Build Profile types not found. Cannot switch to Meta Quest profile.");
                return -1;
            }

            // Step 2: Get all viewable platforms
            var findAllMethod = moduleUtilType.GetMethod("FindAllViewablePlatforms", flags);
            if (findAllMethod == null)
            {
                Log("FindAllViewablePlatforms method not found.");
                return -1;
            }

            var platforms = findAllMethod.Invoke(null, null) as System.Collections.IList;
            if (platforms == null || platforms.Count == 0)
            {
                Log("No viewable platforms found.");
                return -1;
            }

            Log($"Found {platforms.Count} viewable platforms.");

            // Step 3: Find the Meta Quest platform GUID by checking display names
            var getDisplayNameMethod = moduleUtilType.GetMethod("GetClassicPlatformDisplayName", flags);

            // Discover the platform identifier type from FindAllViewablePlatforms's return type.
            // Unity 6.0-6.3 used UnityEditor.GUID; Unity 6.4+ may use a different type.
            System.Type guidType = null;
            var returnType = findAllMethod.ReturnType;
            if (returnType.IsGenericType)
                guidType = returnType.GetGenericArguments()[0];
            else if (returnType.IsArray)
                guidType = returnType.GetElementType();
            // Fallback: try the first element's runtime type
            if (guidType == null && platforms.Count > 0)
                guidType = platforms[0].GetType();

            System.Reflection.MethodInfo isSupportedMethod = null;
            if (guidType != null)
                isSupportedMethod = moduleUtilType.GetMethod("IsBuildProfileSupported",
                    flags, null, new[] { guidType }, null);

            object metaQuestGuid = null;

            foreach (var platform in platforms)
            {
                // Platform might be the identifier directly, or a wrapper with a guid property
                object platformGuid = platform;
                if (guidType != null && platform.GetType() != guidType)
                {
                    var guidProp = platform.GetType().GetProperty("guid", flags)
                        ?? platform.GetType().GetProperty("platformGuid", flags)
                        ?? platform.GetType().GetProperty("platformId", flags);
                    if (guidProp != null)
                        platformGuid = guidProp.GetValue(platform);
                    else
                    {
                        var guidField = platform.GetType().GetField("guid", flags)
                            ?? platform.GetType().GetField("platformGuid", flags);
                        if (guidField != null)
                            platformGuid = guidField.GetValue(platform);
                        else
                            continue;
                    }
                }

                // Get the display name for this platform
                string displayName = "";
                try
                {
                    if (getDisplayNameMethod != null)
                        displayName = getDisplayNameMethod.Invoke(null, new[] { platformGuid }) as string ?? "";
                }
                catch { }

                Log($"  Platform: {displayName} (GUID: {platformGuid})");

                if (displayName.IndexOf("Quest", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    displayName == "Meta Quest" || displayName == "MetaQuest")
                {
                    metaQuestGuid = platformGuid;
                    Log($"Found Meta Quest platform: {displayName}");
                    break;
                }
            }

            if (metaQuestGuid == null)
            {
                Log("Meta Quest platform not found among viewable platforms.");
                return -1;
            }

            // Step 4: Check if the platform is supported/installed (advisory only).
            // Don't hard-fail — IsBuildProfileSupported can return false for
            // already-activated platforms. Proceed to GetOrCreateClassicPlatformBuildProfile
            // which will fail if the platform truly isn't available.
            if (isSupportedMethod != null)
            {
                try
                {
                    bool supported = (bool)isSupportedMethod.Invoke(null, new[] { metaQuestGuid });
                    if (!supported)
                        Log("Note: IsBuildProfileSupported returned false for Meta Quest. Proceeding anyway.");
                }
                catch { }
            }

            // Step 5: Get or create the classic platform build profile
            var instanceProp = contextType.GetProperty("instance", flags);
            var context = instanceProp?.GetValue(null);
            if (context == null)
            {
                Log("Could not get BuildProfileContext.instance.");
                return -1;
            }

            var getOrCreateMethod = contextType.GetMethod("GetOrCreateClassicPlatformBuildProfile", flags);
            if (getOrCreateMethod == null)
            {
                Log("GetOrCreateClassicPlatformBuildProfile method not found.");
                return -1;
            }

            var profile = getOrCreateMethod.Invoke(context, new[] { metaQuestGuid });
            if (profile == null)
            {
                Log("Failed to create Meta Quest build profile.");
                return -1;
            }

            Log("Created/found Meta Quest build profile.");

            // Step 5b: Check if we're already on the Meta Quest platform.
            // Read the profile's build target and compare with the active target.
            // If they match, no switch is needed — return 0 immediately.
            // This prevents steps 6a-6c from attempting a no-op switch that
            // returns 1 ("reload expected") when no reload will actually occur.
            BuildTarget profileBuildTarget = BuildTarget.NoTarget;
            foreach (var propName in new[] { "buildTarget", "platformBuildTarget", "BuildTarget" })
            {
                var prop = profile.GetType().GetProperty(propName, flags);
                if (prop != null && prop.PropertyType == typeof(BuildTarget))
                {
                    profileBuildTarget = (BuildTarget)prop.GetValue(profile);
                    break;
                }
                var fld = profile.GetType().GetField(propName, flags);
                if (fld != null && fld.FieldType == typeof(BuildTarget))
                {
                    profileBuildTarget = (BuildTarget)fld.GetValue(profile);
                    break;
                }
            }
            if (profileBuildTarget != BuildTarget.NoTarget &&
                EditorUserBuildSettings.activeBuildTarget == profileBuildTarget)
            {
                Log("Already on Meta Quest / Android build target. No switch needed.");
                return 0;
            }

            // Step 6: Activate the Meta Quest build profile.
            // SetActiveBuildProfile() works for new projects in Unity 6.2+.
            // It may throw "Classic Platforms cannot be set as the active build profile"
            // on some existing projects — fall through to alternatives if so.

            // 6a: Try SetActiveBuildProfile first — this is the approach that worked
            var setActiveMethod = buildProfileType.GetMethod("SetActiveBuildProfile", flags);
            if (setActiveMethod != null)
            {
                try
                {
                    SavePhase(PHASE_POST_PLATFORM_SWITCH);
                    setActiveMethod.Invoke(null, new[] { profile });
                    Log("Activated Meta Quest build profile. Unity may reload.");
                    return 1;
                }
                catch (System.Reflection.TargetInvocationException ex)
                {
                    string msg = ex.InnerException?.Message ?? ex.Message;
                    Log($"SetActiveBuildProfile failed: {msg}");
                    ClearPhase();
                    // Fall through to alternative approaches for classic platforms
                }
            }

            // 6b: Try methods on BuildProfileContext for classic platform activation
            string[] classicMethodNames = new[] {
                "SetActiveClassicPlatform",
                "ActivateClassicBuildProfile",
                "SetActiveClassicBuildProfile",
                "SwitchClassicPlatform",
                "SwitchActiveBuildProfile"
            };
            foreach (var name in classicMethodNames)
            {
                foreach (var searchType in new[] { contextType, buildProfileType, moduleUtilType })
                {
                    var methods = searchType.GetMethods(flags);
                    foreach (var method in methods)
                    {
                        if (method.Name != name) continue;
                        var parms = method.GetParameters();
                        try
                        {
                            object target = method.IsStatic ? null : context;
                            if (parms.Length == 1 && guidType != null && parms[0].ParameterType == guidType)
                            {
                                SavePhase(PHASE_POST_PLATFORM_SWITCH);
                                method.Invoke(target, new[] { metaQuestGuid });
                                Log($"Activated Meta Quest via {searchType.Name}.{name}(GUID).");
                                return 1;
                            }
                            else if (parms.Length == 1 && buildProfileType.IsAssignableFrom(parms[0].ParameterType))
                            {
                                SavePhase(PHASE_POST_PLATFORM_SWITCH);
                                method.Invoke(target, new[] { profile });
                                Log($"Activated Meta Quest via {searchType.Name}.{name}(BuildProfile).");
                                return 1;
                            }
                        }
                        catch (System.Exception ex)
                        {
                            string msg = ex.InnerException?.Message ?? ex.Message;
                            Log($"{searchType.Name}.{name} failed: {msg}");
                            ClearPhase();
                        }
                    }
                }
            }

            // 6c: Try setting writable properties on BuildProfileContext
            foreach (var propName in new[] { "activeProfile", "activeBuildProfile", "activeClassicPlatform" })
            {
                var prop = contextType.GetProperty(propName, flags);
                if (prop != null && prop.CanWrite)
                {
                    try
                    {
                        if (buildProfileType.IsAssignableFrom(prop.PropertyType))
                        {
                            SavePhase(PHASE_POST_PLATFORM_SWITCH);
                            prop.SetValue(context, profile);
                            Log($"Activated Meta Quest via BuildProfileContext.{propName} = profile.");
                            return 1;
                        }
                        else if (guidType != null && prop.PropertyType == guidType)
                        {
                            SavePhase(PHASE_POST_PLATFORM_SWITCH);
                            prop.SetValue(context, metaQuestGuid);
                            Log($"Activated Meta Quest via BuildProfileContext.{propName} = GUID.");
                            return 1;
                        }
                    }
                    catch (System.Exception ex)
                    {
                        string msg = ex.InnerException?.Message ?? ex.Message;
                        Log($"Setting {propName} failed: {msg}");
                        ClearPhase();
                    }
                }
            }

            // 6d: Last resort — SwitchActiveBuildTarget to Android.
            // This won't select the Meta Quest build profile specifically, but at least
            // gets the project on the Android build target so packages and settings work.
            BuildTarget profileTarget = BuildTarget.NoTarget;
            foreach (var propName in new[] { "buildTarget", "platformBuildTarget", "BuildTarget" })
            {
                var prop = profile.GetType().GetProperty(propName, flags);
                if (prop != null && prop.PropertyType == typeof(BuildTarget))
                {
                    profileTarget = (BuildTarget)prop.GetValue(profile);
                    break;
                }
                var fld = profile.GetType().GetField(propName, flags);
                if (fld != null && fld.FieldType == typeof(BuildTarget))
                {
                    profileTarget = (BuildTarget)fld.GetValue(profile);
                    break;
                }
            }
            if (profileTarget != BuildTarget.NoTarget &&
                EditorUserBuildSettings.activeBuildTarget != profileTarget)
            {
                SavePhase(PHASE_POST_PLATFORM_SWITCH);
                bool switched = EditorUserBuildSettings.SwitchActiveBuildTarget(
                    BuildTargetGroup.Android, profileTarget);
                if (switched)
                {
                    Log("WARNING: Switched to Android build target (not Meta Quest build profile).");
                    Log("To select Meta Quest specifically: File > Build Profiles > Meta Quest.");
                    return 1;
                }
                ClearPhase();
            }
            else if (profileTarget != BuildTarget.NoTarget)
            {
                // Already on Android — may be Meta Quest or generic Android.
                // We can't distinguish them, so assume it's OK and continue.
                Log("Already on Android/Meta Quest build target.");
                return 0;
            }

            // Diagnostic: Log available methods so we can find the right API
            Log("=== DIAGNOSTIC: Build Profile API methods ===");
            foreach (var searchType in new[] { contextType, buildProfileType, moduleUtilType })
            {
                Log($"--- {searchType.Name} ---");
                foreach (var m in searchType.GetMethods(flags))
                {
                    string mName = m.Name;
                    if (mName.Contains("Active") || mName.Contains("Classic") ||
                        mName.Contains("Switch") || mName.Contains("Platform") ||
                        mName.Contains("Profile"))
                    {
                        string parmStr = "";
                        foreach (var p in m.GetParameters())
                            parmStr += (parmStr.Length > 0 ? ", " : "") + p.ParameterType.Name;
                        Log($"  {(m.IsStatic ? "static " : "")}{m.ReturnType.Name} {mName}({parmStr})");
                    }
                }
            }

            return -1; // all approaches failed
        }
        catch (System.Exception ex)
        {
            var inner = (ex as System.Reflection.TargetInvocationException)?.InnerException ?? ex;
            Log($"Build Profile switch failed: {inner.GetType().Name}: {inner.Message}");
            Log($"Stack: {inner.StackTrace}");
            ClearPhase();
            return -1;
        }
    }

    // Player Settings removed — Project Setup Tool Fix All handles these.

    // ── UPM Readiness Check ─────────────────────────────────────────
    // After a domain reload from package install, the Packages folder may still
    // be locked by UPM. Poll Client.List() as a health check and enforce a minimum
    // delay before calling Client.Add() to prevent "exclusive access" errors.

    private static ListRequest _upmReadyCheck;
    private static double _upmWaitStart;
    private static System.Action _upmReadyCallback;
    private const double UPM_MIN_DELAY = 2.0;  // minimum seconds after reload before Client.Add
    private const double UPM_TIMEOUT = 15.0;    // give up waiting after this many seconds

    private static void WaitForUpmReady(System.Action onReady)
    {
        _upmWaitStart = EditorApplication.timeSinceStartup;
        _upmReadyCallback = onReady;
        _upmReadyCheck = Client.List();
        EditorApplication.update += PollUpmReady;
    }

    private static void PollUpmReady()
    {
        double elapsed = EditorApplication.timeSinceStartup - _upmWaitStart;

        // Timeout — proceed anyway
        if (elapsed >= UPM_TIMEOUT)
        {
            EditorApplication.update -= PollUpmReady;
            Log($"WARNING: UPM readiness check timed out after {UPM_TIMEOUT}s. Proceeding anyway.");
            _upmReadyCallback?.Invoke();
            return;
        }

        if (_upmReadyCheck == null || !_upmReadyCheck.IsCompleted)
            return;

        // List completed — but enforce minimum delay
        if (elapsed < UPM_MIN_DELAY)
            return; // keep polling until minimum delay has passed

        EditorApplication.update -= PollUpmReady;

        if (_upmReadyCheck.Status == StatusCode.Success)
            Log($"UPM ready after {elapsed:F1}s.");
        else
            Log($"WARNING: UPM List() returned {_upmReadyCheck.Status} after {elapsed:F1}s. Proceeding anyway.");

        _upmReadyCallback?.Invoke();
    }

    // ── Sequential Package Installation ──────────────────────────────

    private static void InstallNextPackage()
    {
        if (_stepIndex >= RequiredPackages.Length)
        {
            OnAllPackagesInstalled();
            return;
        }

        string pkg = RequiredPackages[_stepIndex];
        string label = PackageLabels[_stepIndex];
        _statusMessage = $"Installing {label} ({pkg})...";
        Log($"Installing {label} ({pkg})...");

        // Persist current index in case domain reload happens during install
        SavePhase(PHASE_INSTALLING_PACKAGES, _stepIndex);

        try
        {
            // Guard: don't start a new install if one is already in flight
            if (_currentRequest != null && !_currentRequest.IsCompleted)
            {
                Log($"WARNING: Previous package install still in progress. Skipping {pkg}.");
                return;
            }
            // Wait for UPM to be ready before calling Client.Add().
            // Prevents "exclusive access" errors from Packages folder lock contention
            // after domain reloads on 2022.3.x. On Unity 6, List() completes instantly.
            WaitForUpmReady(() =>
            {
                try
                {
                    _currentRequest = Client.Add(pkg);
                    EditorApplication.update += OnPackageInstallProgress;
                }
                catch (System.Exception ex2)
                {
                    Log($"ERROR: Failed to start install of {pkg}: {ex2.GetType().Name}: {ex2.Message}");
                    _stepIndex++;
                    InstallNextPackage();
                }
            });
        }
        catch (System.Exception ex)
        {
            Log($"ERROR: Failed to start install of {pkg}: {ex.GetType().Name}: {ex.Message}");
            _stepIndex++;
            InstallNextPackage();
        }
    }

    private static void OnPackageInstallProgress()
    {
        if (_currentRequest == null || !_currentRequest.IsCompleted)
            return;

        EditorApplication.update -= OnPackageInstallProgress;

        if (_currentRequest.Status == StatusCode.Success)
        {
            Log($"Installed: {_currentRequest.Result.packageId}");
        }
        else if (_currentRequest.Status == StatusCode.Failure)
        {
            string pkg = RequiredPackages[_stepIndex];
            Log($"FAILED to install {pkg}: {_currentRequest.Error.message}");

            if (pkg.Contains("meta"))
            {
                Log("The Meta XR SDK package name may have changed. " +
                    "Check Meta's developer docs for the current name.");
            }
        }

        _stepIndex++;
        // Save the incremented index so a domain reload during the next install
        // doesn't retry the package that just completed/failed.
        SavePhase(PHASE_INSTALLING_PACKAGES, _stepIndex);
        InstallNextPackage();
    }

    // ── Post-Install: Configure XR Settings ──────────────────────────

    private static void OnAllPackagesInstalled()
    {
        SavePhase(PHASE_POST_PACKAGES);
        _statusMessage = "Configuring OpenXR for Meta Quest...";
        Log("Configuring OpenXR for Meta Quest...");

        EditorApplication.delayCall += ConfigureXRAfterReload;
    }

    private static void ConfigureXRAfterReload()
    {
        try
        {
            Log("Generating deferred XR config script...");
            CreateDeferredConfigScript();
        }
        catch (System.Exception ex)
        {
            Log($"XR config error: {ex.Message}");
        }

        FinalStatus();
    }

    private static void CreateDeferredConfigScript()
    {
        // XR configuration requires types from the packages we just installed
        // (e.g. XRGeneralSettings, OpenXRLoader). These types don't exist until Unity
        // recompiles after package install. So we write a second script that runs ONCE
        // after the next reload (when those types are available), then deletes itself.
        string script = @"
// Auto-generated by MetaQuestVRSetup. Runs once to configure XR, then self-deletes.
// If this file causes compile errors (e.g. missing XR types), delete it manually:
//   Assets/Editor/MetaQuestXRConfig_AutoRun.cs
// Then re-run Meta > Simplified VR Setup.
#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEditor.XR.Management;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;

[InitializeOnLoad]
public class MetaQuestXRConfig_AutoRun
{
    // Project-specific EditorPrefs key prefix (avoids cross-project interference).
    // Uses a deterministic hash — string.GetHashCode() is randomized per process
    // on CoreCLR (Unity 6+) and would break EditorPrefs lookup after editor restarts.
    private static string StableHash(string s)
    {
        unchecked
        {
            int hash = 5381;
            for (int i = 0; i < s.Length; i++)
                hash = hash * 33 + s[i];
            return (hash & 0x7FFFFFFF).ToString(""X"");
        }
    }
    private static readonly string _ph = StableHash(Application.dataPath);
    private static readonly string _keyUpstRunning =
        ""MetaQuestVRSetup_"" + _ph + ""_UPST_Running"";
    private static readonly string _keyEpPhase =
        ""MetaQuestVRSetup_"" + _ph + ""_EP_Phase"";
    private static readonly string _keyEpPkgIndex =
        ""MetaQuestVRSetup_"" + _ph + ""_EP_PkgIndex"";
    private static readonly string _keyEpTimestamp =
        ""MetaQuestVRSetup_"" + _ph + ""_EP_Timestamp"";
    private static readonly string _keyEpRestartCount =
        ""MetaQuestVRSetup_"" + _ph + ""_EP_RestartCount"";

    static MetaQuestXRConfig_AutoRun()
    {
        EditorApplication.delayCall += Configure;
    }

    private static void Configure()
    {
        try
        {
            // Auto-dismiss any ""Enable Feature Set"" dialogs shown by the Meta XR SDK
            // during its [InitializeOnLoad]. Our script already enables everything the
            // feature set would, so this dialog is redundant.
            DismissFeatureSetDialogs();

            // Clean up duplicate XR folders created by package initialization.
            // Each XR package may create its own Assets/XR folder during [InitializeOnLoad],
            // resulting in Assets/XR, Assets/XR 1, Assets/XR 2, etc.
            CleanupDuplicateXRFolders();

            // Resolve the correct BuildTargetGroup — Meta Quest on Unity 6.1+, Android otherwise.
            // Search by name pattern since the enum field name varies across Unity versions.
            BuildTargetGroup targetGroup = BuildTargetGroup.Android;
            System.Reflection.FieldInfo mqGroupField = typeof(BuildTargetGroup).GetField(""MetaQuest"");
            if (mqGroupField == null)
            {
                foreach (var field in typeof(BuildTargetGroup).GetFields(
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
                {
                    string name = field.Name;
                    if (name.IndexOf(""Quest"", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                        name == ""MetaQuest"")
                    {
                        mqGroupField = field;
                        Debug.Log(""[Meta Quest VR Setup] Found Quest build target group: "" + name);
                        break;
                    }
                }
            }
            if (mqGroupField != null)
            {
                targetGroup = (BuildTargetGroup)mqGroupField.GetValue(null);
                Debug.Log(""[Meta Quest VR Setup] Using "" + mqGroupField.Name + "" build target group."");
            }

            // Check if resuming after editor restart (e.g. graphics API change on Windows)
            bool resumingUpst = EditorPrefs.GetBool(_keyUpstRunning, false);

            if (!resumingUpst)
            {
                // Configure OpenXR for the device platform (Android / Meta Quest)
                EnableOpenXRForBuildTarget(targetGroup);

                // Also enable OpenXR for the desktop platform (Standalone)
                // so Quest Link / Play Mode testing works in the Editor
                EnableOpenXRForBuildTarget(BuildTargetGroup.Standalone);
                Debug.Log(""[Meta Quest VR Setup] OpenXR enabled for desktop (Quest Link / Play Mode)."");

                // Enable Meta Quest feature in OpenXR (device platform only)
                var openxrSettings = OpenXRSettings.GetSettingsForBuildTargetGroup(targetGroup);
                if (openxrSettings != null)
                {
                    // Use reflection-based name matching for all features and profiles.
                    // This avoids hardcoded using directives that break if Meta changes namespaces.
                    EnableFeaturesByName(openxrSettings);

                    // Remove duplicate feature entries from OpenXR settings.
                    // Re-running setup or XR reinit can produce duplicates in the serialized asset.
                    DeduplicateOpenXRFeatures(openxrSettings);
                }

                Debug.Log(""[Meta Quest VR Setup] XR configured for Meta Quest successfully!"");

                // Set up VR scene (remove Main Camera, add OVRCameraRig) — ask first
                SetupVRScene();

                // Configure Audio Spatializer for Meta XR (if available)
                ConfigureAudioSpatializer();
            }
            else
            {
                Debug.Log(""[Meta Quest VR Setup] Resuming setup after editor restart (graphics API change)..."");
            }

            // Mark Project Setup Tool as in-progress. This flag survives editor restarts
            // caused by changing the Standalone graphics API (Windows: Direct3D → Vulkan).
            // On restart, the deferred script's [InitializeOnLoad] re-enters Configure(),
            // sees this flag, skips XR/scene/audio config, and re-runs all passes.
            EditorPrefs.SetBool(_keyUpstRunning, true);

            // Run Project Setup Tool Fix All after scene setup and audio config.
            // Android first (two passes), then Standalone (two passes).
            // Standalone is required because Unity uses Standalone settings in Editor
            // (XR Sim, Quest Link). Some tasks only become fixable after pass 1.
            EditorApplication.delayCall += () =>
            {
                try
                {
                    Debug.Log(""[Meta Quest VR Setup] Running Project Setup Tool Fix All (pass 1)..."");
                    RunProjectSetupToolFixAll(targetGroup);
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning(""[Meta Quest VR Setup] Project Setup Tool pass 1 failed: "" + ex.Message);
                }

                // Second deferred pass — catches tasks that became fixable after pass 1
                EditorApplication.delayCall += () =>
                {
                    try
                    {
                        Debug.Log(""[Meta Quest VR Setup] Running Project Setup Tool Fix All (pass 2)..."");
                        RunProjectSetupToolFixAll(targetGroup);
                        EnumerateRemainingTasks(targetGroup);
                    }
                    catch (System.Exception ex)
                    {
                        Debug.LogWarning(""[Meta Quest VR Setup] Project Setup Tool Android pass 2 failed: "" + ex.Message);
                    }

                    // ── Standalone Project Setup Tool passes ────────────────
                    // The Meta XR SDK uses Standalone settings in Editor (XR Sim, Quest Link),
                    // so fixing Standalone tasks is required, not optional.
                    EditorApplication.delayCall += () =>
                    {
                        try
                        {
                            Debug.Log(""[Meta Quest VR Setup] Running Project Setup Tool Fix All — Standalone (pass 1)..."");
                            RunProjectSetupToolFixAll(BuildTargetGroup.Standalone);
                        }
                        catch (System.Exception ex)
                        {
                            Debug.LogWarning(""[Meta Quest VR Setup] Project Setup Tool Standalone pass 1 failed: "" + ex.Message);
                        }

                        EditorApplication.delayCall += () =>
                        {
                            try
                            {
                                Debug.Log(""[Meta Quest VR Setup] Running Project Setup Tool Fix All — Standalone (pass 2)..."");
                                RunProjectSetupToolFixAll(BuildTargetGroup.Standalone);
                                EnumerateRemainingTasks(BuildTargetGroup.Standalone);
                            }
                            catch (System.Exception ex)
                            {
                                Debug.LogWarning(""[Meta Quest VR Setup] Project Setup Tool Standalone pass 2 failed: "" + ex.Message);
                            }

                            // ── Finalize ────────────────────────────────────────
                            // Save scene — Fix All may have modified OVRManager settings
                            try
                            {
                                var scene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
                                if (scene.isDirty)
                                {
                                    UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
                                    Debug.Log(""[Meta Quest VR Setup] Scene saved (post-PST)."");
                                }
                            }
                            catch (System.Exception ex2)
                            {
                                Debug.Log(""[Meta Quest VR Setup] Could not save scene: "" + ex2.Message);
                            }

                            // Close Project Setup Tool and Package Manager windows
                            CloseProjectSetupToolWindow();
                            ClosePackageManagerWindow();

                            // Clear in-progress flag — setup completed successfully
                            EditorPrefs.DeleteKey(_keyUpstRunning);

                            Debug.Log(""══════════════════════════════════════════════════════════════"");
                            Debug.Log(""[Meta Quest VR Setup] SETUP COMPLETE — Your project is ready to build for Meta Quest!"");
                            Debug.Log(""══════════════════════════════════════════════════════════════"");

                            // Self-deletion and optional SDK flag ALWAYS scheduled,
                            // even if Project Setup Tool or scene save threw above.
                            EditorPrefs.SetBool(""MetaQuestVRSetup_"" + _ph + ""_ShowOptionalSDKs"", true);
                            EditorApplication.delayCall += () =>
                            {
                                AssetDatabase.DeleteAsset(""Assets/Editor/MetaQuestXRConfig_AutoRun.cs"");
                            };
                        };
                    };
                };
            };

            // Flag the main script to verify the Meta Quest build profile after
            // the domain reload triggered by self-deletion.
            // Using SessionState because delayCall doesn't survive domain reloads.
            // The main script's [InitializeOnLoad] constructor checks this flag.
            SessionState.SetInt(""MetaQuestVRSetup_ReactivateProfile"", 1);
        }
        catch (System.Exception ex)
        {
            string errMsg = ""[Meta Quest VR Setup] XR configuration failed: "" + ex.Message + ""\n"" + ex.StackTrace;
            Debug.LogError(errMsg);
            Debug.LogError(""You can configure manually: Edit > Project Settings > XR Plug-in Management"");
            // Persist error to file — Unity clears console on restart/domain reload
            try { System.IO.File.WriteAllText(
                System.IO.Path.Combine(Application.dataPath, "".."", ""Library"", ""MetaQuestSetup_Error.txt""),
                System.DateTime.Now + ""\n"" + errMsg); } catch { }
            EditorPrefs.DeleteKey(_keyUpstRunning);

            // Self-delete on failure too, to prevent retry-loop
            EditorApplication.delayCall += () =>
            {
                AssetDatabase.DeleteAsset(""Assets/Editor/MetaQuestXRConfig_AutoRun.cs"");
            };
        }
        finally
        {
            // Clear the main script's phase in case it was not cleared before
            // the domain reload that compiled this script. This prevents
            // CheckForResume() from re-entering OnAllPackagesInstalled().
            SessionState.EraseInt(""MetaQuestVRSetup_Phase"");
            SessionState.EraseInt(""MetaQuestVRSetup_PkgIndex"");
            // Also clear EditorPrefs (survive editor restarts) so stale state
            // doesn't trigger auto-resume on next editor open.
            EditorPrefs.DeleteKey(_keyEpPhase);
            EditorPrefs.DeleteKey(_keyEpPkgIndex);
            EditorPrefs.DeleteKey(_keyEpTimestamp);
            EditorPrefs.DeleteKey(_keyEpRestartCount);
        }
    }

    private static void EnableFeaturesByName(OpenXRSettings openxrSettings)
    {
        // Enable all Meta Quest-related features and interaction profiles using
        // reflection-based name matching. This avoids hardcoded namespace dependencies
        // (e.g. MetaQuestSupport, Interactions) that break if Meta renames them.
        string[] featurePatterns = new[] {
            ""MetaQuestFeature"",           // Core Meta Quest support (Unity OpenXR)
            ""MetaXRFeature"",              // Meta XR extensions (passthrough, body/face tracking, spatial anchors, haptics)
            ""OculusTouchController"",      // Quest 2 controllers
            ""MetaQuestTouchPlus"",         // Quest 3 controllers
            ""MetaQuestTouchPro"",          // Quest Pro controllers
            ""HandInteraction""             // OpenXR hand interaction profile
            // Foveation and Subsampled Layout omitted — Project Setup Tool handles these
        };

        // Track which patterns matched so we can warn about missing features
        var matched = new System.Collections.Generic.HashSet<string>();

        var allFeatures = openxrSettings.GetFeatures<OpenXRFeature>();
        foreach (var feature in allFeatures)
        {
            string typeName = feature.GetType().Name;
            foreach (var pattern in featurePatterns)
            {
                if (typeName.Contains(pattern))
                {
                    matched.Add(pattern);
                    if (!feature.enabled)
                    {
                        feature.enabled = true;
                        Debug.Log($""[Meta Quest VR Setup] Enabled {typeName}."");
                    }
                    break;
                }
            }
        }

        // Warn about critical features that weren't found
        string[] critical = new[] { ""MetaQuestFeature"", ""MetaXRFeature"" };
        foreach (var pattern in critical)
        {
            if (!matched.Contains(pattern))
            {
                Debug.LogWarning($""[Meta Quest VR Setup] Expected OpenXR feature '{pattern}' "" +
                    ""not found. Check that Meta XR Core SDK is installed correctly."");
            }
        }
    }

    private static void DeduplicateOpenXRFeatures(OpenXRSettings openxrSettings)
    {
        // OpenXR Package Settings can accumulate duplicate feature entries when
        // setup runs multiple times or XR settings are re-initialized after package
        // reinstall. Unity references features by fileID so duplicates don't break
        // anything, but they bloat the settings file. This removes extras, keeping
        // the first (enabled) instance of each feature type.
        try
        {
            var allFeatures = openxrSettings.GetFeatures<OpenXRFeature>();
            if (allFeatures == null || allFeatures.Length <= 1) return;

            var seen = new System.Collections.Generic.HashSet<string>();
            int removed = 0;
            foreach (var feature in allFeatures)
            {
                if (feature == null) continue;
                string key = feature.GetType().FullName;
                if (!seen.Add(key))
                {
                    // Duplicate — remove from the asset container first to avoid
                    // dangling references, then destroy the ScriptableObject.
                    feature.enabled = false;
                    AssetDatabase.RemoveObjectFromAsset(feature);
                    Object.DestroyImmediate(feature);
                    removed++;
                }
            }

            if (removed > 0)
            {
                // Force OpenXR to rebuild its internal features array from the asset
                EditorUtility.SetDirty(openxrSettings);
                AssetDatabase.SaveAssets();
                Debug.Log($""[Meta Quest VR Setup] Removed {removed} duplicate OpenXR feature entries."");
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning(""[Meta Quest VR Setup] Could not deduplicate OpenXR features: "" + ex.Message);
        }
    }

    private static double _pstPollStartTime = -1;
    private static BuildTargetGroup _pstTargetGroup;

    private static void RunProjectSetupToolFixAll(BuildTargetGroup resolvedTargetGroup)
    {
        // Meta XR Core SDK's Project Setup Tool (PST) uses a Registry + ProcessorQueue
        // architecture. Tasks are stored in OVRConfigurationTaskRegistry, and Fix All
        // is triggered via OVRProjectSetup.FixTasks() or the public FixAllAsync().
        //
        // Since the SDK is installed at runtime (not compile time), we resolve these
        // public types and methods via reflection.
        //
        // Approach (in priority order):
        //   1. OVRProjectSetup.FixTasks(btg, ..., blocking: true) — most reliable
        //   2. OVRProjectSetup.FixAllAsync(btg) — public async API
        //   3. Iterate Registry tasks and call task.Fix(btg) individually
        //   4. Manual fallback message
        try
        {
            // Try to explicitly load the SDK editor assembly first
            try { System.Reflection.Assembly.Load(""Oculus.VR.Editor""); } catch { }

            // Find the OVRProjectSetup static class
            System.Type setupType = FindType(""OVRProjectSetup"");
            if (setupType == null)
            {
                // SDK assembly may not be fully loaded yet.
                // Use EditorApplication.update polling with 1s intervals for up to 30s.
                // (delayCall fires too fast — all retries complete within milliseconds)
                if (_pstPollStartTime < 0)
                {
                    _pstPollStartTime = EditorApplication.timeSinceStartup;

                    // Diagnostic: log what Meta/OVR/Oculus assemblies ARE loaded
                    string metaAsm = """";
                    foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies())
                    {
                        string n = a.GetName().Name;
                        if (n.IndexOf(""Meta"", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                            n.IndexOf(""OVR"", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                            n.IndexOf(""Oculus"", System.StringComparison.OrdinalIgnoreCase) >= 0)
                            metaAsm += ""\n  "" + n;
                    }
                    Debug.Log(""[Meta Quest VR Setup] OVRProjectSetup not found yet. "" +
                              ""Will poll for up to 30s. Meta/OVR assemblies currently loaded:"" + metaAsm);

                    _pstTargetGroup = resolvedTargetGroup;
                    EditorApplication.update += PollForProjectSetupTool;
                    return;
                }

                // If we get here, polling timed out — this path is reached from the poll callback
                Debug.Log(""[Meta Quest VR Setup] OVRProjectSetup type not found after 30s. "" +
                          ""Run manually: Meta > Tools > Project Setup Tool > Fix All > Apply All"");
                return;
            }

            Debug.Log(""[Meta Quest VR Setup] Found OVRProjectSetup: "" + setupType.FullName);

            var bf = System.Reflection.BindingFlags.Public |
                     System.Reflection.BindingFlags.NonPublic |
                     System.Reflection.BindingFlags.Static;

            // Step 1: Try FixTasks(BuildTargetGroup, filter, logMessages, blocking, onCompleted)
            // This is the same method the PST UI calls. It queues a OVRConfigurationTaskFixer
            // processor that handles retries (up to 4 passes) and cascading dependencies.
            var fixTasksMethod = setupType.GetMethod(""FixTasks"", bf);
            if (fixTasksMethod != null)
            {
                var parameters = fixTasksMethod.GetParameters();
                Debug.Log($""[Meta Quest VR Setup] Found FixTasks with {parameters.Length} params"");

                if (parameters.Length >= 1 && parameters[0].ParameterType == typeof(BuildTargetGroup))
                {
                    // Build args array matching the method signature.
                    // FixTasks(BuildTargetGroup, filter=null, logMessages=0, blocking=true, onCompleted=null)
                    object[] args = new object[parameters.Length];
                    args[0] = resolvedTargetGroup;
                    for (int i = 1; i < parameters.Length; i++)
                    {
                        if (parameters[i].ParameterType == typeof(bool) &&
                            (parameters[i].Name == ""blocking"" || parameters[i].Name == ""isBlocking""))
                            args[i] = true; // blocking = true
                        else if (parameters[i].HasDefaultValue)
                            args[i] = parameters[i].DefaultValue;
                        else
                            args[i] = null;
                    }

                    fixTasksMethod.Invoke(null, args);
                    Debug.Log(""[Meta Quest VR Setup] Project Setup Tool: FixTasks executed (blocking)."");
                    return;
                }
            }

            // Step 2: Try FixAllAsync(BuildTargetGroup) — public async API
            var fixAllAsync = setupType.GetMethod(""FixAllAsync"", bf);
            if (fixAllAsync != null)
            {
                var parameters = fixAllAsync.GetParameters();
                if (parameters.Length == 1 && parameters[0].ParameterType == typeof(BuildTargetGroup))
                {
                    // Returns Task — we invoke and let it run. The ProcessorQueue
                    // hooks into EditorApplication.update to process on main thread.
                    fixAllAsync.Invoke(null, new object[] { resolvedTargetGroup });
                    Debug.Log(""[Meta Quest VR Setup] Project Setup Tool: FixAllAsync invoked."");
                    return;
                }
            }

            // Step 3: Iterate individual tasks via Registry
            // OVRProjectSetup has a static Registry property (OVRConfigurationTaskRegistry).
            // Registry has GetTasks(BuildTargetGroup) returning IEnumerable<OVRConfigurationTask>.
            // Each task has public Fix(BuildTargetGroup) and IsDone(BuildTargetGroup).
            if (TryFixIndividualTasks(setupType, resolvedTargetGroup))
                return;

            // Fallback: log manual instruction
            Debug.Log(""[Meta Quest VR Setup] Project Setup Tool could not be run automatically. "" +
                      ""Run manually: Meta > Tools > Project Setup Tool > Fix All > Apply All"");
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning(""[Meta Quest VR Setup] Could not auto-run Project Setup Tool: "" +
                             ex.Message + ""\nRun manually: Meta > Tools > Project Setup Tool"");
        }
    }

    private static double _pstLastPollTime = 0;

    private static void PollForProjectSetupTool()
    {
        // Poll every 1 second for OVRProjectSetup type, up to 30s total.
        double now = EditorApplication.timeSinceStartup;
        if (now - _pstLastPollTime < 1.0) return; // throttle to 1s intervals
        _pstLastPollTime = now;

        double elapsed = now - _pstPollStartTime;

        // Try loading the assembly each poll
        try { System.Reflection.Assembly.Load(""Oculus.VR.Editor""); } catch { }

        System.Type setupType = FindType(""OVRProjectSetup"");
        if (setupType != null)
        {
            EditorApplication.update -= PollForProjectSetupTool;
            Debug.Log($""[Meta Quest VR Setup] Found OVRProjectSetup after {elapsed:F1}s: {setupType.FullName}"");
            // Reset for potential future calls
            _pstPollStartTime = -1;
            RunProjectSetupToolFixAll(_pstTargetGroup);
            return;
        }

        if (elapsed >= 30.0)
        {
            EditorApplication.update -= PollForProjectSetupTool;
            // Log diagnostic on timeout
            string metaAsm = """";
            foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                string n = a.GetName().Name;
                if (n.IndexOf(""Meta"", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.IndexOf(""OVR"", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.IndexOf(""Oculus"", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    metaAsm += ""\n  "" + n;
            }
            Debug.Log(""[Meta Quest VR Setup] OVRProjectSetup type not found after 30s. "" +
                      ""Meta/OVR assemblies loaded:"" + metaAsm +
                      ""\nRun manually: Meta > Tools > Project Setup Tool > Fix All > Apply All"");
            _pstPollStartTime = -1;
            return;
        }

        Debug.Log($""[Meta Quest VR Setup] Polling for OVRProjectSetup... {elapsed:F0}s elapsed"");
    }

    private static System.Type FindType(string typeName)
    {
        // Search all loaded assemblies for a type by short name.
        // Tries exact match first, then EndsWith to handle namespaced types.
        foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
        {
            // Fast path: try common fully-qualified names
            var t = asm.GetType(typeName);
            if (t != null) return t;
        }

        // Slow path: scan all types for a name match
        foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
        {
            System.Type[] types = null;
            try { types = asm.GetTypes(); }
            catch (System.Reflection.ReflectionTypeLoadException ex) { types = ex.Types; }
            catch { continue; }
            if (types == null) continue;

            foreach (var t in types)
            {
                if (t == null) continue;
                if (t.Name == typeName) return t;
            }
        }
        return null;
    }

    private static bool TryFixIndividualTasks(System.Type setupType, BuildTargetGroup resolvedTargetGroup)
    {
        // Access OVRProjectSetup.Registry (OVRConfigurationTaskRegistry),
        // then call GetTasks(BuildTargetGroup) or iterate the task list directly.
        // Each OVRConfigurationTask has public Fix(BuildTargetGroup) and IsDone(BuildTargetGroup).
        var bf = System.Reflection.BindingFlags.Public |
                 System.Reflection.BindingFlags.NonPublic |
                 System.Reflection.BindingFlags.Static |
                 System.Reflection.BindingFlags.Instance;

        // Try to get the Registry property
        object registry = null;
        var registryProp = setupType.GetProperty(""Registry"", bf);
        if (registryProp != null)
        {
            try { registry = registryProp.GetValue(null); } catch { }
        }

        System.Collections.IEnumerable tasks = null;

        if (registry != null)
        {
            Debug.Log(""[Meta Quest VR Setup] Found Registry: "" + registry.GetType().FullName);

            // Try GetTasks(BuildTargetGroup) or GetValidTasks(BuildTargetGroup)
            var regType = registry.GetType();
            foreach (var methodName in new[] { ""GetTasks"", ""GetValidTasks"" })
            {
                var method = regType.GetMethod(methodName, bf);
                if (method != null)
                {
                    var pp = method.GetParameters();
                    if (pp.Length == 1 && pp[0].ParameterType == typeof(BuildTargetGroup))
                    {
                        try
                        {
                            tasks = method.Invoke(registry, new object[] { resolvedTargetGroup })
                                    as System.Collections.IEnumerable;
                            if (tasks != null)
                            {
                                Debug.Log($""[Meta Quest VR Setup] Got tasks via Registry.{methodName}()"");
                                break;
                            }
                        }
                        catch { }
                    }
                }
            }
        }

        // Fallback: try static GetTasks on OVRProjectSetup itself
        if (tasks == null)
        {
            var getTasks = setupType.GetMethod(""GetTasks"", bf);
            if (getTasks != null)
            {
                var pp = getTasks.GetParameters();
                if (pp.Length == 1 && pp[0].ParameterType == typeof(BuildTargetGroup))
                {
                    try
                    {
                        tasks = getTasks.Invoke(null, new object[] { resolvedTargetGroup })
                                as System.Collections.IEnumerable;
                        if (tasks != null)
                            Debug.Log(""[Meta Quest VR Setup] Got tasks via OVRProjectSetup.GetTasks()"");
                    }
                    catch { }
                }
            }
        }

        if (tasks == null)
        {
            Debug.Log(""[Meta Quest VR Setup] Could not access task registry."");
            return false;
        }

        // Iterate tasks: skip done/ignored, call Fix(BuildTargetGroup) on each
        int fixCount = 0, skipCount = 0, failCount = 0, total = 0;
        foreach (var task in tasks)
        {
            if (task == null) continue;
            total++;
            try
            {
                var tt = task.GetType();

                // Check IsDone(BuildTargetGroup)
                var isDone = tt.GetMethod(""IsDone"", bf, null,
                    new[] { typeof(BuildTargetGroup) }, null);
                if (isDone != null)
                {
                    bool done = (bool)isDone.Invoke(task, new object[] { resolvedTargetGroup });
                    if (done) { skipCount++; continue; }
                }

                // Check IsIgnored(BuildTargetGroup)
                var isIgnored = tt.GetMethod(""IsIgnored"", bf, null,
                    new[] { typeof(BuildTargetGroup) }, null);
                if (isIgnored != null)
                {
                    bool ignored = (bool)isIgnored.Invoke(task, new object[] { resolvedTargetGroup });
                    if (ignored) { skipCount++; continue; }
                }

                // Check FixAction is not null (task is fixable)
                var fixActionProp = tt.GetProperty(""FixAction"", bf);
                if (fixActionProp != null)
                {
                    var fixAction = fixActionProp.GetValue(task);
                    if (fixAction == null) { skipCount++; continue; }
                }

                // Call Fix(BuildTargetGroup) — public method
                var fixMethod = tt.GetMethod(""Fix"", bf, null,
                    new[] { typeof(BuildTargetGroup) }, null);
                if (fixMethod != null)
                {
                    var result = fixMethod.Invoke(task, new object[] { resolvedTargetGroup });
                    if (result is bool success && success)
                        fixCount++;
                    else
                        failCount++;
                }
                else
                {
                    failCount++;
                }
            }
            catch (System.Exception ex)
            {
                failCount++;
                Debug.LogWarning(""[Meta Quest VR Setup] Task fix error: "" + ex.Message);
            }
        }

        Debug.Log($""[Meta Quest VR Setup] Project Setup Tool: {fixCount} fixed, "" +
                  $""{skipCount} skipped (done/ignored), {failCount} failed, {total} total."");
        return fixCount > 0 || (total > 0 && failCount == 0);
    }

    private static void SetupVRScene()
    {
        try
        {
            // Check if OVRCameraRig already exists (idempotent re-run — no prompt needed)
            bool cameraRigExists = false;
            System.Reflection.MethodInfo findGoMethod = null;
            foreach (var m in typeof(Object).GetMethods(
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
            {
                if (m.Name == ""FindObjectsByType"" && m.IsGenericMethodDefinition &&
                    m.GetParameters().Length == 1)
                {
                    findGoMethod = m;
                    break;
                }
            }
            GameObject[] allGOs;
            if (findGoMethod != null)
            {
                var generic = findGoMethod.MakeGenericMethod(typeof(GameObject));
                var paramType = generic.GetParameters()[0].ParameterType;
                // FindObjectsSortMode.None == 0; safe since this is the only single-param generic overload
                var noneValue = System.Enum.ToObject(paramType, 0);
                allGOs = (GameObject[])generic.Invoke(null, new object[] { noneValue });
            }
            else
            {
                #pragma warning disable CS0618
                allGOs = Object.FindObjectsOfType<GameObject>();
                #pragma warning restore CS0618
            }
            foreach (var go in allGOs)
            {
                if (go.name == ""OVRCameraRig"")
                {
                    cameraRigExists = true;
                    break;
                }
            }

            if (cameraRigExists)
            {
                Debug.Log(""[Meta Quest VR Setup] OVRCameraRig already exists in scene. Skipping."");
                return;
            }

            // Ask before modifying the scene
            bool proceed = EditorUtility.DisplayDialog(
                ""Set Up VR Scene?"",
                ""This will remove the default Main Camera and add an OVRCameraRig prefab "" +
                ""with FloorLevel tracking.\n\nSkip this if you're working on an existing scene."",
                ""Set Up Scene"", ""Skip"");

            if (!proceed)
            {
                Debug.Log(""[Meta Quest VR Setup] Scene setup skipped by user."");
                return;
            }

            bool sceneModified = false;

            // Delete default Main Camera
            Camera[] cameras = null;
            System.Reflection.MethodInfo findByTypeMethod = null;
            foreach (var m in typeof(Object).GetMethods(
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
            {
                if (m.Name == ""FindObjectsByType"" && m.IsGenericMethodDefinition &&
                    m.GetParameters().Length == 1)
                {
                    findByTypeMethod = m;
                    break;
                }
            }
            if (findByTypeMethod != null)
            {
                var generic = findByTypeMethod.MakeGenericMethod(typeof(Camera));
                var paramType = generic.GetParameters()[0].ParameterType;
                var noneValue = System.Enum.ToObject(paramType, 0);
                cameras = (Camera[])generic.Invoke(null, new object[] { noneValue });
            }
            else
            {
                #pragma warning disable CS0618
                cameras = Object.FindObjectsOfType<Camera>();
                #pragma warning restore CS0618
            }
            foreach (var cam in cameras)
            {
                if (cam.gameObject.name == ""Main Camera"")
                {
                    Object.DestroyImmediate(cam.gameObject);
                    Debug.Log(""[Meta Quest VR Setup] Removed default Main Camera."");
                    sceneModified = true;
                    break;
                }
            }

            // Find and instantiate OVRCameraRig prefab from Meta XR SDK
            string[] guids = AssetDatabase.FindAssets(""OVRCameraRig t:Prefab"");
            if (guids.Length > 0)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab != null)
                {
                    var instance = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
                    Debug.Log(""[Meta Quest VR Setup] Added OVRCameraRig to scene."");
                    sceneModified = true;

                    if (instance != null)
                    {
                        SetTrackingOriginToFloorLevel(instance);
                    }
                }
                else
                {
                    Debug.LogWarning(""[Meta Quest VR Setup] OVRCameraRig prefab could not be loaded."");
                }
            }
            else
            {
                Debug.LogWarning(""[Meta Quest VR Setup] OVRCameraRig prefab not found. "" +
                    ""Add it manually: search for OVRCameraRig in your Project window."");
            }

            // Save the scene if we modified it (instead of just marking dirty)
            if (sceneModified)
            {
                UnityEditor.SceneManagement.EditorSceneManager.SaveScene(
                    UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
                Debug.Log(""[Meta Quest VR Setup] Scene saved."");
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning(""[Meta Quest VR Setup] Scene setup: "" + ex.Message +
                ""\nYou can manually: delete Main Camera, then add OVRCameraRig prefab to your scene."");
        }
    }

    private static void ConfigureAudioSpatializer()
    {
        // Set the Audio Spatializer to Meta XR Audio if available.
        // Without this, spatial audio won't work in VR.
        try
        {
            string current = AudioSettings.GetSpatializerPluginName();
            if (!string.IsNullOrEmpty(current) && current.Contains(""Meta""))
            {
                Debug.Log(""[Meta Quest VR Setup] Audio Spatializer already set to "" + current);
                return;
            }

            // Try known Meta spatializer plugin names
            string[] metaSpatializers = new[] {
                ""Meta XR Audio"",
                ""MetaXRAudio"",
                ""OculusSpatializer""
            };

            // Get available spatializer plugins via reflection
            // (GetSpatializerPluginNames is internal in some Unity versions)
            var method = typeof(AudioSettings).GetMethod(""GetSpatializerPluginNames"",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic);

            if (method != null)
            {
                var available = method.Invoke(null, null) as string[];
                if (available != null)
                {
                    foreach (var spatName in metaSpatializers)
                    {
                        foreach (var avail in available)
                        {
                            if (avail.Contains(spatName) || spatName.Contains(avail))
                            {
                                AudioSettings.SetSpatializerPluginName(avail);
                                Debug.Log(""[Meta Quest VR Setup] Audio Spatializer set to "" + avail);
                                return;
                            }
                        }
                    }
                    Debug.Log(""[Meta Quest VR Setup] Meta XR Audio spatializer not found in available plugins. "" +
                        ""You can set it manually: Edit > Project Settings > Audio > Spatializer Plugin."");
                }
            }
            else
            {
                // Direct attempt if reflection fails
                AudioSettings.SetSpatializerPluginName(""Meta XR Audio"");
                Debug.Log(""[Meta Quest VR Setup] Audio Spatializer set to Meta XR Audio (unverified)."");
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning(""[Meta Quest VR Setup] Audio spatializer config: "" + ex.Message +
                ""\nSet manually: Edit > Project Settings > Audio > Spatializer Plugin > Meta XR Audio"");
        }
    }

    private static void SetTrackingOriginToFloorLevel(GameObject ovrCameraRig)
    {
        // Set OVRManager.trackingOriginType to FloorLevel via reflection.
        // OVRManager.TrackingOrigin.FloorLevel = 1
        try
        {
            var ovrManager = ovrCameraRig.GetComponent(""OVRManager"");
            if (ovrManager == null)
            {
                Debug.Log(""[Meta Quest VR Setup] OVRManager component not found on OVRCameraRig."");
                return;
            }

            // Try multiple field/property names — SDK v85+ may have renamed or changed to property
            var bf = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance;
            string[] fieldNames = new[] { ""trackingOriginType"", ""_trackingOriginType"", ""TrackingOriginType"" };
            bool found = false;

            // Try fields first
            foreach (var name in fieldNames)
            {
                var field = ovrManager.GetType().GetField(name, bf);
                if (field != null)
                {
                    var enumType = field.FieldType;
                    object floorLevel;
                    try { floorLevel = System.Enum.Parse(enumType, ""FloorLevel""); }
                    catch { floorLevel = System.Enum.ToObject(enumType, 1); }
                    field.SetValue(ovrManager, floorLevel);
                    Debug.Log(""[Meta Quest VR Setup] Tracking origin set to FloorLevel (field: "" + name + "")."");
                    found = true;
                    break;
                }
            }

            // Try properties if no field found
            if (!found)
            {
                foreach (var name in fieldNames)
                {
                    var property = ovrManager.GetType().GetProperty(name, bf);
                    if (property != null && property.CanWrite)
                    {
                        var enumType = property.PropertyType;
                        object floorLevel;
                        try { floorLevel = System.Enum.Parse(enumType, ""FloorLevel""); }
                        catch { floorLevel = System.Enum.ToObject(enumType, 1); }
                        property.SetValue(ovrManager, floorLevel);
                        Debug.Log(""[Meta Quest VR Setup] Tracking origin set to FloorLevel (property: "" + name + "")."");
                        found = true;
                        break;
                    }
                }
            }

            // Last resort: search all fields/properties containing tracking and origin
            if (!found)
            {
                foreach (var f in ovrManager.GetType().GetFields(bf))
                {
                    string fn = f.Name.ToLower();
                    if (fn.Contains(""tracking"") && fn.Contains(""origin""))
                    {
                        var enumType = f.FieldType;
                        if (enumType.IsEnum && System.Enum.IsDefined(enumType, ""FloorLevel""))
                        {
                            object floorLevel = System.Enum.Parse(enumType, ""FloorLevel"");
                            f.SetValue(ovrManager, floorLevel);
                            Debug.Log(""[Meta Quest VR Setup] Tracking origin set to FloorLevel (found: "" + f.Name + "")."");
                            found = true;
                            break;
                        }
                    }
                }
            }
            if (!found)
            {
                foreach (var p in ovrManager.GetType().GetProperties(bf))
                {
                    string pn = p.Name.ToLower();
                    if (pn.Contains(""tracking"") && pn.Contains(""origin"") && p.CanWrite)
                    {
                        var enumType = p.PropertyType;
                        if (enumType.IsEnum && System.Enum.IsDefined(enumType, ""FloorLevel""))
                        {
                            object floorLevel = System.Enum.Parse(enumType, ""FloorLevel"");
                            p.SetValue(ovrManager, floorLevel);
                            Debug.Log(""[Meta Quest VR Setup] Tracking origin set to FloorLevel (found: "" + p.Name + "")."");
                            found = true;
                            break;
                        }
                    }
                }
            }

            if (!found)
            {
                // Log available fields for diagnostics
                string fields = """";
                foreach (var f in ovrManager.GetType().GetFields(bf))
                    if (f.Name.ToLower().Contains(""track"") || f.Name.ToLower().Contains(""origin""))
                        fields += "" "" + f.Name;
                foreach (var p in ovrManager.GetType().GetProperties(bf))
                    if (p.Name.ToLower().Contains(""track"") || p.Name.ToLower().Contains(""origin""))
                        fields += "" "" + p.Name + ""(prop)"";
                Debug.Log(""[Meta Quest VR Setup] Could not find tracking origin field on OVRManager."" +
                    "" Available tracking/origin members:"" + fields +
                    ""\nSet manually: OVRCameraRig > OVRManager > Tracking Origin Type > Floor Level"");
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning(""[Meta Quest VR Setup] Could not set tracking origin: "" + ex.Message +
                ""\nSet manually: OVRCameraRig > OVRManager > Tracking Origin Type > Floor Level"");
        }
    }

    private static void DismissFeatureSetDialogs()
    {
        // The Meta XR SDK shows an ""Enable Meta XR Feature Set"" dialog during
        // its [InitializeOnLoad] when it detects that its feature set isn't enabled.
        // Since our script already enables OpenXR, the Meta Quest feature, Hand Tracking,
        // and all other features programmatically, this dialog is redundant.
        // Close any such dialog windows automatically.
        try
        {
            var allWindows = Resources.FindObjectsOfTypeAll<EditorWindow>();
            foreach (var window in allWindows)
            {
                string title = window.titleContent.text;
                // Match the feature set dialog — its title typically contains
                // ""Feature"" or ""XR Feature Set"" or similar
                if (title.Contains(""Feature Set"") || title.Contains(""Feature set""))
                {
                    Debug.Log(""[Meta Quest VR Setup] Auto-dismissed feature set dialog: "" + title);
                    window.Close();
                }
            }
        }
        catch (System.Exception ex)
        {
            // Non-critical — if we can't dismiss it, the user can click manually
            Debug.Log(""[Meta Quest VR Setup] Could not auto-dismiss feature set dialog: "" + ex.Message);
        }
    }

    private static void ClosePackageManagerWindow()
    {
        try
        {
            var allWindows = Resources.FindObjectsOfTypeAll<EditorWindow>();
            foreach (var window in allWindows)
            {
                string typeName = window.GetType().FullName;
                string title = window.titleContent.text;
                // Unity's Package Manager window type is UnityEditor.PackageManager.UI.PackageManagerWindow
                // Also match by title in case the type name changes across Unity versions.
                if (typeName.Contains(""PackageManager"") || title == ""Package Manager"")
                {
                    window.Close();
                    Debug.Log(""[Meta Quest VR Setup] Closed Package Manager window."");
                    break;
                }
            }
        }
        catch { /* Non-critical */ }
    }

    private static void CloseProjectSetupToolWindow()
    {
        // The Meta XR SDK's [InitializeOnLoad] auto-opens the Project Setup Tool
        // window after the SDK is installed. Close it since our script handles
        // configuration automatically.
        try
        {
            var allWindows = Resources.FindObjectsOfTypeAll<EditorWindow>();
            foreach (var window in allWindows)
            {
                string typeName = window.GetType().FullName ?? """";
                string title = window.titleContent.text ?? """";
                if (typeName.Contains(""ProjectSetup"") ||
                    typeName.Contains(""OVRProjectSetup"") ||
                    title.Contains(""Project Setup""))
                {
                    window.Close();
                    Debug.Log(""[Meta Quest VR Setup] Closed Project Setup Tool window."");
                }
            }
        }
        catch { /* Non-critical */ }
    }

    private static void CleanupDuplicateXRFolders()
    {
        // Delete numbered XR folders (XR 1, XR 2, etc.) created during package installation.
        // Each XR package's [InitializeOnLoad] may create its own settings folder.
        // We keep Assets/XR/ (the canonical location) and delete the duplicates.
        for (int i = 1; i <= 10; i++)
        {
            string folder = ""Assets/XR "" + i;
            if (AssetDatabase.IsValidFolder(folder))
            {
                AssetDatabase.DeleteAsset(folder);
                Debug.Log(""[Meta Quest VR Setup] Removed duplicate folder: "" + folder);
            }
        }

        // Also clean up duplicate Settings subfolders (Settings 1, Settings 2, etc.)
        // Same root cause — multiple [InitializeOnLoad] handlers racing to create settings.
        for (int i = 1; i <= 10; i++)
        {
            string folder = ""Assets/XR/Settings "" + i;
            if (AssetDatabase.IsValidFolder(folder))
            {
                AssetDatabase.DeleteAsset(folder);
                Debug.Log(""[Meta Quest VR Setup] Removed duplicate folder: "" + folder);
            }
        }

        // If the registered config object pointed to a deleted folder, clear it
        // so GetOrCreateXRSettingsContainer can find the surviving one.
        string key = ""com.unity.xr.management.loader_settings"";
        XRGeneralSettingsPerBuildTarget existing = null;
        EditorBuildSettings.TryGetConfigObject(key, out existing);
        if (existing == null)
        {
            EditorBuildSettings.RemoveConfigObject(key);
        }
    }

    /// <summary>
    /// Gets the shared XR settings container, creating and persisting it if needed.
    /// This ensures a single Assets/XR/Settings/ folder is used for all build targets.
    /// </summary>
    private static XRGeneralSettingsPerBuildTarget GetOrCreateXRSettingsContainer()
    {
        string key = ""com.unity.xr.management.loader_settings"";

        // 1. Check if a container is already registered with EditorBuildSettings
        XRGeneralSettingsPerBuildTarget container = null;
        EditorBuildSettings.TryGetConfigObject(key, out container);
        if (container != null)
            return container;

        // 2. Search AssetDatabase — Unity's package init may have created one already
        string[] guids = AssetDatabase.FindAssets(""t:XRGeneralSettingsPerBuildTarget"");
        if (guids.Length > 0)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[0]);
            container = AssetDatabase.LoadAssetAtPath<XRGeneralSettingsPerBuildTarget>(path);
            if (container != null)
            {
                EditorBuildSettings.AddConfigObject(key, container, true);
                Debug.Log(""[Meta Quest VR Setup] Using existing XR settings at "" + path);
                return container;
            }
        }

        // 3. Create fresh — nothing exists yet
        if (!AssetDatabase.IsValidFolder(""Assets/XR""))
            AssetDatabase.CreateFolder(""Assets"", ""XR"");
        if (!AssetDatabase.IsValidFolder(""Assets/XR/Settings""))
            AssetDatabase.CreateFolder(""Assets/XR"", ""Settings"");

        container = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
        AssetDatabase.CreateAsset(container,
            ""Assets/XR/Settings/XRGeneralSettingsPerBuildTarget.asset"");
        EditorBuildSettings.AddConfigObject(key, container, true);
        AssetDatabase.SaveAssets();

        Debug.Log(""[Meta Quest VR Setup] Created XR settings container."");
        return container;
    }

    private static void EnableOpenXRForBuildTarget(BuildTargetGroup group)
    {
        // Use the shared container so both build targets share one Assets/XR/ folder
        var container = GetOrCreateXRSettingsContainer();
        string containerPath = AssetDatabase.GetAssetPath(container);

        // Look up settings on our container (instance method).
        // Do NOT use the static XRGeneralSettingsForBuildTarget() — it creates
        // its own container and saves it to disk, producing duplicate XR folders.
        var buildTargetSettings = container.SettingsForBuildTarget(group);

        if (buildTargetSettings == null)
        {
            buildTargetSettings = ScriptableObject.CreateInstance<XRGeneralSettings>();
            buildTargetSettings.name = ""XRGeneralSettings "" + group;
            container.SetSettingsForBuildTarget(group, buildTargetSettings);
            AssetDatabase.AddObjectToAsset(buildTargetSettings, containerPath);
        }

        // Create XR Manager Settings if needed
        if (buildTargetSettings.Manager == null)
        {
            var manager = ScriptableObject.CreateInstance<XRManagerSettings>();
            manager.name = ""XRManagerSettings "" + group;
            buildTargetSettings.Manager = manager;
            AssetDatabase.AddObjectToAsset(manager, containerPath);
        }

        // Ensure XR auto-starts on app launch. Without these, the app
        // launches as a flat 2D Android app instead of entering VR mode.
        buildTargetSettings.InitManagerOnStart = true;
        buildTargetSettings.Manager.automaticLoading = true;
        buildTargetSettings.Manager.automaticRunning = true;

        // Enable OpenXR loader if not already present
        var openXRLoaderType = typeof(UnityEngine.XR.OpenXR.OpenXRLoader);
        var loaders = buildTargetSettings.Manager.activeLoaders;
        bool hasOpenXR = false;
        foreach (var loader in loaders)
        {
            if (loader.GetType() == openXRLoaderType)
            {
                hasOpenXR = true;
                break;
            }
        }

        if (!hasOpenXR)
        {
            var openXRLoader = ScriptableObject.CreateInstance<UnityEngine.XR.OpenXR.OpenXRLoader>();
            openXRLoader.name = ""OpenXRLoader "" + group;
            AssetDatabase.AddObjectToAsset(openXRLoader, containerPath);
            buildTargetSettings.Manager.TryAddLoader(openXRLoader);
            Debug.Log($""[Meta Quest VR Setup] Enabled OpenXR loader for {group}."");
        }

        // Persist all changes
        EditorUtility.SetDirty(container);
        AssetDatabase.SaveAssets();
    }

    private static void EnumerateRemainingTasks(BuildTargetGroup resolvedTargetGroup)
    {
        // After Fix All, enumerate any tasks that remain unfixed so the
        // developer knows what to handle manually.
        var bf = System.Reflection.BindingFlags.Public |
                 System.Reflection.BindingFlags.NonPublic |
                 System.Reflection.BindingFlags.Static |
                 System.Reflection.BindingFlags.Instance;
        try
        {
            System.Type setupType = FindType(""OVRProjectSetup"");
            if (setupType == null) return;

            // Get tasks via Registry.GetTasks(btg) or OVRProjectSetup.GetTasks(btg)
            System.Collections.IEnumerable tasks = null;
            var registryProp = setupType.GetProperty(""Registry"", bf);
            if (registryProp != null)
            {
                var registry = registryProp.GetValue(null);
                if (registry != null)
                {
                    var getTasks = registry.GetType().GetMethod(""GetTasks"", bf);
                    if (getTasks != null)
                    {
                        var pp = getTasks.GetParameters();
                        if (pp.Length == 1 && pp[0].ParameterType == typeof(BuildTargetGroup))
                            tasks = getTasks.Invoke(registry, new object[] { resolvedTargetGroup })
                                    as System.Collections.IEnumerable;
                    }
                }
            }
            if (tasks == null)
            {
                var getTasks = setupType.GetMethod(""GetTasks"", bf);
                if (getTasks != null)
                {
                    var pp = getTasks.GetParameters();
                    if (pp.Length == 1 && pp[0].ParameterType == typeof(BuildTargetGroup))
                        tasks = getTasks.Invoke(null, new object[] { resolvedTargetGroup })
                                as System.Collections.IEnumerable;
                }
            }
            if (tasks == null) return;

            var remaining = new System.Collections.Generic.List<string>();
            foreach (var task in tasks)
            {
                if (task == null) continue;
                var tt = task.GetType();

                // Check IsDone(BuildTargetGroup)
                var isDone = tt.GetMethod(""IsDone"", bf, null,
                    new[] { typeof(BuildTargetGroup) }, null);
                if (isDone == null) continue;
                bool done = (bool)isDone.Invoke(task, new object[] { resolvedTargetGroup });
                if (done) continue;

                // Check IsIgnored(BuildTargetGroup)
                var isIgnored = tt.GetMethod(""IsIgnored"", bf, null,
                    new[] { typeof(BuildTargetGroup) }, null);
                if (isIgnored != null)
                {
                    bool ignored = (bool)isIgnored.Invoke(task, new object[] { resolvedTargetGroup });
                    if (ignored) continue;
                }

                // Get display name — OVRConfigurationTask has a Message property
                string taskName = """";
                foreach (var propName in new[] { ""Message"", ""message"", ""Name"", ""name"" })
                {
                    var prop = tt.GetProperty(propName, bf);
                    if (prop != null && prop.GetIndexParameters().Length == 0)
                    {
                        // Message property may take BuildTargetGroup
                        var getter = prop.GetGetMethod(true);
                        if (getter != null)
                        {
                            var gp = getter.GetParameters();
                            if (gp.Length == 0)
                                taskName = prop.GetValue(task) as string ?? """";
                        }
                        if (!string.IsNullOrEmpty(taskName)) break;
                    }
                }
                if (string.IsNullOrEmpty(taskName))
                    taskName = tt.Name;
                remaining.Add(taskName);
            }

            if (remaining.Count > 0)
            {
                Debug.Log(""[Meta Quest VR Setup] Remaining unfixed tasks ("" + remaining.Count +
                    "", run manually via Meta > Tools > Project Setup Tool):"");
                foreach (var r in remaining)
                    Debug.Log(""  - "" + r);
            }
            else
            {
                Debug.Log(""[Meta Quest VR Setup] All Project Setup Tool tasks resolved."");
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning(""[Meta Quest VR Setup] Could not enumerate remaining tasks: "" + ex.Message);
        }
    }
}
#endif
";
        // Ensure Editor directory exists
        string editorDir = Path.Combine(Application.dataPath, "Editor");
        if (!Directory.Exists(editorDir))
            Directory.CreateDirectory(editorDir);

        File.WriteAllText(_deferredScriptPath, script);
        Log("Created deferred XR config script (runs after Unity reloads).");
        // Clear phase BEFORE Refresh — AssetDatabase.Refresh() can trigger an
        // immediate domain reload (synchronous in some Unity versions) that kills
        // the current execution context. If ClearPhase() runs after Refresh(),
        // it may never execute, leaving KEY_PHASE stuck at PHASE_POST_PACKAGES
        // and causing CheckForResume() to loop.
        ClearPhase();
        AssetDatabase.Refresh();
    }

    private static void FinalStatus()
    {
        ClearPhase();

        // Close the Package Manager window if Unity auto-opened it
        ClosePackageManagerWindow();

        _statusMessage =
            "Packages installed. Unity is reloading to apply XR settings — " +
            "look for 'SETUP COMPLETE' in the Console.";
        Log("Packages installed. Unity will reload once more to configure XR. Look for 'SETUP COMPLETE' in Console.");
    }

    // NOTE: This ClosePackageManagerWindow() is intentionally duplicated from the
    // deferred script. The deferred script is a self-contained string literal that
    // can't reference methods in this class (it's written to disk and compiled
    // separately). Both copies are needed.
    private static void ClosePackageManagerWindow()
    {
        try
        {
            var allWindows = Resources.FindObjectsOfTypeAll<EditorWindow>();
            foreach (var window in allWindows)
            {
                string typeName = window.GetType().FullName;
                string title = window.titleContent.text;
                if (typeName.Contains("PackageManager") || title == "Package Manager")
                {
                    window.Close();
                    Log("Closed Package Manager window.");
                    break;
                }
            }
        }
        catch { }
    }

    // Also exists in the deferred config script (MetaQuestXRConfig_AutoRun).
    // Duplicated here because the dialog can appear after Core SDK installs,
    // before the deferred script is generated.
    private static void DismissFeatureSetDialogs()
    {
        try
        {
            var allWindows = Resources.FindObjectsOfTypeAll<EditorWindow>();
            foreach (var window in allWindows)
            {
                string title = window.titleContent.text;
                if (title.Contains("Feature Set") || title.Contains("Feature set") ||
                    title.Contains("Android Manifest"))
                {
                    Debug.Log("[Meta Quest VR Setup] Auto-dismissed SDK dialog: " + title);
                    window.Close();
                }
            }
        }
        catch (System.Exception ex)
        {
            Debug.Log("[Meta Quest VR Setup] Could not auto-dismiss feature set dialog: " + ex.Message);
        }
    }

    /// <summary>
    /// Re-activates the Meta Quest build profile after a domain reload reset it.
    /// Called from the static constructor via SessionState flag (set by the
    /// deferred config script before its self-deletion triggers a reload).
    /// </summary>
    private static void ReactivateMetaQuestProfile()
    {
        var bf = System.Reflection.BindingFlags.Public |
                 System.Reflection.BindingFlags.NonPublic |
                 System.Reflection.BindingFlags.Static |
                 System.Reflection.BindingFlags.Instance;
        try
        {
            System.Type buildProfileType = null;
            System.Type moduleUtilType = null;
            System.Type contextType = null;
            foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = asm.GetType("UnityEditor.Build.Profile.BuildProfile");
                if (t != null) buildProfileType = t;
                t = asm.GetType("UnityEditor.Build.Profile.BuildProfileModuleUtil");
                if (t != null) moduleUtilType = t;
                t = asm.GetType("UnityEditor.Build.Profile.BuildProfileContext");
                if (t != null) contextType = t;
            }
            if (buildProfileType == null || moduleUtilType == null || contextType == null) return;

            var findAll = moduleUtilType.GetMethod("FindAllViewablePlatforms", bf);
            if (findAll == null) return;
            var platforms = findAll.Invoke(null, null) as System.Collections.IList;
            if (platforms == null) return;

            var getDisplayName = moduleUtilType.GetMethod("GetClassicPlatformDisplayName", bf);
            System.Type guidType = null;
            var retType = findAll.ReturnType;
            if (retType.IsGenericType)
                guidType = retType.GetGenericArguments()[0];
            else if (retType.IsArray)
                guidType = retType.GetElementType();
            if (guidType == null && platforms.Count > 0)
                guidType = platforms[0].GetType();
            object questGuid = null;
            foreach (var p in platforms)
            {
                object guid = p;
                if (guidType != null && p.GetType() != guidType)
                {
                    var gp = p.GetType().GetProperty("guid", bf)
                        ?? p.GetType().GetProperty("platformGuid", bf);
                    if (gp != null) guid = gp.GetValue(p); else continue;
                }
                string name = "";
                try { if (getDisplayName != null) name = getDisplayName.Invoke(null, new[] { guid }) as string ?? ""; } catch { }
                if (name.IndexOf("Quest", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name == "Meta Quest" || name == "MetaQuest")
                { questGuid = guid; break; }
            }
            if (questGuid == null) return;

            var ctx = contextType.GetProperty("instance", bf)?.GetValue(null);
            if (ctx == null) return;
            var getOrCreate = contextType.GetMethod("GetOrCreateClassicPlatformBuildProfile", bf);
            if (getOrCreate == null) return;
            var profile = getOrCreate.Invoke(ctx, new[] { questGuid });
            if (profile == null) return;

            var setActive = buildProfileType.GetMethod("SetActiveBuildProfile", bf);
            if (setActive != null)
            {
                setActive.Invoke(null, new[] { profile });
                Log("Re-activated Meta Quest build profile.");
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning("[Meta Quest VR Setup] Could not re-activate Meta Quest profile: " +
                (ex.InnerException?.Message ?? ex.Message));
        }
    }

    private static void Log(string message)
    {
        _log.Add(message);
        Debug.Log("[Meta Quest VR Setup] " + message);

        // Append to persistent file so logs survive domain reloads
        try
        {
            File.AppendAllText(_logFilePath,
                $"[{System.DateTime.Now:HH:mm:ss}] {message}\n");
        }
        catch { }
    }
}

// Optional SDK selection window — shown after main setup completes.
// Lives in MetaQuestVRSetup.cs (not the deferred script) so it survives domain reloads.
// Install queue persisted via SessionState so it survives domain reloads triggered by
// package installation (each Client.Add can cause Unity to reload scripts).
public class MetaQuestOptionalSDKs : EditorWindow
{
    private static readonly string[][] optionalPackages = new string[][] {
        new[] { "com.meta.xr.sdk.interaction", "Interaction SDK (ISDK)" },
        new[] { "com.meta.xr.mrutilitykit", "MR Utility Kit (MRUK)" },
        new[] { "com.meta.xr.sdk.haptics", "Haptics SDK" },
        new[] { "com.meta.xr.sdk.audio", "Audio SDK" },
        new[] { "com.meta.xr.sdk.platform", "Platform SDK" }
    };

    private const string KEY_INSTALL_QUEUE = "MetaQuestOptionalSDKs_Queue";
    private const string KEY_INSTALL_INDEX = "MetaQuestOptionalSDKs_Index";
    private const string KEY_POST_INSTALL_PST = "MetaQuestOptionalSDKs_PostPST";

    private bool[] _selections;
    private bool _installing = false;
    private int _installIndex = 0;
    private AddRequest _addRequest;
    private List<string> _installQueue;

    public static void ShowWindow()
    {
        var window = GetWindow<MetaQuestOptionalSDKs>("Optional Meta XR SDKs");
        window.minSize = new Vector2(380, 260);
    }

    private void OnEnable()
    {
        _selections = new bool[optionalPackages.Length];

        // Check if post-install PST was pending (lost to domain reload)
        if (SessionState.GetBool(KEY_POST_INSTALL_PST, false))
        {
            SessionState.EraseBool(KEY_POST_INSTALL_PST);
            Debug.Log("[Meta Quest VR Setup] Resuming post-install PST after domain reload...");
            RunPostInstallPST();
            return;
        }

        // Resume interrupted install queue after domain reload
        string savedQueue = SessionState.GetString(KEY_INSTALL_QUEUE, "");
        if (!string.IsNullOrEmpty(savedQueue))
        {
            _installQueue = new List<string>(savedQueue.Split('\n'));
            _installIndex = SessionState.GetInt(KEY_INSTALL_INDEX, 0);
            if (_installIndex < _installQueue.Count)
            {
                _installing = true;
                Debug.Log($"[Meta Quest VR Setup] Resuming optional SDK install ({_installIndex + 1}/{_installQueue.Count})...");
                InstallNextOptional();
            }
            else
            {
                ClearInstallState();
            }
        }
    }

    private void OnGUI()
    {
        if (_selections == null) { OnEnable(); if (_selections == null) return; }

        GUILayout.Label("Optional Meta XR Packages", EditorStyles.boldLabel);
        GUILayout.Space(5);
        GUILayout.Label("Select additional SDKs to install:", EditorStyles.wordWrappedLabel);
        GUILayout.Space(10);

        GUI.enabled = !_installing;

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Select All", EditorStyles.miniButtonLeft))
            for (int i = 0; i < _selections.Length; i++) _selections[i] = true;
        if (GUILayout.Button("Deselect All", EditorStyles.miniButtonRight))
            for (int i = 0; i < _selections.Length; i++) _selections[i] = false;
        EditorGUILayout.EndHorizontal();

        GUILayout.Space(5);
        for (int i = 0; i < optionalPackages.Length; i++)
        {
            _selections[i] = EditorGUILayout.ToggleLeft(optionalPackages[i][1], _selections[i]);
        }

        GUILayout.Space(10);

        if (_installing)
        {
            int total = _installQueue != null ? _installQueue.Count : 0;
            GUILayout.Label($"Installing {_installIndex + 1}/{total}...", EditorStyles.miniLabel);
            GUILayout.Space(5);
        }

        if (!_installing)
        {
            if (GUILayout.Button("Install Selected", GUILayout.Height(30)))
            {
                _installQueue = new List<string>();
                for (int i = 0; i < _selections.Length; i++)
                {
                    if (_selections[i])
                        _installQueue.Add(optionalPackages[i][0]);
                }
                if (_installQueue.Count > 0)
                {
                    _installing = true;
                    _installIndex = 0;
                    // Persist queue so it survives domain reloads
                    SessionState.SetString(KEY_INSTALL_QUEUE, string.Join("\n", _installQueue));
                    SessionState.SetInt(KEY_INSTALL_INDEX, 0);
                    InstallNextOptional();
                }
                else
                {
                    EditorPrefs.DeleteKey(MetaQuestVRSetup.KEY_SHOW_OPTIONAL_SDKS);
                    Close();
                }
            }

            if (GUILayout.Button("Skip", GUILayout.Height(25)))
            {
                ClearInstallState();
                Close();
            }
        }
        GUI.enabled = true;
    }

    private void ClearInstallState()
    {
        _installing = false;
        SessionState.EraseString(KEY_INSTALL_QUEUE);
        SessionState.EraseInt(KEY_INSTALL_INDEX);
        EditorPrefs.DeleteKey(MetaQuestVRSetup.KEY_SHOW_OPTIONAL_SDKS);
        // Reset the "already shown" flag so re-running setup can show the window again
        SessionState.EraseBool("MetaQuestVRSetup_OptionalSDKsShown");
    }

    // ── UPM Readiness Check (instance version for optional SDKs) ────
    // Same pattern as MetaQuestVRSetup.WaitForUpmReady but as instance methods
    // since MetaQuestOptionalSDKs is an EditorWindow, not a static class.

    private ListRequest _optUpmReadyCheck;
    private double _optUpmWaitStart;
    private System.Action _optUpmReadyCallback;

    private void WaitForUpmReady(System.Action onReady)
    {
        _optUpmWaitStart = EditorApplication.timeSinceStartup;
        _optUpmReadyCallback = onReady;
        _optUpmReadyCheck = Client.List();
        EditorApplication.update += PollOptionalUpmReady;
    }

    private void PollOptionalUpmReady()
    {
        if (this == null)
        {
            EditorApplication.update -= PollOptionalUpmReady;
            return;
        }

        double elapsed = EditorApplication.timeSinceStartup - _optUpmWaitStart;

        if (elapsed >= 30.0)
        {
            EditorApplication.update -= PollOptionalUpmReady;
            Debug.Log("[Meta Quest VR Setup] UPM readiness check timed out (30s). Proceeding anyway.");
            _optUpmReadyCallback?.Invoke();
            return;
        }

        if (_optUpmReadyCheck == null || !_optUpmReadyCheck.IsCompleted)
            return;

        if (elapsed < 2.0)
            return; // enforce minimum delay

        EditorApplication.update -= PollOptionalUpmReady;

        if (_optUpmReadyCheck.Status == StatusCode.Success)
            Debug.Log($"[Meta Quest VR Setup] UPM ready after {elapsed:F1}s (optional SDKs).");
        else
            Debug.Log($"[Meta Quest VR Setup] UPM List() returned {_optUpmReadyCheck.Status}. Proceeding anyway.");

        _optUpmReadyCallback?.Invoke();
    }

    private void InstallNextOptional()
    {
        if (_installQueue == null || _installIndex >= _installQueue.Count)
        {
            Debug.Log("[Meta Quest VR Setup] Optional SDK installation complete.");
            // Persist PST flag BEFORE clearing state — the last package's domain reload
            // can fire before delayCall executes, killing any scheduled callbacks.
            // OnEnable checks this flag after the reload and runs PST from there.
            SessionState.SetBool(KEY_POST_INSTALL_PST, true);
            ClearInstallState();
            // Close Package Manager window that may have opened during installs
            EditorApplication.delayCall += () =>
            {
                var allWindows = Resources.FindObjectsOfTypeAll<EditorWindow>();
                foreach (var window in allWindows)
                {
                    string typeName = window.GetType().FullName;
                    string title = window.titleContent.text;
                    if (typeName.Contains("PackageManager") || title == "Package Manager")
                    {
                        window.Close();
                        break;
                    }
                }
            };
            // Run post-install PST passes for tasks added by optional SDKs
            // (e.g., MRUK adds "Scene Support should be Required"), then save scene.
            // If a domain reload kills this delayCall, OnEnable will pick it up via KEY_POST_INSTALL_PST.
            RunPostInstallPST();
            return;
        }

        // Persist current index so it survives domain reload
        SessionState.SetInt(KEY_INSTALL_INDEX, _installIndex);

        string pkg = _installQueue[_installIndex];
        Debug.Log($"[Meta Quest VR Setup] Installing optional package: {pkg}");
        // Wait for UPM to be ready before calling Client.Add().
        // Prevents lock contention errors on 2022.3.x.
        WaitForUpmReady(() =>
        {
            _addRequest = Client.Add(pkg);
            EditorApplication.update += OnOptionalInstallProgress;
        });
    }

    private void OnOptionalInstallProgress()
    {
        // Null guard: if user closed window mid-install, unregister and bail
        if (this == null)
        {
            EditorApplication.update -= OnOptionalInstallProgress;
            return;
        }
        if (_addRequest == null || !_addRequest.IsCompleted) return;
        EditorApplication.update -= OnOptionalInstallProgress;

        if (_addRequest.Status == StatusCode.Success)
            Debug.Log($"[Meta Quest VR Setup] Installed: {_addRequest.Result.packageId}");
        else
        {
            string failedPkg = (_installQueue != null && _installIndex < _installQueue.Count)
                ? _installQueue[_installIndex] : "unknown";
            Debug.LogWarning($"[Meta Quest VR Setup] Failed to install {failedPkg}: {_addRequest.Error?.message}");
        }

        _installIndex++;
        // Persist incremented index before potential domain reload
        SessionState.SetInt(KEY_INSTALL_INDEX, _installIndex);
        Repaint();
        InstallNextOptional();
    }

    // ── Post-Install PST Pass ──────────────────────────────────────────
    // Optional SDKs (e.g., MRUK) register new PST tasks that weren't present
    // during the deferred script's 4 Project Setup Tool passes. Run Fix All once more for
    // Android + Standalone to catch them, then save the scene and close.

    private void RunPostInstallPST()
    {
        // NOTE: Do NOT clear KEY_POST_INSTALL_PST here. A domain reload can kill
        // the delayCall below before it executes. The flag must persist until the
        // PST work actually completes, so OnEnable can retry after a reload.

        EditorApplication.delayCall += () =>
        {
            Debug.Log("[Meta Quest VR Setup] Running post-install Project Setup Tool Fix All (Android)...");
            RunPSTFixAll(BuildTargetGroup.Android);

            EditorApplication.delayCall += () =>
            {
                Debug.Log("[Meta Quest VR Setup] Running post-install Project Setup Tool Fix All (Standalone)...");
                RunPSTFixAll(BuildTargetGroup.Standalone);

                EditorApplication.delayCall += () =>
                {
                    EnumerateRemainingPSTTasks(BuildTargetGroup.Android);

                    // PST work is done — clear the flag so OnEnable won't re-run
                    SessionState.EraseBool(KEY_POST_INSTALL_PST);

                    // Save scene after all fixes
                    var scene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
                    if (scene.isDirty)
                    {
                        UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
                        Debug.Log("[Meta Quest VR Setup] Scene saved (post-optional SDKs).");
                    }

                    Debug.Log("[Meta Quest VR Setup] All done.");
                    Close();
                };
            };
        };
    }

    private static System.Type FindPSTType(string typeName)
    {
        foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
        {
            System.Type[] types = null;
            try { types = asm.GetTypes(); }
            catch (System.Reflection.ReflectionTypeLoadException ex) { types = ex.Types; }
            catch { continue; }
            if (types == null) continue;
            foreach (var t in types)
            {
                if (t != null && t.Name == typeName)
                    return t;
            }
        }
        return null;
    }

    private static void RunPSTFixAll(BuildTargetGroup targetGroup)
    {
        try
        {
            try { System.Reflection.Assembly.Load("Oculus.VR.Editor"); } catch { }

            System.Type setupType = FindPSTType("OVRProjectSetup");
            if (setupType == null)
            {
                Debug.Log("[Meta Quest VR Setup] OVRProjectSetup not found. Skipping post-install PST.");
                return;
            }

            var bf = System.Reflection.BindingFlags.Public |
                     System.Reflection.BindingFlags.NonPublic |
                     System.Reflection.BindingFlags.Static;

            var fixTasksMethod = setupType.GetMethod("FixTasks", bf);
            if (fixTasksMethod != null)
            {
                var parameters = fixTasksMethod.GetParameters();
                if (parameters.Length >= 1 && parameters[0].ParameterType == typeof(BuildTargetGroup))
                {
                    object[] args = new object[parameters.Length];
                    args[0] = targetGroup;
                    for (int i = 1; i < parameters.Length; i++)
                    {
                        if (parameters[i].ParameterType == typeof(bool) &&
                            (parameters[i].Name == "blocking" || parameters[i].Name == "isBlocking"))
                            args[i] = true;
                        else if (parameters[i].HasDefaultValue)
                            args[i] = parameters[i].DefaultValue;
                        else
                            args[i] = null;
                    }

                    fixTasksMethod.Invoke(null, args);
                    Debug.Log($"[Meta Quest VR Setup] Post-install PST: FixTasks executed ({targetGroup}).");
                    return;
                }
            }

            Debug.Log("[Meta Quest VR Setup] FixTasks method not found. Run manually: Meta > Tools > Project Setup Tool.");
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[Meta Quest VR Setup] Post-install PST failed: {ex.Message}");
        }
    }

    private static void EnumerateRemainingPSTTasks(BuildTargetGroup targetGroup)
    {
        var bf = System.Reflection.BindingFlags.Public |
                 System.Reflection.BindingFlags.NonPublic |
                 System.Reflection.BindingFlags.Static |
                 System.Reflection.BindingFlags.Instance;
        try
        {
            System.Type setupType = FindPSTType("OVRProjectSetup");
            if (setupType == null) return;

            System.Collections.IEnumerable tasks = null;
            var registryProp = setupType.GetProperty("Registry", bf);
            if (registryProp != null)
            {
                var registry = registryProp.GetValue(null);
                if (registry != null)
                {
                    var getTasks = registry.GetType().GetMethod("GetTasks", bf);
                    if (getTasks != null)
                    {
                        var pp = getTasks.GetParameters();
                        if (pp.Length == 1 && pp[0].ParameterType == typeof(BuildTargetGroup))
                            tasks = getTasks.Invoke(registry, new object[] { targetGroup })
                                    as System.Collections.IEnumerable;
                    }
                }
            }
            if (tasks == null) return;

            var remaining = new List<string>();
            foreach (var task in tasks)
            {
                if (task == null) continue;
                var tt = task.GetType();

                var isDone = tt.GetMethod("IsDone", bf, null,
                    new[] { typeof(BuildTargetGroup) }, null);
                if (isDone == null) continue;
                bool done = (bool)isDone.Invoke(task, new object[] { targetGroup });
                if (done) continue;

                var isIgnored = tt.GetMethod("IsIgnored", bf, null,
                    new[] { typeof(BuildTargetGroup) }, null);
                if (isIgnored != null)
                {
                    bool ignored = (bool)isIgnored.Invoke(task, new object[] { targetGroup });
                    if (ignored) continue;
                }

                string taskName = "";
                foreach (var propName in new[] { "Message", "message", "Name", "name" })
                {
                    var prop = tt.GetProperty(propName, bf);
                    if (prop != null && prop.GetIndexParameters().Length == 0)
                    {
                        var getter = prop.GetGetMethod(true);
                        if (getter != null && getter.GetParameters().Length == 0)
                            taskName = prop.GetValue(task) as string ?? "";
                        if (!string.IsNullOrEmpty(taskName)) break;
                    }
                }
                if (string.IsNullOrEmpty(taskName))
                    taskName = tt.Name;
                remaining.Add(taskName);
            }

            if (remaining.Count > 0)
            {
                Debug.Log($"[Meta Quest VR Setup] Remaining unfixed tasks ({remaining.Count}, " +
                    "run manually via Meta > Tools > Project Setup Tool):");
                foreach (var r in remaining)
                    Debug.Log("  - " + r);
            }
            else
            {
                Debug.Log("[Meta Quest VR Setup] All post-install Project Setup Tool tasks resolved.");
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[Meta Quest VR Setup] Could not enumerate remaining tasks: {ex.Message}");
        }
    }
}

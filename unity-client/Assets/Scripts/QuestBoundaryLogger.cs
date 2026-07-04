using System.Text;
using UnityEngine;

public class QuestBoundaryLogger : MonoBehaviour
{
    private string _guiText = "Waiting...";
    private bool _logged = false;
    private float _retryTimer = 0f;

    void Update()
    {
        if (_logged) return;
        _retryTimer += Time.deltaTime;
        if (_retryTimer < 1f) return;
        _retryTimer = 0f;
        TryLog();
    }

    void TryLog()
    {
        var boundary = OVRManager.boundary;
        if (boundary == null) { _guiText = "boundary is null"; return; }

        bool configured = boundary.GetConfigured();
        Debug.Log($"[BoundaryLogger] GetConfigured: {configured}");

        if (!configured)
        {
            _guiText = "GetConfigured=False, retrying...";
            return;
        }

        _logged = true;
        var sb = new StringBuilder();
        sb.AppendLine($"Configured: {configured}");

        Vector3 dims = boundary.GetDimensions(OVRBoundary.BoundaryType.PlayArea);
        Debug.Log($"[BoundaryLogger] GetDimensions(PlayArea): {dims}");
        sb.AppendLine($"Dims: {dims.x:F3}, {dims.y:F3}, {dims.z:F3}");

        Vector3[] pts = boundary.GetGeometry(OVRBoundary.BoundaryType.PlayArea);
        if (pts == null || pts.Length == 0)
        {
            Debug.Log("[BoundaryLogger] GetGeometry(PlayArea): null or empty");
            sb.AppendLine("Geometry: none");
        }
        else
        {
            Debug.Log($"[BoundaryLogger] GetGeometry(PlayArea): {pts.Length} points");
            sb.AppendLine($"Geometry ({pts.Length} pts):");
            for (int i = 0; i < pts.Length; i++)
            {
                string line = $"  [{i}] {pts[i].x:F3}, {pts[i].y:F3}, {pts[i].z:F3}";
                Debug.Log($"[BoundaryLogger] {line}");
                sb.AppendLine(line);
            }
        }

        _guiText = sb.ToString();
    }

    void OnGUI()
    {
        GUI.skin.label.fontSize = 28;
        GUI.Label(new Rect(20, 20, Screen.width - 40, Screen.height - 40), _guiText);
    }
}

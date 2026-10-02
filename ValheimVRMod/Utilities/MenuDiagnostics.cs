using System.Linq;
using System.Text;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.Video;
using ValheimVRMod.VRCore;
using ValheimVRMod.VRCore.UI;

namespace ValheimVRMod.Utilities
{
    // Temporary diagnostics (local build only): logs the cameras, GUI panels, rigs and the cinematic's video state
    // while in the start scene, to find why the intro is pink/black and the main menu shows twice.
    // Lines start with [VHVR-DIAG]. Dumps on scene load, when a cinematic starts/stops, every 2 s while one plays,
    // every 5 s otherwise for the first 2 minutes after a scene load (while no local player), and on F8.
    class MenuDiagnostics : MonoBehaviour
    {
        private float sceneLoadedAt;
        private float nextDump;
        private bool wasPlaying;

        void Awake()
        {
            SceneManager.sceneLoaded += (scene, mode) =>
            {
                sceneLoadedAt = Time.realtimeSinceStartup;
                Dump("scene loaded: " + scene.name);
                nextDump = Time.realtimeSinceStartup + 1f;
            };
            sceneLoadedAt = Time.realtimeSinceStartup;
        }

        void LateUpdate()
        {
            bool playing = CinematicsManager.IsStartedPlaying();
            float now = Time.realtimeSinceStartup;
            if (playing != wasPlaying)
            {
                wasPlaying = playing;
                Dump(playing ? "cinematic started" : "cinematic stopped");
                nextDump = now + 0.5f;
                return;
            }
            if (ZInput.GetKeyDown(KeyCode.F8, true))
            {
                Dump("F8");
                return;
            }
            bool menu = Player.m_localPlayer == null && now - sceneLoadedAt < 120f;
            if ((playing || menu) && now >= nextDump)
            {
                Dump(playing ? "playing" : "menu");
                nextDump = now + (playing ? 2f : 5f);
            }
        }

        private static string PathOf(Transform t)
        {
            var sb = new StringBuilder(t.name);
            for (var p = t.parent; p != null; p = p.parent) sb.Insert(0, p.name + "/");
            return sb.ToString();
        }

        private static string V(Vector3 v) => $"({v.x:F2},{v.y:F2},{v.z:F2})";

        private static object Get(object o, string field)
        {
            try { return o == null ? null : Traverse.Create(o).Field(field).GetValue(); }
            catch { return null; }
        }

        private static object GetStatic(System.Type t, string field)
        {
            try { return Traverse.Create(t).Field(field).GetValue(); }
            catch { return null; }
        }

        private static void Dump(string why)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"[VHVR-DIAG] ===== {why} | t={Time.realtimeSinceStartup:F1} frame={Time.frameCount} " +
                          $"scene={SceneManager.GetActiveScene().name} localPlayer={(Player.m_localPlayer != null)} " +
                          $"cinematic={CinematicsManager.IsStartedPlaying()} VRPlayer.instance={(VRPlayer.instance != null ? PathOf(VRPlayer.instance.transform) : "null")} " +
                          $"vrCam={(VRPlayer.vrCam != null ? PathOf(VRPlayer.vrCam.transform) : "null")} readyForCinematic={VRGUI.isReadyToShowCinematic}");
            try
            {
                foreach (var c in Resources.FindObjectsOfTypeAll<Camera>().Where(c => c.gameObject.scene.IsValid()))
                {
                    var tt = c.targetTexture;
                    sb.AppendLine($"[VHVR-DIAG] cam '{PathOf(c.transform)}' en={c.enabled} active={c.gameObject.activeInHierarchy} depth={c.depth} " +
                                  $"eye={c.stereoTargetEye} stereo={c.stereoEnabled} target={(tt != null ? tt.name + " " + tt.width + "x" + tt.height : "screen")} " +
                                  $"mask=0x{c.cullingMask:X8} clear={c.clearFlags} ortho={c.orthographic} pos={V(c.transform.position)} scene={c.gameObject.scene.name}");
                }
                int uiPanelLayer = LayerUtils.getUiPanelLayer();
                foreach (var r in Resources.FindObjectsOfTypeAll<Renderer>().Where(r => r.gameObject.scene.IsValid() &&
                         (r.gameObject.layer == uiPanelLayer || r.name.Contains("Panel") || r.name.Contains("Proxy"))))
                {
                    var m = r.sharedMaterial;
                    sb.AppendLine($"[VHVR-DIAG] renderer '{PathOf(r.transform)}' layer={r.gameObject.layer} en={r.enabled} active={r.gameObject.activeInHierarchy} " +
                                  $"pos={V(r.transform.position)} scale={V(r.transform.lossyScale)} shader={(m != null && m.shader != null ? m.shader.name : "none")} " +
                                  $"tex={(m != null && m.mainTexture != null ? m.mainTexture.name + " " + m.mainTexture.width + "x" + m.mainTexture.height : "none")}");
                }
                var players = Resources.FindObjectsOfTypeAll<Valve.VR.InteractionSystem.Player>().Where(p => p.gameObject.scene.IsValid()).ToList();
                sb.AppendLine($"[VHVR-DIAG] SteamVR players={players.Count}: " + string.Join(", ", players.Select(p => PathOf(p.transform) + (p.gameObject.activeInHierarchy ? "" : "(inactive)"))));
                var guis = Resources.FindObjectsOfTypeAll<VRGUI>().Where(g => g.gameObject.scene.IsValid()).ToList();
                sb.AppendLine($"[VHVR-DIAG] VRGUIs={guis.Count}; EventSystems=" + string.Join(", ",
                    Resources.FindObjectsOfTypeAll<EventSystem>().Where(e => e.gameObject.scene.IsValid())
                        .Select(e => PathOf(e.transform) + (e.isActiveAndEnabled ? "" : "(off)") + (EventSystem.current == e ? "*" : ""))));
                foreach (var g in guis)
                {
                    var panelCam = Get(g, "_uiPanelCamera") as Camera;
                    var anchored = Get(g, "anchoredUiPanelCamera") as Camera;
                    var proxy = Get(g, "_handPanelProxy") as Renderer;
                    var uiPanel = GetStatic(typeof(VRGUI), "_uiPanel") as Transform;
                    sb.AppendLine($"[VHVR-DIAG] VRGUI uiPanel={(uiPanel != null ? PathOf(uiPanel) + " " + V(uiPanel.position) : "null")} " +
                                  $"panelCam={(panelCam != null ? PathOf(panelCam.transform) : "null")} anchored={(anchored != null)} " +
                                  $"proxy={(proxy != null ? proxy.enabled + " " + V(proxy.transform.position) : "null")} " +
                                  $"configuredGuiCam={GetStatic(typeof(VRGUI), "hasConfiguredGuiCamera")}");
                }
                var cm = CinematicsManager.s_instance;
                if (cm != null)
                {
                    VideoPlayer vp = cm.m_videoPlayer;
                    var cmCam = Get(cm, "m_camera") as Camera;
                    var mainCam = Get(cm, "m_mainCamera") as Camera;
                    sb.AppendLine($"[VHVR-DIAG] video clip={(vp.clip != null ? vp.clip.name + " " + vp.clip.width + "x" + vp.clip.height : "null")} mode={vp.renderMode} " +
                                  $"target={(vp.targetTexture != null ? vp.targetTexture.name + " " + vp.targetTexture.width + "x" + vp.targetTexture.height + " created=" + vp.targetTexture.IsCreated() : "null")} " +
                                  $"targetCam={(vp.targetCamera != null ? PathOf(vp.targetCamera.transform) : "null")} playing={vp.isPlaying} prepared={vp.isPrepared} frame={vp.frame} " +
                                  $"texture={(vp.texture != null ? vp.texture.width + "x" + vp.texture.height : "null")} source={vp.source} " +
                                  $"cmCam={(cmCam != null ? cmCam.enabled.ToString() : "null")} m_mainCamera={(mainCam != null ? PathOf(mainCam.transform) : "null")}");
                    var screen = cm.GetComponent<VRCinematicScreen>();
                    if (screen != null)
                    {
                        var canvas = Get(screen, "screenCanvas") as Canvas;
                        var img = Get(screen, "videoImage") as UnityEngine.UI.RawImage;
                        var rt = Get(screen, "videoTexture") as RenderTexture;
                        sb.AppendLine($"[VHVR-DIAG] cinematic screen canvas={(canvas != null ? canvas.gameObject.activeInHierarchy + " cam=" + (canvas.worldCamera != null ? canvas.worldCamera.name : "null") + " pos=" + V(canvas.transform.position) + " layer=" + canvas.gameObject.layer : "null")} " +
                                      $"image tex={(img != null && img.texture != null ? img.texture.name + " " + img.texture.width + "x" + img.texture.height : "null")} " +
                                      $"rt={(rt != null ? rt.width + "x" + rt.height + " fmt=" + rt.format + " created=" + rt.IsCreated() + " sameAsTarget=" + (rt == vp.targetTexture) : "null")}");
                    }
                    else
                    {
                        sb.AppendLine("[VHVR-DIAG] no VRCinematicScreen on CinematicsManager");
                    }
                }
                foreach (var fbs in Resources.FindObjectsOfTypeAll<FrameBufferScaler>().Where(f => f.gameObject.scene.IsValid()))
                {
                    sb.AppendLine($"[VHVR-DIAG] FrameBufferScaler '{PathOf(fbs.transform)}' active={fbs.gameObject.activeInHierarchy}");
                }
            }
            catch (System.Exception e)
            {
                sb.AppendLine("[VHVR-DIAG] dump failed: " + e);
            }
            Debug.Log(sb.ToString());
        }
    }
}

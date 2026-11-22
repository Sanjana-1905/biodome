using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using BioDome.CameraSystem;
using BioDome.Plants;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BioDome.EditorTools.Tests
{
    /// <summary>
    /// Plays the REAL scene and renders the scene's own Game camera (placed by the real CameraController) to PNG files at set moments:
    /// healthy, wilted, recovering and flowering plants, and a seedling at the start and end of the run. Overlay UI is not part of a camera render.
    /// Changes no asset and does not save the scene. Command line:
    /// -executeMethod BioDome.EditorTools.Tests.PlayModeShotProbe.Run -shotOut DIR [-shotSpeed 4] [-shotSeconds 115]
    /// </summary>
    public static class PlayModeShotProbe
    {
        private sealed class Shot { public float At; public string Name; public System.Func<PlantController> Who; public float Distance = 2.4f; public float Yaw = 30f; public float Pitch = 16f; public bool Done; public System.Func<bool> Ready; }

        private static float startReal = -1f;
        private static string outDir;
        private static int frames, waitFrames;
        private static Shot active;
        private static List<Shot> shots;
        private static readonly Dictionary<string, PlantController> subjects = new Dictionary<string, PlantController>();
        private static readonly StringBuilder log = new StringBuilder("shot,real_s,sim_hours,species,growth,health,wilt,bloom,water,condition\n");
        private static bool dried, watered; private static float endAt, waterTime = -1f, nowT;

        public static void Run()
        {
            outDir = Arg("-shotOut") ?? "Shots";
            Directory.CreateDirectory(outDir);
            endAt = float.Parse(Arg("-shotSeconds") ?? "115", CultureInfo.InvariantCulture);
            EditorSceneManager.OpenScene("Assets/Scenes/BioDome_Working.unity");
            startReal = -1f; frames = 0; waitFrames = 0; active = null; dried = watered = false; waterTime = -1f; subjects.Clear(); shots = null;
            EditorApplication.update -= OnUpdate;
            EditorApplication.update += OnUpdate;
            EditorApplication.isPlaying = true;
        }

        private static void OnUpdate()
        {
            if (!EditorApplication.isPlaying) return;
            var manager = Object.FindFirstObjectByType<BioDome.Core.GreenhouseManager>();
            var camCtl = Object.FindFirstObjectByType<CameraController>();
            if (manager == null || camCtl == null || Camera.main == null) return;
            var all = Object.FindObjectsByType<PlantController>(FindObjectsSortMode.None);
            if (all.Length == 0 || !all.All(p => p.IsInitialized)) return;
            frames++;

            if (startReal < 0f)
            {
                startReal = Time.realtimeSinceStartup;
                typeof(BioDome.Core.GreenhouseManager).GetField("simulatedHoursPerSecond", BindingFlags.NonPublic | BindingFlags.Instance)
                    .SetValue(manager, float.Parse(Arg("-shotSpeed") ?? "4", CultureInfo.InvariantCulture));
                PlantController Pick(string id, bool mature, int skip = 0) => all.Where(p => p.ZoneProfile == null && p.Data.SpeciesId == id && (mature ? p.Simulation.Growth01 >= 0.9f : p.Simulation.Growth01 < 0.2f))
                    .OrderBy(p => p.name).Skip(skip).FirstOrDefault();
                subjects["healthy_monstera"] = Pick("monstera", true, 0);
                subjects["wilt_monstera"] = Pick("monstera", true, 1);
                subjects["healthy_banana"] = Pick("banana", true, 0);
                subjects["wilt_banana"] = Pick("banana", true, 1);
                subjects["healthy_calathea"] = Pick("calathea", true, 0);
                subjects["recover_calathea"] = Pick("calathea", true, 1);
                subjects["anthurium"] = Pick("anthurium", true, 0);
                subjects["orchid"] = Pick("orchid", true, 0);
                subjects["bird"] = Pick("bird_of_paradise", true, 0);
                subjects["seedling"] = Pick("monstera", false, 0);
                shots = new List<Shot>
                {
                    new Shot { At = 0.5f, Name = "01_healthy_monstera", Who = () => subjects["healthy_monstera"], Distance = 2.6f },
                    new Shot { At = 0.5f, Name = "02_healthy_banana", Who = () => subjects["healthy_banana"], Distance = 4.5f },
                    new Shot { At = 0.5f, Name = "03_healthy_calathea", Who = () => subjects["healthy_calathea"], Distance = 2.0f },
                    new Shot { At = 0.5f, Name = "04_flowering_anthurium", Who = () => subjects["anthurium"], Distance = 1.7f },
                    new Shot { At = 0.5f, Name = "05_flowering_orchid", Who = () => subjects["orchid"], Distance = 1.3f },
                    new Shot { At = 0.5f, Name = "06_flowering_bird_of_paradise", Who = () => subjects["bird"], Distance = 3.2f },
                    new Shot { At = 0.5f, Name = "07_seedling_monstera_start", Who = () => subjects["seedling"], Distance = 1.6f },
                    new Shot { At = 3.0f, Name = "08_wilted_monstera", Who = () => subjects["wilt_monstera"], Distance = 2.6f },
                    new Shot { At = 3.0f, Name = "09_wilted_banana", Who = () => subjects["wilt_banana"], Distance = 4.5f },
                    new Shot { At = 3.0f, Name = "10_wilted_calathea", Who = () => subjects["recover_calathea"], Distance = 2.0f },
                    new Shot { At = 3.0f, Ready = () => waterTime > 0f && nowT >= waterTime + 3.5f, Name = "11_recovering_calathea", Who = () => subjects["recover_calathea"], Distance = 2.0f },
                    new Shot { At = 3.0f, Ready = () => waterTime > 0f && nowT >= waterTime + 3.5f, Name = "12_recovering_monstera", Who = () => subjects["wilt_monstera"], Distance = 2.6f },
                    new Shot { At = endAt - 1f, Name = "13_seedling_monstera_end", Who = () => subjects["seedling"], Distance = 1.6f },
                };
            }

            float t = Time.realtimeSinceStartup - startReal;
            nowT = t;
            if (!watered)
            {
                bool wiltShotsDone = shots.Where(x => x.Name.StartsWith("08_") || x.Name.StartsWith("09_") || x.Name.StartsWith("10_")).All(x => x.Done);
                if (!wiltShotsDone) { foreach (var k in new[] { "wilt_monstera", "wilt_banana", "recover_calathea" }) if (subjects[k] != null) SetWater(subjects[k], Mathf.Min(subjects[k].Simulation.Water01, 0.02f)); }
                else { watered = true; waterTime = t; foreach (var k in new[] { "wilt_monstera", "wilt_banana", "recover_calathea" }) subjects[k]?.ReceiveWater(0.6f * subjects[k].Data.WaterCapacityLitres); }
            }

            if (active != null)
            {
                if (--waitFrames <= 0) { Capture(active, camCtl, t); active.Done = true; active = null; }
            }
            else
            {
                var next = shots.FirstOrDefault(s => !s.Done && t >= s.At && (s.Ready == null || s.Ready()));
                if (next != null)
                {
                    var p = next.Who();
                    if (p == null) { next.Done = true; log.AppendLine(next.Name + ",,missing subject"); }
                    else { camCtl.SnapTo(p.transform.position + Vector3.up * 0.35f, next.Distance, next.Yaw, next.Pitch); active = next; waitFrames = 4; }
                }
            }

            if (t >= endAt && shots.All(s => s.Done))
            {
                File.WriteAllText(Path.Combine(outDir, "shots.csv"), log.ToString());
                EditorApplication.update -= OnUpdate;
                EditorApplication.Exit(0);
            }
        }

        private static void Capture(Shot shot, CameraController ctl, float t)
        {
            Camera cam = Camera.main;
            var rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32);
            var prevTarget = cam.targetTexture;
            cam.targetTexture = rt; cam.Render(); cam.targetTexture = prevTarget;
            RenderTexture.active = rt;
            var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); tex.Apply();
            RenderTexture.active = null;
            File.WriteAllBytes(Path.Combine(outDir, shot.Name + ".png"), tex.EncodeToPNG());
            Object.DestroyImmediate(tex); rt.Release(); Object.DestroyImmediate(rt);

            var p = shot.Who(); var s = p.Simulation;
            string cond = !s.IsAlive ? "Dead" : s.IsRecovering && (s.Wilt01 > 0.05f || s.Health01 < s.Data.HealthyThreshold) ? "Recovering" : s.Wilt01 > 0.25f ? "Wilting" : s.State.ToString();
            var envc = Object.FindFirstObjectByType<BioDome.Environment.EnvironmentController>().Current;
            log.AppendLine(string.Join(",", new[] { shot.Name, t.ToString("0.0", CultureInfo.InvariantCulture), (envc.DayCount * 24f + envc.TimeOfDayHours).ToString("0.0", CultureInfo.InvariantCulture), p.Data.SpeciesId, F(s.Growth01), F(s.Health01), F(s.Wilt01), F(s.Bloom01), F(s.Water01), cond }));
        }

        private static void SetWater(PlantController p, float value) =>
            typeof(BioDome.Simulation.PlantSimulation).GetProperty("Water01").GetSetMethod(true).Invoke(p.Simulation, new object[] { value });

        private static string F(float v) => v.ToString("0.000", CultureInfo.InvariantCulture);

        private static string Arg(string name)
        {
            string[] args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == name) return args[i + 1];
            return null;
        }
    }
}

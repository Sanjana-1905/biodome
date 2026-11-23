using BioDome.Core;
using BioDome.Plants;
using UnityEngine;

namespace BioDome.Experiment
{
    /// <summary>
    /// Makes a pod's condition visible inside its glass chamber, driven by the pod's LIVE profile and its plants' measured soil moisture (so it follows the simulation
    /// and the pod controls). Heat: warm glow and rising heat shimmer. Low humidity: drifting dry dust. Low light: a dark shade cloth under the lid.
    /// Dry soil: the soil surface pales as the pod's average soil moisture falls. Added at run time to each pod; adds no simulation and no colliders.
    /// </summary>
    [DisallowMultipleComponent]
    public class PodVisualCues : MonoBehaviour
    {
        private ExperimentPod pod;
        private ParticleSystem heat, dust;
        private Light glow;
        private Renderer shade, soil, air, lamp;
        private Material shadeMat, soilMat, airMat, lampMat, ledMat;
        private float shadeAlpha, soilAlpha, glowTarget, timer, airAlpha, lampAlpha;
        private Color airColor = Color.clear;
        private static Texture2D softTexture;

        private void Start()
        {
            pod = GetComponent<ExperimentPod>();
            if (pod == null) { enabled = false; return; }
            var root = new GameObject("RuntimeCues").transform;
            root.SetParent(transform, false);

            heat = MakeParticles(root, "HeatShimmer", new Vector3(0f, 0.35f, 0f), new Vector3(2.0f, 0.1f, 1.6f), new Color(1f, 0.55f, 0.2f, 0.35f), 0.10f, 0.35f, new Vector3(0f, 0.55f, 0f), 3.2f);
            dust = MakeParticles(root, "DryDust", new Vector3(0f, 0.7f, 0f), new Vector3(2.2f, 0.7f, 1.8f), new Color(0.82f, 0.70f, 0.48f, 0.35f), 0.05f, 0.12f, new Vector3(0.06f, 0.02f, 0.04f), 5.5f);

            var lightObject = new GameObject("HeatGlow");
            lightObject.transform.SetParent(root, false);
            lightObject.transform.localPosition = new Vector3(0f, 1.1f, 0f);
            glow = lightObject.AddComponent<Light>();
            glow.type = LightType.Point; glow.color = new Color(1f, 0.5f, 0.2f); glow.range = 3.2f; glow.intensity = 0f; glow.shadows = LightShadows.None;

            shade = MakeSlab(root, "ShadeCloth", new Vector3(0f, 1.77f, 0f), new Vector3(2.8f, 0.012f, 2.4f), new Color(0.02f, 0.03f, 0.04f, 0f), out shadeMat);
            air = MakeSlab(root, "AirTint", new Vector3(0f, 0.95f, 0f), new Vector3(2.9f, 1.65f, 2.5f), new Color(1f, 1f, 1f, 0f), out airMat);
            lamp = MakeSlab(root, "HeatLamps", new Vector3(0f, 1.72f, 0f), new Vector3(2.5f, 0.05f, 0.5f), new Color(1f, 0.45f, 0.1f, 0f), out lampMat);
            MakePresentation();
            soil = MakeSlab(root, "DrySoil", new Vector3(0f, 0.306f, 0f), new Vector3(2.2f, 0.004f, 1.8f), new Color(0.78f, 0.66f, 0.46f, 0f), out soilMat);
        }

        private void Update()
        {
            if (pod == null || pod.Profile == null) return;
            timer -= Time.unscaledDeltaTime;
            if (timer <= 0f)
            {
                timer = 0.2f;
                ZoneProfileValues(out float tempOffset, out float humidityOffset, out float light);
                // Environment: from the conditions the pod's plants actually receive (live: follows the pod controls immediately).
                PlantData data = pod.Plants.Count > 0 && pod.Plants[0] != null ? pod.Plants[0].Data : null;
                EnvironmentConditions c = pod.Plants.Count > 0 && pod.Plants[0] != null ? pod.Plants[0].LastConditions : default;
                float hot = 0f, dry = 0f, dim = 0f;
                if (data != null && pod.Plants[0].IsInitialized)
                {
                    hot = Mathf.Clamp01((c.TemperatureCelsius - data.IdealTemperatureCelsius.y) / 5f);
                    dry = Mathf.Clamp01((data.IdealHumidity01.x - c.Humidity01) / 0.2f);
                    dim = Mathf.Clamp01((data.IdealLight01.x + 0.1f - c.Light01) / 0.25f);
                }
                SetRate(heat, hot * 40f);
                SetRate(dust, dry * 24f);
                glowTarget = hot * 3.2f;
                lampAlpha = hot * 0.9f;
                shadeAlpha = Mathf.Max(Mathf.Clamp01(1f - light) * 0.8f, dim * 0.85f);
                float aHeat = hot * 0.20f, aDry = dry * 0.24f, aDim = dim * 0.42f;
                airAlpha = Mathf.Clamp01(aHeat + aDry + aDim);
                Color mix = airAlpha > 0.001f ? (new Color(1f, 0.55f, 0.2f) * aHeat + new Color(0.9f, 0.78f, 0.5f) * aDry + new Color(0.03f, 0.05f, 0.10f) * aDim) / airAlpha : Color.white;
                airColor = new Color(mix.r, mix.g, mix.b, 1f);
                ExperimentPod.Metrics m = pod.Measure();
                soilAlpha = Mathf.Clamp01((0.45f - m.Moisture) / 0.2f) * 0.8f; // pale, dry-looking soil only once the pod's soil is drying below normal
                UpdateLed(m.Stress);
            }
            float k = 1f - Mathf.Exp(-4f * Time.unscaledDeltaTime);
            SetAlpha(shadeMat, Mathf.Lerp(CurrentAlpha(shadeMat), shadeAlpha, k));
            SetAlpha(soilMat, Mathf.Lerp(CurrentAlpha(soilMat), soilAlpha, k));
            glow.intensity = Mathf.Lerp(glow.intensity, glowTarget, k);
            SetColorAlpha(airMat, airColor, Mathf.Lerp(CurrentAlpha(airMat), airAlpha, k));
            SetAlpha(lampMat, Mathf.Lerp(CurrentAlpha(lampMat), lampAlpha, k));
            air.enabled = CurrentAlpha(airMat) > 0.01f;
            lamp.enabled = CurrentAlpha(lampMat) > 0.02f;
            shade.enabled = CurrentAlpha(shadeMat) > 0.01f;
            soil.enabled = CurrentAlpha(soilMat) > 0.01f;
        }

        private void ZoneProfileValues(out float t, out float h, out float l)
        {
            t = pod.Profile.TemperatureOffsetCelsius; h = pod.Profile.HumidityOffset; l = pod.Profile.LightMultiplier;
        }

        // --- presentation ----------------------------------------------------------------------

        // Smaller sign, an accent floor plate and a stress LED that shows the pod's live average stress (green / amber / red).
        private void MakePresentation()
        {
            Transform sign = transform.Find("Sign");
            if (sign != null) sign.localScale *= 0.68f;

            GameObject plate = GameObject.CreatePrimitive(PrimitiveType.Cube);
            plate.name = "AccentFloorPlate";
            Object.Destroy(plate.GetComponent<Collider>());
            plate.transform.SetParent(transform.Find("RuntimeCues"), false);
            plate.transform.localPosition = new Vector3(0f, 0.012f, 0f);
            plate.transform.localScale = new Vector3(3.5f, 0.02f, 3.1f);
            Shader lit = Shader.Find("Universal Render Pipeline/Lit");
            var plateMat = new Material(lit != null ? lit : Shader.Find("Standard"));
            Color accent = pod.Accent;
            Color dark = new Color(accent.r * 0.35f, accent.g * 0.35f, accent.b * 0.35f, 1f);
            if (plateMat.HasProperty("_BaseColor")) plateMat.SetColor("_BaseColor", dark);
            if (plateMat.HasProperty("_Smoothness")) plateMat.SetFloat("_Smoothness", 0.25f);
            plate.GetComponent<Renderer>().sharedMaterial = plateMat;

            Transform led = transform.Find("Equipment/SensorLed");
            if (led != null)
            {
                led.localScale *= 3.5f;
                var r = led.GetComponent<Renderer>();
                if (r != null) { ledMat = new Material(r.sharedMaterial); r.sharedMaterial = ledMat; }
            }
        }

        private void UpdateLed(float stress)
        {
            if (ledMat == null) return;
            Color c = stress < 0.08f ? new Color(0.25f, 0.95f, 0.4f) : stress < 0.25f ? new Color(1f, 0.75f, 0.15f) : new Color(1f, 0.25f, 0.2f);
            if (ledMat.HasProperty("_BaseColor")) ledMat.SetColor("_BaseColor", c);
            if (ledMat.HasProperty("_Color")) ledMat.SetColor("_Color", c);
            if (ledMat.HasProperty("_EmissionColor")) { ledMat.EnableKeyword("_EMISSION"); ledMat.SetColor("_EmissionColor", c * 2.5f); }
        }

        private static void SetColorAlpha(Material m, Color c, float a)
        {
            c.a = a;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        }

        // --- helpers ---------------------------------------------------------------------------

        private static void SetRate(ParticleSystem ps, float rate)
        {
            var e = ps.emission;
            e.rateOverTime = rate;
            if (rate > 0.01f && !ps.isPlaying) ps.Play();
        }

        private static float CurrentAlpha(Material m) => m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor").a : m.color.a;

        private static void SetAlpha(Material m, float a)
        {
            Color c = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : m.color;
            c.a = a;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        }

        private static Material TransparentMaterial(Color color, Texture texture = null, bool particle = false)
        {
            Shader shader = particle ? Shader.Find("Universal Render Pipeline/Particles/Unlit") : null;
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            var m = new Material(shader);
            if (m.HasProperty("_Surface"))
            {
                m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 0f);
                m.SetFloat("_SrcBlend", 5f); m.SetFloat("_DstBlend", 10f); m.SetFloat("_ZWrite", 0f);
                m.SetOverrideTag("RenderType", "Transparent");
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.renderQueue = 3000;
            }
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            if (m.HasProperty("_Color")) m.SetColor("_Color", color);
            if (texture != null && m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", texture);
            return m;
        }

        private static Texture2D Soft()
        {
            if (softTexture != null) return softTexture;
            const int n = 32;
            softTexture = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = Vector2.Distance(new Vector2(x, y), new Vector2((n - 1) * 0.5f, (n - 1) * 0.5f)) / (n * 0.5f);
                    softTexture.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(1f - d) * Mathf.Clamp01(1f - d)));
                }
            softTexture.Apply();
            return softTexture;
        }

        private static Renderer MakeSlab(Transform parent, string name, Vector3 localPosition, Vector3 scale, Color color, out Material material)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localScale = scale;
            var r = go.GetComponent<Renderer>();
            material = TransparentMaterial(color);
            r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.enabled = false;
            return r;
        }

        private static ParticleSystem MakeParticles(Transform parent, string name, Vector3 localPosition, Vector3 boxSize, Color color, float sizeMin, float sizeMax, Vector3 velocity, float lifetime)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true; main.playOnAwake = false;
            main.startLifetime = lifetime; main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
            main.startColor = color;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.maxParticles = 150;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box; shape.scale = boxSize;
            var emission = ps.emission; emission.rateOverTime = 0f;
            var vel = ps.velocityOverLifetime;
            vel.enabled = true; vel.space = ParticleSystemSimulationSpace.Local;
            vel.x = velocity.x; vel.y = velocity.y; vel.z = velocity.z;
            var fade = ps.colorOverLifetime;
            fade.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.25f), new GradientAlphaKey(0f, 1f) });
            fade.color = g;
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = TransparentMaterial(Color.white, Soft(), true);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return ps;
        }
    }
}

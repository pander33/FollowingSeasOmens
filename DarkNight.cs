using System.Reflection;
using Enviro;
using UnityEngine;
using UnityEngine.Rendering;

namespace FollowingSeasOmens
{
    /// <summary>
    /// A pitch-black night: no moon, no stars, a sky and a sea as black as ink. Only a lantern
    /// shows anything.
    ///
    /// The skybox's moon, its glow, the stars, the galaxy and the sky tint are written again in
    /// <c>LateUpdate</c> from Enviro's own curves, after Enviro's <c>Update</c>. Everything else that
    /// lights the night — the moonlight, the ambient light, the fog colour — is taken down through
    /// stand-in gradients, which Enviro itself evaluates wherever and whenever it draws. The sky's
    /// ambient probe is rendered again from the dark sky, and the reflections are turned down. Lanterns are lights of their own and are
    /// not touched.
    /// </summary>
    internal sealed class DarkNight : Omen
    {
        private static readonly int MoonColor = Shader.PropertyToID("_MoonColor");
        private static readonly int MoonGlowIntensity = Shader.PropertyToID("_MoonGlowIntensity");
        private static readonly int StarIntensity = Shader.PropertyToID("_StarIntensity");
        private static readonly int GalaxyIntensity = Shader.PropertyToID("_GalaxyIntensity");
        private static readonly int SkyTint = Shader.PropertyToID("_SkyColorTint");

        /// <summary>What is left of the night's light at full strength.</summary>
        private const float Left = 0.03f;
        private static readonly Color Dim = new Color(Left, Left, Left);

        private EnviroSkyModule _sky;
        private GradientTint _moonlight, _ambientSky, _ambientEquator, _ambientGround, _fog, _unityFog;
        private EnviroLightingModule _lighting;
        private float _probeKeep = 1f, _probeStamp, _probeNext, _reflectionKeep = 1f;
        private bool _probeOurs;

        /// <summary>When Enviro will next render the ambient probe; it changes each time Enviro does.</summary>
        private static readonly FieldInfo LastAmbientUpdate =
            typeof(EnviroLightingModule).GetField("lastAmbientSkyboxUpdate", BindingFlags.NonPublic | BindingFlags.Instance);

        public override string Name => "Dark night";
        public override string Announcement => "Not a star in the sky tonight";
        public override string JournalText => "A night so dark we could not see the bow without a lantern";

        protected override bool Begin(EnviroManager enviro)
        {
            if (enviro.Sky == null || enviro.Sky.Settings == null || enviro.Sky.mySkyboxMat == null) return false;
            _sky = enviro.Sky;

            if (enviro.Lighting != null && enviro.Lighting.Settings != null)
            {
                _lighting = enviro.Lighting;
                var s = enviro.Lighting.Settings;
                _moonlight = new GradientTint(() => s.moonColorGradient, g => s.moonColorGradient = g);
                _ambientSky = new GradientTint(() => s.ambientSkyColorGradient, g => s.ambientSkyColorGradient = g);
                _ambientEquator = new GradientTint(() => s.ambientEquatorColorGradient, g => s.ambientEquatorColorGradient = g);
                _ambientGround = new GradientTint(() => s.ambientGroundColorGradient, g => s.ambientGroundColorGradient = g);
            }
            if (enviro.Fog != null && enviro.Fog.Settings != null)
            {
                var f = enviro.Fog.Settings;
                _fog = new GradientTint(() => f.ambientColorGradient, g => f.ambientColorGradient = g);
                _unityFog = new GradientTint(() => f.unityFogColor, g => f.unityFogColor = g);
            }
            foreach (var tint in Tints()) tint?.Begin();
            return true;
        }

        protected override void Draw(EnviroManager enviro, float strength)
        {
            EnviroSky settings = _sky.Settings;
            Material mat = _sky.mySkyboxMat;
            float solar = enviro.solarTime;
            float keep = Mathf.Lerp(1f, Left, strength);
            if (mat != null)
            {
                mat.SetColor(MoonColor, settings.moonColorGradient.Evaluate(solar) * keep);
                mat.SetFloat(MoonGlowIntensity, settings.moonGlowIntensityCurve.Evaluate(solar) * keep);
                mat.SetFloat(StarIntensity, settings.starIntensityCurve.Evaluate(solar) * keep);
                mat.SetFloat(GalaxyIntensity, settings.galaxyIntensityCurve.Evaluate(solar) * keep);
            }
            Shader.SetGlobalColor(SkyTint, settings.skyColorTint * keep);

            _moonlight?.Update(enviro.lunarTime, Dim, strength);
            _ambientSky?.Update(solar, Dim, strength);
            _ambientEquator?.Update(solar, Dim, strength);
            _ambientGround?.Update(solar, Dim, strength);
            _fog?.Update(solar, Dim, strength);
            _unityFog?.Update(solar, Dim, strength);

            // In ambient mode Skybox the ambient light is a probe rendered from the sky now and then by
            // DynamicGI.UpdateEnvironment (Enviro calls it every tenth of a game hour). It is rendered again
            // here, from the darkened sky and at a lower intensity, each time Enviro has rendered its own
            // and whenever the strength has moved on.
            if (_lighting != null && RenderSettings.ambientMode == AmbientMode.Skybox)
            {
                RenderSettings.ambientIntensity = EnviroAmbient(enviro) * keep;
                float stamp = LastAmbientUpdate != null ? (float)LastAmbientUpdate.GetValue(_lighting) : 0f;
                if (Mathf.Abs(keep - _probeKeep) > 0.02f || stamp != _probeStamp ||
                    LastAmbientUpdate == null && Time.time > _probeNext)
                {
                    DynamicGI.UpdateEnvironment();
                    _probeKeep = keep;
                    _probeStamp = stamp;
                    _probeNext = Time.time + 1f;
                    _probeOurs = true;
                }
            }

            EnviroReflectionsModule reflections = enviro.Reflections;
            if (reflections != null && reflections.Settings != null)
            {
                float intensity = reflections.Settings.globalReflectionsIntensity;
                RenderSettings.reflectionIntensity = intensity * keep;
                ReflectionProbe global = GlobalProbe(enviro);
                if (global != null) global.intensity = intensity * keep;
                // The probe holds the sky it last saw; Enviro renders it again only as the game clock moves.
                if (Mathf.Abs(keep - _reflectionKeep) > 0.1f)
                {
                    reflections.RenderGlobalReflectionProbe(true, false);
                    _reflectionKeep = keep;
                }
            }
        }

        protected override void End()
        {
            EnviroManager enviro = EnviroManager.instance;
            if (_probeOurs && enviro != null && _lighting != null)
            {
                RenderSettings.ambientIntensity = EnviroAmbient(enviro);
                DynamicGI.UpdateEnvironment();
            }
            _probeOurs = false;
            _probeKeep = 1f;
            if (enviro != null && enviro.Reflections != null && enviro.Reflections.Settings != null)
            {
                float intensity = enviro.Reflections.Settings.globalReflectionsIntensity;
                RenderSettings.reflectionIntensity = intensity;
                ReflectionProbe global = GlobalProbe(enviro);
                if (global != null) global.intensity = intensity;
                if (_reflectionKeep < 1f) enviro.Reflections.RenderGlobalReflectionProbe(true, false);
            }
            _reflectionKeep = 1f;
            foreach (var tint in Tints()) tint?.End();
            _sky = null;
            _lighting = null;
            _moonlight = _ambientSky = _ambientEquator = _ambientGround = _fog = _unityFog = null;
        }

        /// <summary>The ambient intensity Enviro itself sets for this moment.</summary>
        private float EnviroAmbient(EnviroManager enviro) =>
            _lighting.Settings.ambientIntensityCurve.Evaluate(enviro.solarTime) * _lighting.Settings.ambientIntensityModifier;

        private static ReflectionProbe GlobalProbe(EnviroManager enviro) =>
            enviro.Objects != null && enviro.Objects.globalReflectionProbe != null
                ? enviro.Objects.globalReflectionProbe.myProbe
                : null;

        private GradientTint[] Tints() => new[] { _moonlight, _ambientSky, _ambientEquator, _ambientGround, _fog, _unityFog };
    }
}

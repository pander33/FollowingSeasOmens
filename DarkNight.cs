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
        private static readonly int SkyIntensity = Shader.PropertyToID("_Intensity");
        private static readonly int MieIntensity = Shader.PropertyToID("_MieScatteringIntensity");
        private static readonly int SunColor = Shader.PropertyToID("_SunColor");
        private static readonly int MoonGlowColor = Shader.PropertyToID("_MoonGlowColor");
        private static readonly int CirrusColor = Shader.PropertyToID("_CirrusCloudColor");
        private static readonly int FlatLightColor = Shader.PropertyToID("_FlatCloudsLightColor");
        private static readonly int FlatAmbientColor = Shader.PropertyToID("_FlatCloudsAmbientColor");
        private static readonly int[] SkyColors =
        {
            Shader.PropertyToID("_FrontColor0"), Shader.PropertyToID("_FrontColor1"),
            Shader.PropertyToID("_FrontColor2"), Shader.PropertyToID("_FrontColor3"),
            Shader.PropertyToID("_FrontColor4"), Shader.PropertyToID("_FrontColor5"),
            Shader.PropertyToID("_BackColor0"), Shader.PropertyToID("_BackColor1"),
            Shader.PropertyToID("_BackColor2"), Shader.PropertyToID("_BackColor3"),
            Shader.PropertyToID("_BackColor4"), Shader.PropertyToID("_BackColor5")
        };

        /// <summary>What is left of the night's light at full strength.</summary>
        private const float Left = 0f;
        private static readonly Color Dim = new Color(Left, Left, Left);

        private EnviroSkyModule _sky;
        private Gradient[] _skyColors;
        private GradientTint _sunlight, _moonlight, _ambientSky, _ambientEquator, _ambientGround, _fog, _unityFog;
        private GradientTint _cloudSunlight, _cloudMoonlight, _cloudAmbient;
        private EnviroLightingModule _lighting;
        private float _probeKeep = 1f, _probeStamp, _probeNext, _reflectionKeep = 1f;
        private bool _probeOurs;
        private PlayerHandler _player;
        private Light _playerLight;
        private float _playerLightIntensity;

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
            var sky = _sky.Settings;
            _skyColors = new[]
            {
                sky.frontColorGradient0, sky.frontColorGradient1, sky.frontColorGradient2,
                sky.frontColorGradient3, sky.frontColorGradient4, sky.frontColorGradient5,
                sky.backColorGradient0, sky.backColorGradient1, sky.backColorGradient2,
                sky.backColorGradient3, sky.backColorGradient4, sky.backColorGradient5
            };

            if (enviro.Lighting != null && enviro.Lighting.Settings != null)
            {
                _lighting = enviro.Lighting;
                var s = enviro.Lighting.Settings;
                _sunlight = new GradientTint(() => s.sunColorGradient, g => s.sunColorGradient = g);
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
            if (enviro.VolumetricClouds != null && enviro.VolumetricClouds.settingsGlobal != null)
            {
                var clouds = enviro.VolumetricClouds.settingsGlobal;
                _cloudSunlight = new GradientTint(() => clouds.sunLightColorGradient, g => clouds.sunLightColorGradient = g);
                _cloudMoonlight = new GradientTint(() => clouds.moonLightColorGradient, g => clouds.moonLightColorGradient = g);
                _cloudAmbient = new GradientTint(() => clouds.ambientColorGradient, g => clouds.ambientColorGradient = g);
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
            var player = DataPersistenceManager.dpInstance != null ? DataPersistenceManager.dpInstance.player : null;
            if (player != _player)
            {
                if (_playerLight != null) _playerLight.intensity = _playerLightIntensity;
                _player = player;
                _playerLight = null;
                if (_player != null)
                {
                    var lightTransform = _player.transform.Find("Light");
                    if (lightTransform != null) _playerLight = lightTransform.GetComponent<Light>();
                    if (_playerLight != null) _playerLightIntensity = _playerLight.intensity;
                }
            }
            if (_playerLight != null) _playerLight.intensity = _playerLightIntensity * keep;
            if (mat != null)
            {
                mat.SetColor(MoonColor, settings.moonColorGradient.Evaluate(solar) * keep);
                mat.SetColor(MoonGlowColor, settings.moonGlowColorGradient.Evaluate(solar) * keep);
                mat.SetFloat(MoonGlowIntensity, settings.moonGlowIntensityCurve.Evaluate(solar) * keep);
                mat.SetFloat(StarIntensity, settings.starIntensityCurve.Evaluate(solar) * keep);
                mat.SetFloat(GalaxyIntensity, settings.galaxyIntensityCurve.Evaluate(solar) * keep);
            }
            Shader.SetGlobalColor(SkyTint, settings.skyColorTint * keep);
            Shader.SetGlobalFloat(SkyIntensity, settings.intensity * settings.intensityCurve.Evaluate(solar) * keep);
            Shader.SetGlobalFloat(MieIntensity, settings.mieScatteringIntensityCurve.Evaluate(solar) * settings.mieScatteringMultiplier * keep);
            Shader.SetGlobalColor(SunColor, settings.sunDiscColorGradient.Evaluate(solar) * keep);
            for (var i = 0; i < SkyColors.Length; i++)
                if (_skyColors[i] != null)
                    Shader.SetGlobalColor(SkyColors[i], _skyColors[i].Evaluate(solar) * keep);
            if (enviro.FlatClouds != null && enviro.FlatClouds.settings != null)
            {
                var clouds = enviro.FlatClouds.settings;
                Shader.SetGlobalColor(CirrusColor, clouds.cirrusCloudsColor.Evaluate(solar) * keep);
                Shader.SetGlobalColor(FlatLightColor, clouds.flatCloudsLightColor.Evaluate(solar) * keep);
                Shader.SetGlobalColor(FlatAmbientColor, clouds.flatCloudsAmbientColor.Evaluate(solar) * keep);
            }

            _sunlight?.Update(solar, Dim, strength);
            _moonlight?.Update(enviro.lunarTime, Dim, strength);
            _ambientSky?.Update(solar, Dim, strength);
            _ambientEquator?.Update(solar, Dim, strength);
            _ambientGround?.Update(solar, Dim, strength);
            _fog?.Update(solar, Dim, strength);
            _unityFog?.Update(solar, Dim, strength);
            _cloudSunlight?.Update(solar, Dim, strength);
            _cloudMoonlight?.Update(enviro.lunarTime, Dim, strength);
            _cloudAmbient?.Update(solar, Dim, strength);
            if (_lighting != null && _lighting.Settings.setDirectLighting)
                _lighting.UpdateDirectLighting();

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

            // The reflection refresh resets ambient intensity, so apply attenuation after it.
            if (_lighting != null && _lighting.Settings.setAmbientLighting)
            {
                var lighting = _lighting.Settings;
                var ambient = EnviroAmbient(enviro);
                if (lighting.ambientMode == AmbientMode.Skybox)
                    RenderSettings.ambientIntensity = ambient * keep;
                else
                {
                    RenderSettings.ambientSkyColor = lighting.ambientSkyColorGradient.Evaluate(solar) * ambient;
                    RenderSettings.ambientEquatorColor = lighting.ambientEquatorColorGradient.Evaluate(solar) * ambient;
                    RenderSettings.ambientGroundColor = lighting.ambientGroundColorGradient.Evaluate(solar) * ambient;
                }
            }
        }

        protected override void End()
        {
            if (_playerLight != null) _playerLight.intensity = _playerLightIntensity;
            _playerLight = null;
            _player = null;
            EnviroManager enviro = EnviroManager.instance;
            foreach (var tint in Tints()) tint?.End();
            if (enviro != null && _sky != null && _sky.mySkyboxMat != null)
            {
                _sky.UpdateSkybox(_sky.mySkyboxMat);
                for (var i = 0; i < SkyColors.Length; i++)
                    if (_skyColors[i] != null)
                        Shader.SetGlobalColor(SkyColors[i], _skyColors[i].Evaluate(enviro.solarTime));
            }
            if (enviro != null && enviro.FlatClouds != null && enviro.FlatClouds.settings != null)
            {
                var clouds = enviro.FlatClouds.settings;
                Shader.SetGlobalColor(CirrusColor, clouds.cirrusCloudsColor.Evaluate(enviro.solarTime));
                Shader.SetGlobalColor(FlatLightColor, clouds.flatCloudsLightColor.Evaluate(enviro.solarTime));
                Shader.SetGlobalColor(FlatAmbientColor, clouds.flatCloudsAmbientColor.Evaluate(enviro.solarTime));
            }
            if (enviro != null && _lighting != null)
            {
                if (_lighting.Settings.setDirectLighting) _lighting.UpdateDirectLighting();
                if (_lighting.Settings.setAmbientLighting) _lighting.UpdateAmbientLighting(true);
            }
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
            _sky = null;
            _skyColors = null;
            _lighting = null;
            _sunlight = _moonlight = _ambientSky = _ambientEquator = _ambientGround = _fog = _unityFog = null;
            _cloudSunlight = _cloudMoonlight = _cloudAmbient = null;
        }

        /// <summary>The ambient intensity Enviro itself sets for this moment.</summary>
        private float EnviroAmbient(EnviroManager enviro) =>
            _lighting.Settings.ambientIntensityCurve.Evaluate(enviro.solarTime) * _lighting.Settings.ambientIntensityModifier;

        private static ReflectionProbe GlobalProbe(EnviroManager enviro) =>
            enviro.Objects != null && enviro.Objects.globalReflectionProbe != null
                ? enviro.Objects.globalReflectionProbe.myProbe
                : null;

        private GradientTint[] Tints() => new[] { _sunlight, _moonlight, _ambientSky, _ambientEquator, _ambientGround, _fog, _unityFog,
            _cloudSunlight, _cloudMoonlight, _cloudAmbient };
    }
}

using Enviro;
using UnityEngine;

namespace FollowingSeasOmens
{
    /// <summary>
    /// A full, large, dark red moon, the sky and the night light gone crimson.
    ///
    /// Enviro writes the skybox's moon colour, glow, phase and size in its <c>Update</c>; they are
    /// written again here in <c>LateUpdate</c>, so no setting of Enviro's is changed for them. The sky
    /// tint is the same (the weather module eases the setting itself every frame). The moon is switched
    /// to Enviro's simple mode, which puts it opposite the sun — up all night, as a full moon is — and
    /// stops Enviro from working out the phase. The moonlight and the ambient light are tinted through
    /// stand-in gradients.
    /// </summary>
    internal sealed class BloodMoon : Omen
    {
        private static readonly int MoonColor = Shader.PropertyToID("_MoonColor");
        private static readonly int MoonGlowColor = Shader.PropertyToID("_MoonGlowColor");
        private static readonly int MoonParameters = Shader.PropertyToID("_SkyMoonParameters");
        private static readonly int SkyTint = Shader.PropertyToID("_SkyColorTint");

        private static readonly Color MoonRed = new Color(2.2f, 0.12f, 0.04f);
        private static readonly Color GlowRed = new Color(1.5f, 0.06f, 0.02f);
        private static readonly Color SkyTintRed = new Color(1f, 0.45f, 0.42f);
        private static readonly Color MoonlightTint = new Color(1.6f, 0.3f, 0.25f);
        private static readonly Color AmbientTint = new Color(1.1f, 0.55f, 0.5f);
        private const float MoonSize = 1.5f;

        private EnviroSkyModule _sky;
        private EnviroLightingModule _lighting;
        private EnviroSky.MoonMode _moonMode;
        private GradientTint _moonlight, _ambientSky, _ambientEquator, _ambientGround;

        public override string Name => "Blood moon";
        public override string Announcement => "The moon is rising blood red";
        public override string JournalText => "A blood moon rose over the sea";

        protected override bool Begin(EnviroManager enviro)
        {
            if (enviro.Sky == null || enviro.Sky.Settings == null || enviro.Sky.mySkyboxMat == null) return false;
            _sky = enviro.Sky;
            _moonMode = _sky.Settings.moonMode;
            _sky.Settings.moonMode = EnviroSky.MoonMode.Simple;

            _lighting = enviro.Lighting;
            if (_lighting != null && _lighting.Settings != null)
            {
                var s = _lighting.Settings;
                _moonlight = new GradientTint(() => s.moonColorGradient, g => s.moonColorGradient = g);
                _ambientSky = new GradientTint(() => s.ambientSkyColorGradient, g => s.ambientSkyColorGradient = g);
                _ambientEquator = new GradientTint(() => s.ambientEquatorColorGradient, g => s.ambientEquatorColorGradient = g);
                _ambientGround = new GradientTint(() => s.ambientGroundColorGradient, g => s.ambientGroundColorGradient = g);
                _moonlight.Begin();
                _ambientSky.Begin();
                _ambientEquator.Begin();
                _ambientGround.Begin();
            }
            return true;
        }

        protected override void Draw(EnviroManager enviro, float strength)
        {
            EnviroSky settings = _sky.Settings;
            Material mat = _sky.mySkyboxMat;
            float solar = enviro.solarTime;
            if (mat != null)
            {
                mat.SetColor(MoonColor, Color.Lerp(settings.moonColorGradient.Evaluate(solar), MoonRed, strength));
                mat.SetColor(MoonGlowColor, Color.Lerp(settings.moonGlowColorGradient.Evaluate(solar), GlowRed, strength));
                float size = settings.moonScale * Mathf.Lerp(1f, MoonSize, strength);
                mat.SetVector(MoonParameters, new Vector4(Mathf.Lerp(settings.moonPhase, 0f, strength), size, size,
                    settings.moonMode == EnviroSky.MoonMode.Off ? 0f : 1f));
            }
            Shader.SetGlobalColor(SkyTint, Color.Lerp(settings.skyColorTint, settings.skyColorTint * SkyTintRed, strength));

            if (_moonlight != null)
            {
                _moonlight.Update(enviro.lunarTime, MoonlightTint, strength);
                _ambientSky.Update(solar, AmbientTint, strength);
                _ambientEquator.Update(solar, AmbientTint, strength);
                _ambientGround.Update(solar, AmbientTint, strength);
            }
        }

        protected override void End()
        {
            if (_sky != null && _sky.Settings != null) _sky.Settings.moonMode = _moonMode;
            if (_moonlight != null)
            {
                _moonlight.End();
                _ambientSky.End();
                _ambientEquator.End();
                _ambientGround.End();
            }
            _sky = null;
            _lighting = null;
            _moonlight = _ambientSky = _ambientEquator = _ambientGround = null;
        }
    }
}

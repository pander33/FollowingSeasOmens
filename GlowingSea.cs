using Crest;
using Enviro;
using UnityEngine;

namespace FollowingSeasOmens
{
    /// <summary>
    /// Glowing plankton: the foam — the bow wave, the wake, the breaking crests — shines blue-green,
    /// and the rest of the water glows a fainter blue.
    ///
    /// Crest's ocean material is given colours far above 1 (HDR), so they are bright even in the dark
    /// and the game's bloom makes them glow: the foam colour for the foam, the foam bubble colour and
    /// the scattering colour for the water around it. The colours are taken at the start and put back
    /// at the end; nothing else of the material is touched.
    /// </summary>
    internal sealed class GlowingSea : Omen
    {
        private static readonly int[] Properties =
        {
            Shader.PropertyToID("_FoamWhiteColor"),
            Shader.PropertyToID("_FoamBubbleColor"),
            Shader.PropertyToID("_Diffuse"),
            Shader.PropertyToID("_DiffuseGrazing")
        };

        // The same order as Properties. The foam keeps its own alpha (how much foam shows).
        private static readonly Color[] Glow =
        {
            new Color(0.8f, 7.2f, 11.2f),
            new Color(0f, 2.5f, 4f, 1f),
            new Color(0.03f, 0.6f, 0.85f, 1f),
            new Color(0.03f, 0.6f, 0.85f, 1f)
        };

        private Material _mat;
        private readonly Color[] _original = new Color[Properties.Length];

        public override string Name => "Glowing sea";
        public override string Announcement => "The sea is glowing in our wake";
        public override string JournalText => "The sea glowed blue in our wake all night";

        protected override bool Begin(EnviroManager enviro)
        {
            Material mat = OceanRenderer.Instance != null ? OceanRenderer.Instance.OceanMaterial : null;
            if (mat == null) return false;
            foreach (int id in Properties)
                if (!mat.HasProperty(id)) return false;
            _mat = mat;
            for (int i = 0; i < Properties.Length; i++) _original[i] = mat.GetColor(Properties[i]);
            return true;
        }

        protected override void Draw(EnviroManager enviro, float strength)
        {
            Material now = OceanRenderer.Instance != null ? OceanRenderer.Instance.OceanMaterial : null;
            if (now != _mat)
            {
                // The ocean was made anew (a world loaded again): give the old material its colours
                // back and start over with the new one.
                End();
                if (now == null || !Begin(enviro)) return;
            }
            for (int i = 0; i < Properties.Length; i++)
            {
                Color c = Color.Lerp(_original[i], Glow[i], strength);
                if (i == 0) c.a = _original[i].a;
                _mat.SetColor(Properties[i], c);
            }
        }

        protected override void End()
        {
            if (_mat != null)
                for (int i = 0; i < Properties.Length; i++) _mat.SetColor(Properties[i], _original[i]);
            _mat = null;
        }
    }
}

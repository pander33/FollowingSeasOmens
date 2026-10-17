using Crest;
using Enviro;
using UnityEngine;

namespace FollowingSeasOmens
{
    /// <summary>
    /// Northern lights after a solar storm: a few curtains of rays a kilometre or two up, green at the
    /// foot and purple (or, in a great storm, red) at the top, swaying and flickering.
    ///
    /// Enviro has an aurora module, but the game ships without it and its look cannot be steered, so
    /// this is a mesh of its own: vertical ribbons, a ray texture made at start, vertex colours above 1
    /// so the bloom takes them, and <c>Sprites/Default</c>, which the game build has and which draws
    /// transparent and after Enviro's fog. The mesh follows the camera across the water, so it is
    /// always the same distance off and the floating origin does not matter.
    /// </summary>
    internal sealed class Aurora : Omen
    {
        private const int Curtains = 3;
        private const int Columns = 160;
        private const int Rows = 8;
        private const float HalfWidth = 4000f;

        private static readonly Color Foot = new Color(0.3f, 2.6f, 0.9f);
        private static readonly Color Middle = new Color(0.15f, 1.6f, 1.0f);
        private static readonly Color TopPurple = new Color(0.9f, 0.25f, 1.3f);
        private static readonly Color TopRed = new Color(1.8f, 0.2f, 0.25f);

        private readonly Transform _parent;
        private GameObject _go;
        private MeshRenderer _renderer;
        private Mesh _mesh;
        private Material _mat;
        private Texture2D _tex;

        private Vector3[] _base;
        private Vector3[] _verts;
        private Color[] _baseColors;
        private Color[] _colors;
        private float[] _columnX;
        private float _seed;
        private float _scroll;

        public Aurora(Transform parent)
        {
            _parent = parent;
        }

        public override string Name => "Northern lights";
        public override string Announcement => "Northern lights are burning in the sky";
        public override string JournalText => "Northern lights burned in the sky all night: a storm on the sun";

        protected override bool Begin(EnviroManager enviro)
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) return false;

            // Everything random in the look comes from the omen's seed, the co-op host's on a guest.
            // Unity's random sequence is shared with the game and is put back whatever happens; what
            // was made before a failure is destroyed.
            Random.State outer = Random.state;
            bool made = false;
            try
            {
                Random.InitState(Seed);
                _seed = Random.Range(0f, 1000f);
                bool great = Random.value < 0.3f;
                _tex = MakeRays(_seed);
                _mat = new Material(shader) { mainTexture = _tex, renderQueue = 3000, hideFlags = HideFlags.HideAndDontSave };
                _mesh = MakeMesh(great ? TopRed : TopPurple);
                _mesh.hideFlags = HideFlags.HideAndDontSave;
                // In the northern sky: the curtains stand along the mesh's +z, and the game's north is the
                // world's +z (a compass reads 0° there). One draw, as before, so the rest of the look stays.
                float yaw = Random.Range(-35f, 35f);

                _go = new GameObject("Omens Aurora") { hideFlags = HideFlags.HideAndDontSave };
                _go.transform.SetParent(_parent, false);
                _go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                _go.AddComponent<MeshFilter>().sharedMesh = _mesh;
                _renderer = _go.AddComponent<MeshRenderer>();
                _renderer.sharedMaterial = _mat;
                _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _renderer.receiveShadows = false;
                _renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                _renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
                made = true;
                return true;
            }
            finally
            {
                Random.state = outer;
                if (!made) End();
            }
        }

        protected override void Draw(EnviroManager enviro, float strength)
        {
            // Not drawn, and not worked out, while it cannot be seen (by day, at the start of dusk).
            bool visible = strength > 0.001f;
            if (_renderer.enabled != visible) _renderer.enabled = visible;
            if (!visible) return;

            Camera cam = Camera.main;
            if (cam == null) return;
            float sea = OceanRenderer.Instance != null ? OceanRenderer.Instance.SeaLevel : 0f;
            Vector3 p = cam.transform.position;
            _go.transform.position = new Vector3(p.x, sea, p.z);

            float t = Time.time;
            _scroll += Time.deltaTime * 0.004f;
            _mat.mainTextureOffset = new Vector2(_scroll, 0f);
            _mat.color = new Color(1f, 1f, 1f, strength);

            // Sway each column of a curtain sideways and let its brightness breathe.
            int perColumn = Rows + 1;
            for (int c = 0; c < Curtains; c++)
            {
                for (int i = 0; i <= Columns; i++)
                {
                    float x = _columnX[i];
                    float sway = 160f * Mathf.Sin(t * 0.05f + x * 0.0009f + c * 2.1f)
                                 + 60f * Mathf.Sin(t * 0.13f + x * 0.004f + c);
                    float breathe = 0.45f + 0.55f * Mathf.PerlinNoise(x * 0.0015f + c * 7.7f + _seed, t * 0.06f);
                    int first = (c * (Columns + 1) + i) * perColumn;
                    for (int r = 0; r < perColumn; r++)
                    {
                        int k = first + r;
                        Vector3 b = _base[k];
                        _verts[k] = new Vector3(b.x, b.y, b.z + sway * (0.6f + 0.4f * r / Rows));
                        Color col = _baseColors[k];
                        col.a *= breathe;
                        _colors[k] = col;
                    }
                }
            }
            _mesh.vertices = _verts;
            _mesh.colors = _colors;
        }

        protected override void End()
        {
            if (_go != null) Object.Destroy(_go);
            if (_mesh != null) Object.Destroy(_mesh);
            if (_mat != null) Object.Destroy(_mat);
            if (_tex != null) Object.Destroy(_tex);
            _go = null;
            _renderer = null;
            _mesh = null;
            _mat = null;
            _tex = null;
        }

        /// <summary>Thin bright rays across, the same all the way up: the curtain's folds.</summary>
        private static Texture2D MakeRays(float seed)
        {
            const int w = 512, h = 4;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, true)
            {
                wrapMode = TextureWrapMode.Repeat,
                hideFlags = HideFlags.HideAndDontSave
            };
            var px = new Color[w * h];
            for (int x = 0; x < w; x++)
            {
                // Sampled round a circle, so the texture repeats without a seam.
                float a = x / (float)w * Mathf.PI * 2f;
                float n = 0.55f * Mathf.PerlinNoise(Mathf.Cos(a) * 8f + seed, Mathf.Sin(a) * 8f + seed)
                          + 0.45f * Mathf.PerlinNoise(Mathf.Cos(a) * 30f + seed + 40f, Mathf.Sin(a) * 30f + seed + 40f);
                float v = Mathf.Clamp01(0.25f + (n - 0.35f) * 2.2f);
                for (int y = 0; y < h; y++) px[y * w + x] = new Color(v, v, v, v);
            }
            tex.SetPixels(px);
            tex.Apply(true);
            return tex;
        }

        private Mesh MakeMesh(Color top)
        {
            int perColumn = Rows + 1;
            int count = Curtains * (Columns + 1) * perColumn;
            _base = new Vector3[count];
            _verts = new Vector3[count];
            _baseColors = new Color[count];
            _colors = new Color[count];
            _columnX = new float[Columns + 1];
            var uv = new Vector2[count];
            var tris = new int[Curtains * Columns * Rows * 6];

            int ti = 0;
            for (int c = 0; c < Curtains; c++)
            {
                float distance = 2200f + c * 700f + Random.Range(-200f, 200f);
                float bend = 500f + c * 150f;
                float phase = Random.Range(0f, Mathf.PI * 2f);
                float foot = 700f + c * 80f;
                float height = 900f + c * 200f;
                for (int i = 0; i <= Columns; i++)
                {
                    float u = i / (float)Columns;
                    float x = Mathf.Lerp(-HalfWidth, HalfWidth, u);
                    _columnX[i] = x;
                    float z = distance + bend * Mathf.Sin(u * Mathf.PI * 2.6f + phase) + 120f * Mathf.Sin(u * 40f + phase * 3f);
                    float ends = Mathf.SmoothStep(0f, 1f, Mathf.Min(u, 1f - u) * 6f);
                    for (int r = 0; r < perColumn; r++)
                    {
                        float v = r / (float)Rows;
                        int k = (c * (Columns + 1) + i) * perColumn + r;
                        _base[k] = new Vector3(x, foot + height * v, z);
                        Color col = v < 0.35f ? Color.Lerp(Foot, Middle, v / 0.35f) : Color.Lerp(Middle, top, (v - 0.35f) / 0.65f);
                        // Sharp at the foot, fading out towards the top, and towards both ends.
                        col.a = Mathf.Clamp01(v * 12f) * Mathf.Pow(1f - v, 1.6f) * ends;
                        _baseColors[k] = col;
                        uv[k] = new Vector2(u * 3f + c * 0.37f, v);
                    }
                }
                for (int i = 0; i < Columns; i++)
                    for (int r = 0; r < Rows; r++)
                    {
                        int a = (c * (Columns + 1) + i) * perColumn + r;
                        int b = a + perColumn;
                        tris[ti++] = a; tris[ti++] = b; tris[ti++] = a + 1;
                        tris[ti++] = a + 1; tris[ti++] = b; tris[ti++] = b + 1;
                    }
            }
            System.Array.Copy(_base, _verts, count);
            System.Array.Copy(_baseColors, _colors, count);

            var mesh = new Mesh { name = "Omens Aurora" };
            mesh.vertices = _verts;
            mesh.colors = _colors;
            mesh.uv = uv;
            mesh.triangles = tris;
            // Far bigger than the curtains, so the mesh is never culled while it sways.
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 20000f);
            mesh.MarkDynamic();
            return mesh;
        }
    }
}

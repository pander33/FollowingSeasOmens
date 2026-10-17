using System;
using System.Collections.Generic;
using System.Globalization;
using Crest;
using UnityEngine;

namespace FollowingSeasOmens
{
	internal sealed class FishingPatches : IDisposable
	{
		private const float CellSize = 500f;
		private const float Radius = 40f;
		private const float RenderDistance = 900f;
		private const float KeepDistance = 2000f;
		private const int RenderCount = 8;
		private const int MaxZones = 512;
		private const int Segments = 48;

		/// <summary>Seconds between two ripples of one patch.</summary>
		private const float PulseSeconds = 3.5f;

		/// <summary>How high above the water a patch is drawn, so the waves do not cut it.</summary>
		private const float Lift = 0.15f;

		private static readonly RaycastHit[] Hits = new RaycastHit[16];
		private static Shader _shader;
		private static bool _shaderLooked;

		private readonly List<Zone> _zones = new List<Zone>();
		private readonly List<Zone> _nearest = new List<Zone>(RenderCount);
		private readonly Ring[] _rings = new Ring[RenderCount];
		private Material _material;
		private Mesh _glowMesh, _rippleMesh;
		private int _seed;
		private bool _authority;
		private bool _disposed;
		private float _nextValidation;
		private float _nextRender;
		private float _activeStrength;
		private Vector3 _renderOrigin;
		internal int Seed => _seed;

		/// <summary>Patches are only real where they can be seen: without the shader there are none.</summary>
		private static Shader PatchShader
		{
			get
			{
				if (!_shaderLooked)
				{
					_shader = Shader.Find("Sprites/Default");
					_shaderLooked = true;
				}

				return _shader;
			}
		}

		public void Update(Transform viewer, int seed, float strength, bool authority)
		{
			if (_disposed) return;
			if (viewer == null || strength <= 0f || PatchShader == null)
			{
				_activeStrength = 0f;
				HideRings();
				return;
			}

			if (_seed != seed || _authority != authority)
			{
				Reset();
				_seed = seed;
				_authority = authority;
			}

			Vector3 origin = OriginOffset();
			if (origin != _renderOrigin)
			{
				foreach (Ring ring in _rings) ring?.Shift(_renderOrigin - origin);
				_renderOrigin = origin;
			}

			Vector3 stableViewer = viewer.position + origin;
			_activeStrength = strength;
			if (authority && Time.unscaledTime >= _nextValidation)
			{
				_nextValidation = Time.unscaledTime + 1f;
				UpdateZones(stableViewer);
			}

			if (Time.unscaledTime >= _nextRender)
			{
				_nextRender = Time.unscaledTime + 1f;
				Render(stableViewer, origin);
			}

			foreach (Ring ring in _rings) ring?.Animate(Time.time, strength);
		}

		/// <summary>0..1 at a point in the scene's coordinates. The zones were checked for water when they
		/// were made (on the host), so a guest gets the same answer as the host.</summary>
		public float Influence(Vector3 localPosition)
		{
			if (_disposed || _activeStrength <= 0f || PatchShader == null) return 0f;
			Vector3 position = localPosition + OriginOffset();
			float influence = 0f;
			foreach (Zone zone in _zones)
			{
				float x = position.x - zone.X, z = position.z - zone.Z;
				float distance = Mathf.Sqrt(x * x + z * z);
				if (distance >= Radius) continue;
				float t = 1f - distance / Radius;
				influence = Mathf.Max(influence, t * t * (3f - 2f * t));
			}

			return influence;
		}

		public string Serialize()
		{
			var text = new System.Text.StringBuilder("1,");
			text.Append(_seed.ToString(CultureInfo.InvariantCulture));
			foreach (Zone zone in _zones)
			{
				text.Append(';').Append(zone.Id.ToString("X8", CultureInfo.InvariantCulture)).Append(',')
					.Append(zone.X.ToString("R", CultureInfo.InvariantCulture)).Append(',')
					.Append(zone.Z.ToString("R", CultureInfo.InvariantCulture));
			}

			return text.ToString();
		}

		public bool Receive(string payload)
		{
			if (_disposed || string.IsNullOrEmpty(payload) || payload.Length > 24000) return false;
			string[] records = payload.Split(';');
			if (records.Length > MaxZones + 1) return false;
			string[] header = records[0].Split(',');
			if (header.Length != 2 || header[0] != "1" ||
			    !int.TryParse(header[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int seed))
				return false;
			var incoming = new List<Zone>(records.Length - 1);
			var ids = new HashSet<uint>();
			for (int i = 1; i < records.Length; i++)
			{
				string[] fields = records[i].Split(',');
				if (fields.Length != 3 || !uint.TryParse(fields[0], NumberStyles.HexNumber,
					    CultureInfo.InvariantCulture, out uint id) ||
				    !float.TryParse(fields[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) ||
				    !float.TryParse(fields[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float z) ||
				    !Finite(x) || !Finite(z) || Mathf.Abs(x) > 100000000f || Mathf.Abs(z) > 100000000f ||
				    !ids.Add(id) || !IsGeneratedZone(seed, id, x, z)) return false;
				incoming.Add(new Zone(id, x, z));
			}

			if (_seed != seed) Reset();
			_seed = seed;
			_authority = false;
			_zones.Clear();
			_zones.AddRange(incoming);
			_nextRender = 0f;
			return true;
		}

		public void Reset()
		{
			_zones.Clear();
			_activeStrength = 0f;
			_nextValidation = 0f;
			_nextRender = 0f;
			HideRings();
		}

		public void Dispose()
		{
			if (_disposed) return;
			Reset();
			foreach (Ring ring in _rings) ring?.Destroy();
			if (_material != null) UnityEngine.Object.Destroy(_material);
			if (_glowMesh != null) UnityEngine.Object.Destroy(_glowMesh);
			if (_rippleMesh != null) UnityEngine.Object.Destroy(_rippleMesh);
			_disposed = true;
		}

		/// <summary>Host only: forgets far zones, drops near ones that turn out to be land now that the
		/// ground there is loaded, and makes the ones of the cells around.</summary>
		private void UpdateZones(Vector3 viewer)
		{
			for (int i = _zones.Count - 1; i >= 0; i--)
			{
				float distance = DistanceSquared(_zones[i], viewer);
				if (distance > KeepDistance * KeepDistance ||
				    distance <= RenderDistance * RenderDistance && !IsWater(_zones[i].X, _zones[i].Z))
					_zones.RemoveAt(i);
			}

			int centerX = Mathf.FloorToInt(viewer.x / CellSize);
			int centerZ = Mathf.FloorToInt(viewer.z / CellSize);
			for (int x = centerX - 2; x <= centerX + 2 && _zones.Count < MaxZones; x++)
			for (int z = centerZ - 2; z <= centerZ + 2 && _zones.Count < MaxZones; z++)
			{
				uint id = Hash(_seed, x, z);
				if (Chance(id) >= 0.2f || Contains(id)) continue;
				float px = (x + Unit(id ^ 0xA341316Cu)) * CellSize;
				float pz = (z + Unit(id ^ 0xC8013EA4u)) * CellSize;
				if (IsWater(px, pz)) _zones.Add(new Zone(id, px, pz));
			}
		}

		/// <summary>No ground at or above the sea level at the centre and around the edge. Crest is not asked:
		/// its height queries answer late, and one query cannot be moved over many points in a frame.</summary>
		private static bool IsWater(float x, float z)
		{
			OceanRenderer ocean = OceanRenderer.Instance;
			var dp = DataPersistenceManager.dpInstance;
			if (ocean == null || dp == null || dp.player == null) return false;
			int ground = dp.player.whatIsGround | dp.player.whatIsGroundObject;
			if (ground == 0) return false;
			float sea = ocean.SeaLevel;
			Vector3 centre = new Vector3(x, 0f, z) - OriginOffset();
			for (int i = 0; i < 9; i++)
			{
				float angle = i * Mathf.PI / 4f;
				Vector3 sample = centre +
				                 (i == 8 ? Vector3.zero : new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * Radius);
				sample.y = sea + 5000f;
				int count = Physics.RaycastNonAlloc(sample, Vector3.down, Hits, 5002f, ground,
					QueryTriggerInteraction.Ignore);
				for (int h = 0; h < count; h++)
					// Ships and loose cargo float: only what does not move is land.
					if (Hits[h].collider.attachedRigidbody == null && Hits[h].point.y >= sea - 2f)
						return false;
			}

			return true;
		}

		private void Render(Vector3 viewerStable, Vector3 origin)
		{
			_nearest.Clear();
			_zones.Sort((a, b) => DistanceSquared(a, viewerStable).CompareTo(DistanceSquared(b, viewerStable)));
			foreach (Zone zone in _zones)
			{
				if (_nearest.Count == RenderCount ||
				    DistanceSquared(zone, viewerStable) > RenderDistance * RenderDistance) break;
				_nearest.Add(zone);
			}

			// A patch keeps its ring while it stays near, so its ripple and height do not jump.
			foreach (Ring ring in _rings)
				if (ring != null && ring.Visible && !Nearest(ring.ZoneId))
					ring.Hide();

			foreach (Zone zone in _nearest)
			{
				Vector3 local = new Vector3(zone.X, 0f, zone.Z) - origin;
				Ring free = null;
				bool placed = false;
				for (int i = 0; i < _rings.Length && !placed; i++)
				{
					Ring ring = _rings[i];
					if (ring != null && ring.Visible && ring.ZoneId == zone.Id)
					{
						ring.Place(local, zone.Id);
						placed = true;
					}
					else if (free == null && (ring == null || !ring.Visible))
						free = ring ?? (_rings[i] = CreateRing());
				}

				if (!placed) free?.Place(local, zone.Id);
			}
		}

		private bool Nearest(uint id)
		{
			foreach (Zone zone in _nearest)
				if (zone.Id == id)
					return true;
			return false;
		}

		private Ring CreateRing()
		{
			if (_material == null)
			{
				_material = new Material(PatchShader)
				{
					name = "Omens Fishing Patch", hideFlags = HideFlags.HideAndDontSave,
					renderQueue = 3000, color = Color.white
				};
				// A soft glow that fades as the bite bonus does, and a thin ripple running out over it.
				_glowMesh = RadialMesh("Omens Fishing Patch Glow", new[] { 0f, 0.25f, 0.5f, 0.75f, 1f },
					new[] { 0.35f, 0.3f, 0.175f, 0.055f, 0f });
				_rippleMesh = RadialMesh("Omens Fishing Patch Ripple", new[] { 0.88f, 0.96f, 1f },
					new[] { 0f, 0.7f, 0f });
			}

			return Ring.Create(_material, _glowMesh, _rippleMesh);
		}

		/// <summary>Rings of vertices at the given radii, coloured with the given alphas.</summary>
		private static Mesh RadialMesh(string name, float[] radii, float[] alphas)
		{
			int rings = radii.Length;
			var vertices = new Vector3[Segments * rings];
			var colors = new Color[vertices.Length];
			var triangles = new List<int>(Segments * (rings - 1) * 6);
			for (int s = 0; s < Segments; s++)
			{
				float angle = s * Mathf.PI * 2f / Segments;
				Vector3 direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
				for (int r = 0; r < rings; r++)
				{
					vertices[s * rings + r] = direction * radii[r];
					colors[s * rings + r] = new Color(0.12f, 0.75f, 1f, alphas[r]);
				}
			}

			for (int s = 0; s < Segments; s++)
			{
				int next = (s + 1) % Segments;
				for (int r = 0; r + 1 < rings; r++)
				{
					int a = s * rings + r, b = a + 1, c = next * rings + r, d = c + 1;
					triangles.Add(a);
					triangles.Add(b);
					triangles.Add(d);
					// At the centre the inner corners are one point: the second half would be empty.
					if (radii[r] <= 0f) continue;
					triangles.Add(a);
					triangles.Add(d);
					triangles.Add(c);
				}
			}

			var mesh = new Mesh { name = name, hideFlags = HideFlags.HideAndDontSave };
			mesh.vertices = vertices;
			mesh.colors = colors;
			mesh.SetTriangles(triangles, 0);
			mesh.RecalculateBounds();
			return mesh;
		}

		private void HideRings()
		{
			foreach (Ring ring in _rings) ring?.Hide();
		}

		private bool Contains(uint id)
		{
			foreach (Zone zone in _zones)
				if (zone.Id == id)
					return true;
			return false;
		}

		private static Vector3 OriginOffset()
		{
			return twoloop.OriginShift.singleton != null
				? twoloop.OriginShift.LocalOffset.ToVector3()
				: Vector3.zero;
		}

		private static float DistanceSquared(Zone zone, Vector3 position)
		{
			float x = zone.X - position.x, z = zone.Z - position.z;
			return x * x + z * z;
		}

		private static uint Hash(int seed, int x, int z)
		{
			unchecked
			{
				uint value = (uint)seed ^ ((uint)x * 0x9E3779B9u) ^ ((uint)z * 0x85EBCA6Bu);
				value ^= value >> 16;
				value *= 0x7FEB352Du;
				value ^= value >> 15;
				value *= 0x846CA68Bu;
				value ^= value >> 16;
				return value;
			}
		}

		private static bool IsGeneratedZone(int seed, uint id, float x, float z)
		{
			int cellX = Mathf.FloorToInt(x / CellSize);
			int cellZ = Mathf.FloorToInt(z / CellSize);
			uint expectedId = Hash(seed, cellX, cellZ);
			if (id != expectedId || Chance(id) >= 0.2f) return false;
			float expectedX = (cellX + Unit(id ^ 0xA341316Cu)) * CellSize;
			float expectedZ = (cellZ + Unit(id ^ 0xC8013EA4u)) * CellSize;
			return Mathf.Abs(x - expectedX) < 0.05f && Mathf.Abs(z - expectedZ) < 0.05f;
		}

		private static float Unit(uint value) => (value & 0x00FFFFFFu) / 16777216f;
		private static float Chance(uint value) => Unit(Hash(unchecked((int)value), 0x51ED270B, -0x2A5A5A5B));
		private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

		private readonly struct Zone
		{
			public readonly uint Id;
			public readonly float X, Z;

			public Zone(uint id, float x, float z)
			{
				Id = id;
				X = x;
				Z = z;
			}
		}

		private sealed class Ring
		{
			private static MaterialPropertyBlock _block;
			private static readonly int ColorId = Shader.PropertyToID("_Color");

			private readonly GameObject _root;
			private readonly Transform _ripple;
			private readonly MeshRenderer _glowRenderer, _rippleRenderer;

			// Its own height query: Crest answers a query a frame or more later, for the point it was given.
			private readonly SampleHeightHelper _height = new SampleHeightHelper();
			private Vector3 _position;
			private float _phase;

			public uint ZoneId { get; private set; }
			public bool Visible { get; private set; }

			private Ring(GameObject root, MeshRenderer glow, MeshRenderer ripple)
			{
				_root = root;
				_glowRenderer = glow;
				_rippleRenderer = ripple;
				_ripple = ripple.transform;
			}

			public static Ring Create(Material material, Mesh glow, Mesh ripple)
			{
				var root = new GameObject("Omens Fishing Patch") { hideFlags = HideFlags.HideAndDontSave };
				var ring = new Ring(root, Part(root.transform, "Glow", glow, material),
					Part(root.transform, "Ripple", ripple, material));
				root.SetActive(false);
				return ring;
			}

			private static MeshRenderer Part(Transform parent, string name, Mesh mesh, Material material)
			{
				var part = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave };
				part.transform.SetParent(parent, false);
				part.transform.localScale = Vector3.one * Radius;
				part.AddComponent<MeshFilter>().sharedMesh = mesh;
				var renderer = part.AddComponent<MeshRenderer>();
				renderer.sharedMaterial = material;
				renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
				renderer.receiveShadows = false;
				renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
				renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
				return renderer;
			}

			public void Place(Vector3 local, uint id)
			{
				if (!Visible || ZoneId != id)
				{
					OceanRenderer ocean = OceanRenderer.Instance;
					_position = new Vector3(local.x, (ocean != null ? ocean.SeaLevel : 0f) + Lift, local.z);
					_phase = (id & 0xFFFFu) / 65536f;
					ZoneId = id;
				}
				else
				{
					_position.x = local.x;
					_position.z = local.z;
				}

				if (Visible || _root == null) return;
				Visible = true;
				_root.transform.position = _position;
				_root.SetActive(true);
			}

			public void Animate(float time, float strength)
			{
				if (!Visible || _root == null) return;
				_height.Init(_position);
				if (_height.Sample(out float surface)) _position.y = surface + Lift;
				_root.transform.position = _position;

				float alpha = Mathf.Clamp01(strength);
				float phase = Mathf.Repeat(time / PulseSeconds + _phase, 1f);
				_ripple.localScale = Vector3.one * (Radius * Mathf.Lerp(0.2f, 1f, phase));
				Tint(_glowRenderer, alpha);
				Tint(_rippleRenderer, alpha * Mathf.Sin(Mathf.PI * phase));
			}

			private static void Tint(Renderer renderer, float alpha)
			{
				if (_block == null) _block = new MaterialPropertyBlock();
				_block.SetColor(ColorId, new Color(1f, 1f, 1f, alpha));
				renderer.SetPropertyBlock(_block);
			}

			public void Shift(Vector3 offset)
			{
				_position += offset;
				if (Visible && _root != null) _root.transform.position = _position;
			}

			public void Hide()
			{
				if (!Visible) return;
				Visible = false;
				if (_root != null) _root.SetActive(false);
			}

			public void Destroy()
			{
				if (_root != null) UnityEngine.Object.Destroy(_root);
			}
		}
	}
}

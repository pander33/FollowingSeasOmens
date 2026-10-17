using Enviro;
using UnityEngine;

namespace FollowingSeasOmens
{
	/// <summary>
	/// A meteor shower: every second or two a falling star flashes across the sky away from one point (the
	/// radiant), fast and gone in half a second, and now and then a slower, coloured fireball that throws
	/// sparks, flares before it goes out, lights the deck for a moment and leaves a glowing train drifting
	/// in the wind.
	///
	/// The light is worked out per pixel by the mod's own shader (<c>omens.bundle</c>, see
	/// <see cref="OmensAssets"/>): a line never thinner than a pixel, a round head and a tail fading
	/// exponentially behind it. The mesh is only strips on a sphere round the camera, along each path,
	/// carrying where a point lies against the path in radians. Low in the sky a star is seen through more
	/// air and from farther off, so it is fainter and thinner. Without the bundle the omen is off.
	/// </summary>
	internal sealed class Meteors : Omen
	{
		private const int Pool = 12;
		private const int StreakSegments = 24;
		private const int TrainSegments = 20;
		private const int Sparks = 48;
		private const float Radius = 3000f;

		/// <summary>Seconds between falling stars on average, at full strength.</summary>
		private const float MeanInterval = 1.6f;

		/// <summary>One in this many is a fireball.</summary>
		private const int FireballOdds = 8;

		private const int StreakVerts = (StreakSegments + 1) * 2;
		private const int TrainVerts = (TrainSegments + 1) * 2;
		private const int MeteorVerts = StreakVerts + TrainVerts;

		/// <summary>The strip's half width, in the line's sigmas: room for the tail's sheen.</summary>
		private const float Room = 32f;

		/// <summary>The head's glow, as the shader draws it: a sigma of so many pixels at size 1.</summary>
		private const float GlowPixels = 7f;

		private static readonly int StrengthId = Shader.PropertyToID("_Strength");

		private static readonly Color Head = new Color(1f, 1f, 0.97f);
		private static readonly Color Tail = new Color(0.78f, 0.9f, 1f);
		private static readonly Color Train = new Color(0.35f, 0.8f, 0.62f);

		private static readonly Color[] Fireballs =
		{
			new Color(0.4f, 1f, 0.45f), new Color(1f, 0.55f, 0.2f), new Color(0.6f, 0.7f, 1f)
		};

		private struct Meteor
		{
			public bool On, Fireball, Burst, Trains;
			public Vector3 Start, Along, Normal;
			public float Age, Life, Travel, TailLength, Sigma, Brightness, FadeAt, Noise, Drift;
			public float TrainLight, TrainFade, TrainSpread, SparkWait;
			public Color HeadColor, TailColor, TrainColor;
		}

		private struct Spark
		{
			public bool On;
			public Vector3 Dir, Velocity;
			public float Age, Life, Light;
			public Color Color;
		}

		private readonly Transform _parent;
		private readonly Meteor[] _meteors = new Meteor[Pool];
		private readonly Spark[] _sparks = new Spark[Sparks];
		private GameObject _go;
		private MeshRenderer _renderer;
		private Mesh _mesh;
		private Material _mat;
		private Light _light;

		/// <summary>The brightest directional light but ours, when the game sets no sun.</summary>
		private Light _main;
		private float _searchAt;
		private static bool _flashLogged;

		private Vector3[] _verts;
		private Vector4[] _tail;
		private Vector4[] _path, _shape, _head, _edge;
		private System.Random _rng;
		private Vector3 _radiant, _side;
		private float _wait;
		private bool _waiting;

		public Meteors(Transform parent)
		{
			_parent = parent;
		}

		public override string Name => "Meteor shower";
		public override string Announcement => "Stars are falling from the sky";
		public override string JournalText => "Stars fell from the sky all night: a meteor shower";

		protected override bool Begin(EnviroManager enviro)
		{
			// Without its shader the omen cannot be drawn: it goes off, once, with the reason in the log.
			Shader shader = OmensAssets.Shader("Meteor");
			if (shader == null) throw new System.InvalidOperationException("the meteor shader is not available");

			// The radiant and the order of the stars come from the omen's seed, the co-op host's on a
			// guest; a random of its own, so Unity's sequence, shared with the game, is not touched.
			_rng = new System.Random(Seed);
			float azimuth = Range(0f, 360f);
			float elevation = Range(35f, 70f);
			_radiant = Quaternion.Euler(-elevation, azimuth, 0f) * Vector3.forward;
			_side = Vector3.Cross(_radiant, Vector3.up).normalized;
			_waiting = false;
			for (int i = 0; i < Pool; i++) _meteors[i].On = false;
			for (int i = 0; i < Sparks; i++) _sparks[i].On = false;

			bool made = false;
			try
			{
				_mat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
				_mesh = MakeMesh();
				_mesh.hideFlags = HideFlags.HideAndDontSave;

				_go = new GameObject("Omens Meteors") { hideFlags = HideFlags.HideAndDontSave };
				_go.transform.SetParent(_parent, false);
				_go.AddComponent<MeshFilter>().sharedMesh = _mesh;
				_renderer = _go.AddComponent<MeshRenderer>();
				_renderer.sharedMaterial = _mat;
				_renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
				_renderer.receiveShadows = false;
				_renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
				_renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

				// The flash of a fireball on the ship and the sea: a directional light from it, without
				// shadows, on only while something is bright, and kept below the night's own light
				// (see FlashCap).
				var flash = new GameObject("Omens Meteor Flash") { hideFlags = HideFlags.HideAndDontSave };
				flash.transform.SetParent(_go.transform, false);
				_light = flash.AddComponent<Light>();
				_light.type = LightType.Directional;
				_light.shadows = LightShadows.None;
				_light.intensity = 0f;
				_light.enabled = false;
				made = true;
				return true;
			}
			finally
			{
				if (!made) End();
			}
		}

		protected override void Draw(EnviroManager enviro, float strength)
		{
			// Not drawn, and no stars fall, while it cannot be seen (by day, at the start of dusk).
			bool visible = strength > 0.001f;
			if (_renderer.enabled != visible) _renderer.enabled = visible;
			Camera cam = visible ? Camera.main : null;
			if (cam == null)
			{
				if (_light.enabled) _light.enabled = false;
				return;
			}

			Transform view = cam.transform;
			_go.transform.SetPositionAndRotation(view.position, Quaternion.identity);
			_mat.SetFloat(StrengthId, strength);
			// Radians per pixel at the middle of the screen: the strips are made wide enough for the
			// shader's line, which is never thinner than a pixel.
			float pixel = 2f * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) / Mathf.Max(1, cam.pixelHeight);

			// The first wait too is drawn once it can be seen, so it also follows the strength.
			float dt = Time.deltaTime;
			if (!_waiting)
			{
				_waiting = true;
				_wait = NextWait(strength);
			}

			_wait -= dt;
			if (_wait <= 0f)
			{
				Spawn();
				_wait = NextWait(strength);
			}

			float flash = 0f, brightest = 0f;
			Color flashColor = Color.white;
			Vector3 flashFrom = Vector3.up;
			for (int i = 0; i < Pool; i++)
			{
				ref Meteor m = ref _meteors[i];
				if (m.On)
				{
					m.Age += dt;
					// The head goes out at Life; a train glows on a while after.
					if (m.Age >= m.Life + (m.Trains ? m.TrainFade * 4f : 0f)) m.On = false;
				}

				if (!m.On)
				{
					Hide(i * MeteorVerts, MeteorVerts);
					continue;
				}

				float light = Place(i * MeteorVerts, ref m, dt, pixel, out Vector3 head);
				flash += light;
				if (light > brightest)
				{
					brightest = light;
					flashColor = m.HeadColor;
					flashFrom = head;
				}
			}

			int first = Pool * MeteorVerts;
			for (int i = 0; i < Sparks; i++)
			{
				ref Spark s = ref _sparks[i];
				if (s.On)
				{
					s.Age += dt;
					if (s.Age >= s.Life) s.On = false;
				}

				if (!s.On)
				{
					Hide(first + i * 4, 4);
					continue;
				}

				s.Dir = (s.Dir + s.Velocity * dt).normalized;
				s.Velocity *= 1f - Mathf.Min(1f, 1.5f * dt);
				float left = 1f - s.Age / s.Life;
				float light = s.Light * Mathf.Pow(left, 1.5f) * (0.6f + 0.4f * Mathf.PerlinNoise(i * 3.1f, s.Age * 30f));
				PlaceSpark(first + i * 4, s, light, pixel);
			}

			_mesh.vertices = _verts;
			_mesh.SetUVs(0, _path);
			_mesh.SetUVs(1, _shape);
			_mesh.SetUVs(2, _head);
			_mesh.SetUVs(3, _tail);
			_mesh.SetUVs(4, _edge);

			if (flash > 0f) flash = Mathf.Min(FlashCap(), flash) * strength;
			bool lit = flash > 0.002f;
			if (_light.enabled != lit) _light.enabled = lit;
			if (lit)
			{
				float max = Mathf.Max(flashColor.r, Mathf.Max(flashColor.g, flashColor.b));
				_light.color = max > 0f ? flashColor / max : Color.white;
				_light.intensity = flash;
				_light.transform.rotation = Quaternion.LookRotation(-flashFrom);
			}
		}

		/// <summary>
		/// How bright the flash may be. URP takes <c>RenderSettings.sun</c> as its main light or, with none
		/// set, the brightest directional light. With a sun set the flash may outshine the moon a little;
		/// without one it stays below the brightest light, so it never becomes the main light and takes the
		/// shadows and the sea's light for a moment. Which it is, is logged once.
		/// </summary>
		private float FlashCap()
		{
			Light sun = RenderSettings.sun;
			if (sun == null && (_main == null || !_main.isActiveAndEnabled) && Time.unscaledTime >= _searchAt)
			{
				_searchAt = Time.unscaledTime + 1f;
				_main = Brightest();
			}

			Light main = sun != null ? sun : _main;
			float night = main != null && main.isActiveAndEnabled ? main.intensity : 0f;
			if (!_flashLogged)
			{
				_flashLogged = true;
				Plugin.Log.LogInfo("Meteor flash: RenderSettings.sun " + (sun != null ? "is " + sun.name : "is not set") +
				                   "; main light " + (main != null ? main.name + " at " + night.ToString("0.###",
					                   System.Globalization.CultureInfo.InvariantCulture) : "none"));
			}

			return sun != null ? Mathf.Min(0.5f, 2f * night) : 0.8f * night;
		}

		private Light Brightest()
		{
			Light best = null;
			foreach (var light in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
				if (light != _light && light.type == LightType.Directional && light.isActiveAndEnabled &&
				    (best == null || light.intensity > best.intensity))
					best = light;
			return best;
		}

		protected override void End()
		{
			if (_go != null) Object.Destroy(_go);
			if (_mesh != null) Object.Destroy(_mesh);
			if (_mat != null) Object.Destroy(_mat);
			_go = null;
			_renderer = null;
			_light = null;
			_main = null;
			_mesh = null;
			_mat = null;
		}

		/// <summary>A new star in a free slot: a few degrees to half the sky off the radiant, moving away
		/// from it along a great circle, and above the horizon all the way.</summary>
		private void Spawn()
		{
			int slot = -1;
			for (int i = 0; i < Pool; i++)
				if (!_meteors[i].On)
				{
					slot = i;
					break;
				}

			if (slot < 0) return;

			bool fireball = _rng.Next(FireballOdds) == 0;
			for (int tries = 0; tries < 6; tries++)
			{
				float away = Range(8f, 55f);
				Vector3 axis = Quaternion.AngleAxis(Range(0f, 360f), _radiant) * _side;
				Vector3 start = Quaternion.AngleAxis(away, axis) * _radiant;
				Vector3 outward = (start - _radiant * Vector3.Dot(start, _radiant)).normalized;
				float a = away * Mathf.Deg2Rad;
				Vector3 along = -Mathf.Sin(a) * _radiant + Mathf.Cos(a) * outward;

				// An ordinary star crosses 40-90 degrees a second, a fireball a third of that.
				float speed = (fireball ? Range(15f, 35f) : Range(40f, 90f)) * Mathf.Deg2Rad;
				float life = fireball ? Range(1.5f, 3f) : Range(0.25f, 0.8f);
				life = Mathf.Min(life, (fireball ? 60f : 35f) * Mathf.Deg2Rad / speed);
				var m = new Meteor
				{
					On = true,
					Fireball = fireball,
					Start = start,
					Along = along,
					Normal = Vector3.Cross(start, along),
					Life = life,
					Travel = speed * life,
					// The tail fades by e over the way the head goes in this many seconds.
					TailLength = speed * (fireball ? 0.15f : 0.025f) * Range(0.8f, 1.2f),
					Sigma = fireball ? Range(1.2e-3f, 2e-3f) : Range(1e-4f, 2.5e-4f),
					Brightness = fireball ? 7f : 3f * Range(0.45f, 1f),
					FadeAt = fireball ? Range(0.85f, 0.95f) : Range(0.6f, 0.95f),
					Noise = Range(0f, 100f),
					Drift = Range(-0.006f, 0.006f)
				};
				if (start.y < 0.17f || At(m, m.Travel).y < 0.07f) continue;

				// Fireballs, and a few bright stars, leave a glowing train.
				m.Trains = fireball || Range(0f, 1f) < 0.3f;
				m.TrainLight = fireball ? 0.6f : 0.25f;
				m.TrainFade = fireball ? Range(2.5f, 5f) : Range(0.3f, 0.6f);
				m.TrainSpread = fireball ? 1.2e-3f : 2e-4f;
				if (fireball)
				{
					Color c = Fireballs[_rng.Next(Fireballs.Length)];
					m.HeadColor = Color.Lerp(c, Color.white, 0.5f);
					m.TailColor = c;
					m.TrainColor = Color.Lerp(Train, c, 0.4f);
				}
				else
				{
					m.HeadColor = Head;
					m.TailColor = Tail;
					m.TrainColor = Train;
				}

				_meteors[slot] = m;
				return;
			}
		}

		/// <summary>The point on the sky an angle along the star's path.</summary>
		private static Vector3 At(in Meteor m, float angle) => m.Start * Mathf.Cos(angle) + m.Along * Mathf.Sin(angle);

		/// <summary>A point an angle along the path and an angle across it, on the sphere.</summary>
		private static Vector3 On(in Meteor m, float along, float across) =>
			(At(m, along) * Mathf.Cos(across) + m.Normal * Mathf.Sin(across)) * Radius;

		/// <summary>Writes one star; returns how much it lights the scene.</summary>
		private float Place(int v, ref Meteor m, float dt, float pixel, out Vector3 headDir)
		{
			bool burning = m.Age < m.Life;
			float u = Mathf.Min(1f, m.Age / m.Life);
			// Lights up at once, brightens a little, goes out quickly; a fireball flares near its end and
			// flickers as it breaks up.
			float bright = burning
				? Smooth(0f, 0.08f, u) * (1f - Smooth(m.FadeAt, Mathf.Min(m.FadeAt + 0.3f, 1f), u)) *
				  Mathf.Lerp(0.8f, 1f, Smooth(0f, 0.5f, u))
				: 0f;
			if (burning && m.Fireball)
			{
				bright += 1.4f * Mathf.Exp(-Sq((u - 0.82f) / 0.06f));
				bright *= 0.85f + 0.15f * Mathf.PerlinNoise(m.Noise, m.Age * 25f);
			}

			// The head slows a little as it goes.
			float headAt = m.Travel * u * (1.1f - 0.1f * u);
			headDir = At(m, headAt);
			// Low in the sky a star is seen through more air and from farther off: fainter and thinner.
			float high = Mathf.Clamp01(headDir.y);
			float air = Mathf.Lerp(0.3f, 1f, Mathf.Sqrt(high));
			float sigma = m.Sigma * Mathf.Lerp(0.55f, 1f, high);

			if (burning)
			{
				float glowSize = m.Fireball ? 2f : 1f;
				float glow = Glow(sigma, pixel, glowSize);
				float width = Width(sigma, pixel, glow);
				float from = headAt - 8f * m.TailLength - width, to = headAt + width;
				var shape = new Vector4(m.TailLength, sigma, 1f, m.Fireball ? 0.6f : 0.4f);
				float light = m.Brightness * bright * air;
				Color tail = m.TailColor * light;
				Vector4 head = (Vector4)(m.HeadColor * light);
				for (int k = 0; k <= StreakSegments; k++)
				{
					float s = Mathf.Lerp(from, to, k / (float)StreakSegments);
					for (int side = 0; side < 2; side++)
					{
						float x = side == 0 ? -width : width;
						int i = v + k * 2 + side;
						_verts[i] = On(m, s, x);
						_tail[i] = (Vector4)tail;
						_path[i] = new Vector4(headAt - s, x, s, glowSize);
						_shape[i] = shape;
						_head[i] = head;
						_edge[i] = new Vector4(side == 0 ? -1f : 1f, (to - s) / width, (s - from) / width, glow);
					}
				}
			}
			else Hide(v, StreakVerts);

			// The train: where the head has been, glowing on, widening and drifting as it fades. Only the
			// shader's tail term, long and flat along, so the brightness is the vertices'.
			int t = v + StreakVerts;
			if (m.Trains)
			{
				float reach = burning ? headAt : m.Travel;
				for (int k = 0; k <= TrainSegments; k++)
				{
					float f = k / (float)TrainSegments;
					float s = reach * f;
					float since = Mathf.Max(0f, m.Age - s / m.Travel * m.Life);
					float spread = sigma * 3f + since * m.TrainSpread;
					float width = 12f * Mathf.Sqrt(spread * spread + 0.5f * pixel * pixel);
					float glow = m.TrainLight * air * Mathf.Exp(-since / m.TrainFade) * Mathf.Clamp01(since / 0.15f) *
					             Smooth(0f, 0.2f, f) * Smooth(1f, 0.75f, f) *
					             (0.6f + 0.4f * Mathf.PerlinNoise(s * 40f + m.Noise, since * 0.7f));
					Color c = m.TrainColor * glow;
					var shape = new Vector4(1000f, spread, 0f, 1f);
					for (int side = 0; side < 2; side++)
					{
						float x = side == 0 ? -width : width;
						int i = t + k * 2 + side;
						_verts[i] = On(m, s, x + m.Drift * since);
						_tail[i] = (Vector4)c;
						_path[i] = new Vector4(1f, x, 10000f, 0f);
						_shape[i] = shape;
						_head[i] = Vector4.zero;
						_edge[i] = new Vector4(side == 0 ? -1f : 1f, 10f, 10f, 0f);
					}
				}
			}
			else Hide(t, TrainVerts);

			// A fireball throws sparks as it burns, and a handful at once when it flares.
			if (burning && m.Fireball)
			{
				m.SparkWait -= dt * 30f * Mathf.Min(1f, bright);
				while (m.SparkWait <= 0f)
				{
					m.SparkWait += 1f;
					Throw(m, headDir, 1f, air);
				}

				if (!m.Burst && u >= 0.8f)
				{
					m.Burst = true;
					for (int k = 0; k < 10; k++) Throw(m, headDir, 1.8f, air);
				}
			}

			// The light it throws: a fireball's flare lights the deck, an ordinary star hardly at all.
			return (m.Fireball ? 0.12f : 0.01f) * bright * bright * air;
		}

		/// <summary>A spark off the head, carried on along the path, a little aside, and soon out.</summary>
		private void Throw(in Meteor m, Vector3 head, float scatter, float air)
		{
			for (int i = 0; i < Sparks; i++)
			{
				if (_sparks[i].On) continue;
				float speed = m.Travel / m.Life;
				Vector3 aside = Vector3.Cross(m.Along, head);
				_sparks[i] = new Spark
				{
					On = true,
					Dir = head,
					Velocity = m.Along * speed * Range(0.3f, 0.8f) + aside * speed * Range(-0.25f, 0.25f) * scatter,
					Life = Range(0.3f, 0.9f) * scatter,
					Light = m.Brightness * 0.35f * air,
					Color = Color.Lerp(m.TailColor, m.HeadColor, Range(0f, 0.5f))
				};
				return;
			}
		}

		/// <summary>A spark: the shader's head term only, on a small square round it.</summary>
		private void PlaceSpark(int v, in Spark s, float light, float pixel)
		{
			const float sigma = 1.5e-4f;
			float glow = Glow(sigma, pixel, 0.5f);
			float width = Width(sigma, pixel, glow);
			Vector3 along = s.Velocity.sqrMagnitude > 1e-12f ? s.Velocity.normalized : Vector3.Cross(s.Dir, Vector3.up).normalized;
			Vector3 across = Vector3.Cross(s.Dir, along);
			var shape = new Vector4(1e-4f, sigma, 1f, 0f);
			Vector4 head = (Vector4)(s.Color * light);
			for (int k = 0; k < 4; k++)
			{
				float a = k < 2 ? -width : width;
				float b = k == 0 || k == 3 ? -width : width;
				_verts[v + k] = (s.Dir + along * a + across * b).normalized * Radius;
				_tail[v + k] = Vector4.zero;
				_path[v + k] = new Vector4(-a, b, 10000f, 0.5f);
				_shape[v + k] = shape;
				_head[v + k] = head;
				_edge[v + k] = new Vector4(b / width, (width - a) / width, (width + a) / width, glow);
			}
		}

		/// <summary>The sigma of a head's glow, in radians.</summary>
		private static float Glow(float sigma, float pixel, float size) => Mathf.Max(sigma * 12f, GlowPixels * pixel) * size;

		/// <summary>Half the width of a strip round a star's path: room for its tail's sheen and for its head's
		/// glow out to 4.5 sigmas. The shader fades everything out over the outer half, so the glow is whole
		/// to past two sigmas and nothing ends in a hard edge.</summary>
		private static float Width(float sigma, float pixel, float glow) =>
			Mathf.Max(Room * Mathf.Sqrt(sigma * sigma + 0.5f * pixel * pixel), 4.5f * glow);

		private void Hide(int v, int count)
		{
			for (int k = 0; k < count; k++)
			{
				_verts[v + k] = Vector3.zero;
				_tail[v + k] = Vector4.zero;
				_head[v + k] = Vector4.zero;
				_edge[v + k] = Vector4.zero;
			}
		}

		/// <summary>Seconds to the next star: random arrivals, the mean longer at a low strength.</summary>
		private float NextWait(float strength) =>
			-Mathf.Log(1f - (float)_rng.NextDouble() * 0.95f) * MeanInterval / Mathf.Max(0.2f, strength);

		private float Range(float min, float max) => min + (float)_rng.NextDouble() * (max - min);

		private static float Sq(float x) => x * x;

		private static float Smooth(float from, float to, float x) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(from, to, x));

		private Mesh MakeMesh()
		{
			int count = Pool * MeteorVerts + Sparks * 4;
			_verts = new Vector3[count];
			_tail = new Vector4[count];
			_path = new Vector4[count];
			_shape = new Vector4[count];
			_head = new Vector4[count];
			_edge = new Vector4[count];
			var tris = new System.Collections.Generic.List<int>(count * 3);
			for (int i = 0; i < Pool; i++)
			{
				int v = i * MeteorVerts;
				StripMesh(v, StreakSegments, tris);
				StripMesh(v + StreakVerts, TrainSegments, tris);
			}

			for (int i = 0; i < Sparks; i++)
			{
				int v = Pool * MeteorVerts + i * 4;
				tris.AddRange(new[] { v, v + 1, v + 2, v, v + 2, v + 3 });
			}

			var mesh = new Mesh { name = "Omens Meteors" };
			mesh.vertices = _verts;
			mesh.SetUVs(0, _path);
			mesh.SetUVs(1, _shape);
			mesh.SetUVs(2, _head);
			mesh.SetUVs(3, _tail);
			mesh.SetUVs(4, _edge);
			mesh.SetTriangles(tris, 0);
			// The whole sky round the camera, so the mesh is never culled.
			mesh.bounds = new Bounds(Vector3.zero, Vector3.one * (Radius * 2.5f));
			mesh.MarkDynamic();
			return mesh;
		}

		private static void StripMesh(int v, int segments, System.Collections.Generic.List<int> tris)
		{
			for (int k = 0; k < segments; k++)
			{
				int a = v + k * 2;
				tris.AddRange(new[] { a, a + 1, a + 3, a, a + 3, a + 2 });
			}
		}
	}
}

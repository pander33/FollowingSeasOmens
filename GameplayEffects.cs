using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace FollowingSeasOmens
{
	internal sealed class GameplayEffects : MonoBehaviour
	{
		/// <summary>How far a guest's compass clock may drift from the host's before it is set again:
		/// setting it on every snapshot would make the needle jump by the network delay.</summary>
		private const float ClockSlack = 0.25f;

		internal static GameplayEffects Instance { get; private set; }
		internal static float BloodStrength { get; private set; }
		internal static float SizeBias { get; private set; } = 1f;
		internal static float CompassOffset { get; private set; }

		private readonly FishingPatches _patches = new FishingPatches();
		private GameplayLink _link;
		private Harmony _harmony;
		private float _bloodBite = 1f, _patchBite = 1f, _seaStrength;
		private float _clock, _lastSent;
		private int _role;
		private string _received;
		private string _appliedSnapshot;
		private Transform _player;
		private float _remoteClock, _remoteAmplitude;
		private bool _remoteClockSet;
		private int _remoteSeed;
		private int _remotePatchSeed;
		private int _generation;
		private float _hostBlood, _hostSize = 1f, _hostBite = 1f, _hostSea;

		private void Awake()
		{
			Instance = this;
			_link = new GameplayLink(text => _received = text);
			_harmony = new Harmony(Plugin.Guid + ".gameplay");
			// Each part goes in or stays out by itself: a game update that breaks one leaves the others.
			Install("Blood-moon rod bites", typeof(FishingEffects), FishingEffects.InstallRodBite);
			Install("Blood-moon rod fish size", typeof(FishingEffects), FishingEffects.InstallRodSize);
			Install("Trolling bites", typeof(FishingEffects), FishingEffects.InstallTrollingBite);
			Install("Blood-moon trolling catch size", typeof(FishingEffects), FishingEffects.InstallTrollingSize);
			Install("Ship compass disturbance", typeof(CompassEffects), CompassEffects.InstallShip);
			Install("Handheld compass disturbance", typeof(CompassEffects), CompassEffects.InstallHandheld);
		}

		private void Install(string name, Type owner, Action<PatchGroup> install)
		{
			var group = new PatchGroup(_harmony, owner);
			try
			{
				install(group);
			}
			catch (Exception error)
			{
				group.Undo();
				Plugin.Log.LogWarning(name + " is off: the game's code is not what this build expects. " +
				                      (error.InnerException ?? error).Message);
			}
		}

		internal void Apply(Omen[] omens, int role)
		{
			if (_role != role)
			{
				Reset();
				_received = null;
				_role = role;
			}

			_link.Tick();
			if (_generation != _link.Generation)
			{
				Reset();
				_generation = _link.Generation;
			}

			var dp = DataPersistenceManager.dpInstance;
			if (dp == null || dp.player == null || Enviro.EnviroManager.instance == null)
			{
				Reset();
				return;
			}

			if (_player != dp.player.transform)
			{
				Reset();
				_player = dp.player.transform;
			}

			if (!dp.paused) _clock += Time.deltaTime;
			if (role == CoopLink.Guest)
			{
				if (!_link.Ready || string.IsNullOrEmpty(_received) ||
				    _received != _appliedSnapshot && !ReadSnapshot(_received))
				{
					ClearEffects();
					_received = null;
				}
				else
				{
					_appliedSnapshot = _received;
					BloodStrength = _hostBlood;
					SizeBias = _hostSize;
					_bloodBite = _hostBite;
					_seaStrength = _hostSea;
					if (!dp.paused) _remoteClock += Time.deltaTime;
					CompassOffset = Offset(_remoteClock, _remoteSeed) * _remoteAmplitude;
					if (omens[0].Broken || !omens[0].Active || omens[0].EffectiveStrength <= 0f)
					{
						BloodStrength = 0f;
						SizeBias = _bloodBite = 1f;
					}

					if (omens[1].Broken || !omens[1].Active || omens[1].EffectiveStrength <= 0f)
						CompassOffset = 0f;
					if (omens[2].Broken || !omens[2].Active || omens[2].EffectiveStrength <= 0f)
						_seaStrength = 0f;
				}

				_patches.Update(dp.player.transform, _remotePatchSeed, _seaStrength, false);
				if (Mathf.Approximately(CompassOffset, 0f)) CompassEffects.Reset();
				return;
			}

			bool gameplayEnabled = Plugin.GameplayEnabled.Value && (role == CoopLink.None || _link.Ready);
			BloodStrength = gameplayEnabled && Plugin.BloodFishingEnabled.Value ? omens[0].EffectiveStrength : 0f;
			_bloodBite = Mathf.Lerp(1f, Plugin.BloodBiteMultiplier.Value, BloodStrength);
			SizeBias = Mathf.Lerp(1f, Plugin.BloodSizeBias.Value, BloodStrength);
			_seaStrength = gameplayEnabled && Plugin.FishingPatchesEnabled.Value ? omens[2].EffectiveStrength : 0f;
			_patchBite = Plugin.PatchBiteMultiplier.Value;
			float aurora = gameplayEnabled && Plugin.CompassEnabled.Value ? omens[1].EffectiveStrength : 0f;
			CompassOffset = Offset(_clock, omens[1].Seed) * Plugin.CompassAmplitude.Value * aurora;
			_patches.Update(dp.player.transform, omens[2].Seed, _seaStrength, true);
			if (Mathf.Approximately(CompassOffset, 0f)) CompassEffects.Reset();
			if (role == CoopLink.Host && Time.unscaledTime - _lastSent >= 1f)
			{
				_lastSent = Time.unscaledTime;
				_link.Publish(string.Join(";", "1", F(BloodStrength), F(SizeBias), F(_bloodBite),
					F(_seaStrength), F(_patchBite), F(_clock), F(Plugin.CompassAmplitude.Value * aurora),
					omens[1].Seed.ToString(CultureInfo.InvariantCulture), _patches.Serialize()));
			}
		}

		internal static float BiteMultiplier(Vector3 position)
		{
			var self = Instance;
			if (self == null) return 1f;
			float local = Mathf.Lerp(1f, self._patchBite, self._seaStrength * self._patches.Influence(position));
			return Mathf.Clamp(self._bloodBite * local, 1f, 2f);
		}

		private static float Offset(float time, int seed)
		{
			float phase = (uint)seed % 6283 * 0.001f;
			return 0.65f * Mathf.Sin(2f * Mathf.PI * 0.7f * time + phase) +
			       0.35f * Mathf.Sin(2f * Mathf.PI * 1.9f * time + phase * 1.7f);
		}

		private bool ReadSnapshot(string text)
		{
			if (text.Length > 24000) return false;
			string[] parts = text.Split(new[] { ';' }, 10);
			if (parts.Length != 10 || parts[0] != "1") return false;
			var values = new float[7];
			for (int i = 0; i < values.Length; i++)
				if (!float.TryParse(parts[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out values[i]) ||
				    float.IsNaN(values[i]) || float.IsInfinity(values[i]))
					return false;
			if (values[0] < 0f || values[0] > 1f || values[1] < 0.1f || values[1] > 1f ||
			    values[2] < 1f || values[2] > 2f || values[3] < 0f || values[3] > 1f ||
			    values[4] < 1f || values[4] > 2f || values[5] < 0f || values[6] < 0f || values[6] > 30f ||
			    !int.TryParse(parts[8], NumberStyles.Integer, CultureInfo.InvariantCulture, out int seed)) return false;
			if (!_patches.Receive(parts[9])) return false;
			_remotePatchSeed = _patches.Seed;
			_hostBlood = values[0];
			_hostSize = values[1];
			_hostBite = values[2];
			_hostSea = values[3];
			_patchBite = values[4];
			if (!_remoteClockSet || seed != _remoteSeed || Mathf.Abs(values[5] - _remoteClock) > ClockSlack)
			{
				_remoteClock = values[5];
				_remoteClockSet = true;
			}

			_remoteAmplitude = values[6];
			_remoteSeed = seed;
			return true;
		}

		private static string F(float value) => value.ToString("R", CultureInfo.InvariantCulture);

		private void ClearEffects()
		{
			_appliedSnapshot = null;
			_remoteClockSet = false;
			BloodStrength = CompassOffset = _seaStrength = 0f;
			SizeBias = _bloodBite = _patchBite = 1f;
			CompassEffects.Reset();
			_patches.Reset();
		}

		internal void Reset()
		{
			ClearEffects();
			CompassEffects.Forget();
			_received = null;
			_appliedSnapshot = null;
			_player = null;
		}

		private void OnDestroy()
		{
			Reset();
			_patches.Dispose();
			_link?.Dispose();
			_harmony?.UnpatchSelf();
			if (Instance == this) Instance = null;
		}
	}

	/// <summary>The Harmony patches of one gameplay part, taken out together when one of them does not fit.</summary>
	internal sealed class PatchGroup
	{
		private readonly Harmony _harmony;
		private readonly Type _owner;
		private readonly List<KeyValuePair<MethodBase, MethodInfo>> _patches =
			new List<KeyValuePair<MethodBase, MethodInfo>>();

		public PatchGroup(Harmony harmony, Type owner)
		{
			_harmony = harmony;
			_owner = owner;
		}

		public void Patch(MethodBase original, string prefix = null, string postfix = null, string transpiler = null,
			string finalizer = null)
		{
			if (original == null) throw new MissingMethodException("a patched game method is missing");
			_harmony.Patch(original, prefix: Make(original, prefix), postfix: Make(original, postfix),
				transpiler: Make(original, transpiler), finalizer: Make(original, finalizer),
				ilmanipulator: null);
		}

		private HarmonyMethod Make(MethodBase original, string name)
		{
			if (name == null) return null;
			MethodInfo method = AccessTools.Method(_owner, name) ??
			                    throw new MissingMethodException(_owner.Name, name);
			// Recorded before patching, so a patch that fails half way is taken out as well.
			_patches.Add(new KeyValuePair<MethodBase, MethodInfo>(original, method));
			return new HarmonyMethod(method);
		}

		public void Undo()
		{
			foreach (var patch in _patches)
			{
				try
				{
					_harmony.Unpatch(patch.Key, patch.Value);
				}
				catch (Exception)
				{
					// Not patched after all.
				}
			}

			_patches.Clear();
		}
	}
}

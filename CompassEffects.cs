using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace FollowingSeasOmens
{
	internal static class CompassEffects
	{
		private static readonly Dictionary<Transform, Quaternion> Applied = new Dictionary<Transform, Quaternion>();

		// What each instrument is never changes; asked once, not every frame.
		private static readonly Dictionary<int, bool> Needles = new Dictionary<int, bool>();
		private static readonly Dictionary<int, bool> ShipDials = new Dictionary<int, bool>();

		private static readonly System.Reflection.FieldInfo Axis = AccessTools.Field(typeof(HandheldCompassManager),
			"axis");

		private static readonly System.Reflection.FieldInfo Camera =
			AccessTools.Field(typeof(HandheldCompassManager), "mainCamTransform");

		internal static void InstallShip(PatchGroup patches) =>
			patches.Patch(AccessTools.Method(typeof(CompassManager), "Update"),
				prefix: nameof(BeforeUpdate), postfix: nameof(AfterShipUpdate));

		internal static void InstallHandheld(PatchGroup patches)
		{
			if (Axis == null || Camera == null) throw new MissingFieldException("HandheldCompassManager fields");
			patches.Patch(AccessTools.Method(typeof(HandheldCompassManager), "Update"),
				prefix: nameof(BeforeUpdate), postfix: nameof(AfterHandheldUpdate), transpiler: nameof(BearingTranspiler));
		}

		private static void BeforeUpdate(Component __instance)
		{
			if (Applied.Count == 0) return;
			Transform instrument = __instance.transform;
			if (!Applied.TryGetValue(instrument, out var correction)) return;
			instrument.localRotation *= Quaternion.Inverse(correction);
			Applied.Remove(instrument);
		}

		private static void AfterShipUpdate(CompassManager __instance)
		{
			if (GameplayEffects.CompassOffset == 0f) return;
			int id = __instance.GetInstanceID();
			if (!ShipDials.TryGetValue(id, out bool dial))
				ShipDials[id] = dial = __instance.name == "Compass_Dial_Holder";
			if (dial) Apply(__instance.transform);
		}

		private static void AfterHandheldUpdate(HandheldCompassManager __instance)
		{
			if (GameplayEffects.CompassOffset == 0f) return;
			int id = __instance.GetInstanceID();
			if (!Needles.TryGetValue(id, out bool needle))
				// The game uses this component only on the inventory compasses (handheld_sun_compass in the
				// main world, handheld_compass in two others): the y one is the needle, x and z hold the rings.
				Needles[id] = needle = (int)Axis.GetValue(__instance) == 1;
			if (needle) Apply(__instance.transform);
		}

		private static void Apply(Transform instrument)
		{
			float offset = GameplayEffects.CompassOffset;
			if (offset == 0f) return;
			Quaternion correction = Quaternion.Euler(0f, -offset, 0f);
			instrument.localRotation *= correction;
			Applied[instrument] = correction;
		}

		private static IEnumerable<CodeInstruction> BearingTranspiler(IEnumerable<CodeInstruction> instructions)
		{
			var codes = new List<CodeInstruction>(instructions);
			var setter = AccessTools.PropertySetter(typeof(TMP_Text), nameof(TMP_Text.text));
			var output = AccessTools.Field(typeof(HandheldCompassManager), "outputText");
			int matches = 0;
			for (int i = 0; i < codes.Count; i++)
			{
				if (!codes[i].LoadsField(output) || i + 1 >= codes.Count) continue;
				int end = i + 1;
				while (end < codes.Count && !codes[end].Calls(setter) && end - i < 50) end++;
				if (end == codes.Count || !codes[end].Calls(setter)) continue;
				codes[end].opcode = OpCodes.Call;
				codes[end].operand = AccessTools.Method(typeof(CompassEffects), nameof(SetBearing));
				codes.Insert(end, new CodeInstruction(OpCodes.Ldarg_0));
				matches++;
				i = end + 1;
			}

			if (matches != 1) throw new InvalidOperationException("Handheld compass bearing IL changed");
			return codes;
		}

		private static void SetBearing(TMP_Text output, string vanilla, HandheldCompassManager instrument)
		{
			// The bearing is the compass's reading, whichever of its rings writes it.
			if (GameplayEffects.CompassOffset == 0f)
			{
				output.text = vanilla;
				return;
			}

			var camera = (Transform)Camera.GetValue(instrument);
			float bearing = Mathf.Repeat(camera.eulerAngles.y + GameplayEffects.CompassOffset, 360f);
			output.text = $"{Mathf.Round(bearing * 100f) / 100f} Deg";
		}

		internal static void Reset()
		{
			if (Applied.Count == 0) return;
			foreach (var pair in Applied)
				if (pair.Key != null)
					pair.Key.localRotation *= Quaternion.Inverse(pair.Value);
			Applied.Clear();
		}

		/// <summary>Another world: its instruments are other objects.</summary>
		internal static void Forget()
		{
			Needles.Clear();
			ShipDials.Clear();
		}
	}
}

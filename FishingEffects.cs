using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace FollowingSeasOmens
{
	internal static class FishingEffects
	{
		private static readonly FieldInfo FishWeight = AccessTools.Field(typeof(Fish), "weight");
		private static readonly FieldInfo FishLength = AccessTools.Field(typeof(Fish), "length");
		private static readonly FieldInfo FishMinWeight = AccessTools.Field(typeof(Fish), "minWeight");
		private static readonly FieldInfo FishMaxWeight = AccessTools.Field(typeof(Fish), "maxWeight");
		private static readonly FieldInfo FishMinLength = AccessTools.Field(typeof(Fish), "minLength");
		private static readonly FieldInfo FishMaxLength = AccessTools.Field(typeof(Fish), "maxLength");
		private static readonly FieldInfo BiteHook = AccessTools.Field(typeof(FishingController), "hook");
		private static FieldInfo _biteController;

		private static readonly ConditionalWeakTable<GameObject, CatchReward> Rewards =
			new ConditionalWeakTable<GameObject, CatchReward>();

		private sealed class CatchReward
		{
			public int Units;
		}

		internal static int CatchUnits(GameObject fish) => Rewards.TryGetValue(fish, out var reward) ? reward.Units : 1;

		internal static void ReceiveCatchUnits(GameObject fish, int units)
		{
			if (units < 1 || units > 4) return;
			Rewards.Remove(fish);
			Rewards.Add(fish, new CatchReward { Units = units });
		}

		private sealed class RodSize
		{
			public float Bias, Adjusted;
		}

		[ThreadStatic] private static Stack<RodSize> RodSizeBias;

		[ThreadStatic] private static HashSet<FishPoint> TrollingPoints;
		[ThreadStatic] private static int TrollingDepth;

		private static MethodInfo RollForFish =>
			AccessTools.Method(typeof(TrollController), "RollForFish", new[] { typeof(List<GameObject>) });

		public static void InstallRodBite(PatchGroup patches)
		{
			var bite = AccessTools.Method(typeof(FishingController), "Bite");
			var stateMachine = bite?.GetCustomAttribute<IteratorStateMachineAttribute>()?.StateMachineType;
			_biteController = stateMachine == null
				? null
				: Array.Find(stateMachine.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic),
					field => field.FieldType == typeof(FishingController));
			if (_biteController == null || BiteHook == null)
				throw new MissingMemberException("FishingController.Bite");
			patches.Patch(AccessTools.Method(stateMachine, "MoveNext"), transpiler: nameof(BiteMoveNextTranspiler));
		}

		public static void InstallRodSize(PatchGroup patches)
		{
			if (FishWeight == null || FishLength == null || FishMinWeight == null || FishMaxWeight == null ||
			    FishMinLength == null || FishMaxLength == null)
				throw new MissingFieldException("Fish size fields");
			patches.Patch(AccessTools.Method(typeof(FishingController), "GetFish"),
				prefix: nameof(RodGetFishPrefix), finalizer: nameof(RodGetFishFinalizer));
			patches.Patch(AccessTools.Method(typeof(Fish), "Awake"),
				postfix: nameof(FishAwakePostfix), transpiler: nameof(FishSizeTranspiler));
		}

		public static void InstallTrollingBite(PatchGroup patches) =>
			patches.Patch(RollForFish, transpiler: nameof(TrollingChanceTranspiler));

		public static void InstallTrollingSize(PatchGroup patches)
		{
			patches.Patch(RollForFish, prefix: nameof(TrollingPrefix), finalizer: nameof(TrollingFinalizer));
			patches.Patch(AccessTools.Method(typeof(FishPoint), nameof(FishPoint.SetScale)),
				prefix: nameof(SetScalePrefix));
			patches.Patch(AccessTools.Method(typeof(ObjectInteractionHandler), "OnTriggerEnter"),
				transpiler: nameof(DepositTranspiler));
		}

		private static void RodGetFishPrefix()
		{
			if (RodSizeBias == null)
				RodSizeBias = new Stack<RodSize>();
			RodSizeBias.Push(new RodSize { Bias = GameplayEffects.SizeBias });
		}

		private static IEnumerable<CodeInstruction> BiteMoveNextTranspiler(IEnumerable<CodeInstruction> instructions)
		{
			var codes = new List<CodeInstruction>(instructions);
			var range = AccessTools.Method(typeof(UnityEngine.Random), nameof(UnityEngine.Random.Range),
				new[] { typeof(int), typeof(int) });
			var waitCtor = AccessTools.Constructor(typeof(WaitForSeconds), new[] { typeof(float) });
			var waitOperand = (MethodBase)waitCtor;
			var waitIndexes = new List<int>();
			for (int i = 0; i < codes.Count - 1; i++)
			{
				if (i + 2 < codes.Count && codes[i].Calls(range) && codes[i + 1].opcode == OpCodes.Conv_R4 &&
				    codes[i + 2].opcode == OpCodes.Newobj && Equals(codes[i + 2].operand, waitOperand))
					waitIndexes.Add(i);
			}

			if (waitIndexes.Count != 1) throw new InvalidOperationException("FishingController.Bite IL changed");

			int waitIndex = waitIndexes[0];
			codes[waitIndex + 1].opcode = OpCodes.Ldarg_0;
			codes.Insert(waitIndex + 2, new CodeInstruction(OpCodes.Ldfld, _biteController));
			codes.Insert(waitIndex + 3, new CodeInstruction(OpCodes.Ldfld, BiteHook));
			codes.Insert(waitIndex + 4, new CodeInstruction(OpCodes.Call,
				AccessTools.Method(typeof(FishingEffects), nameof(ScaleBiteWait))));
			return codes;
		}

		private static float ScaleBiteWait(int wait, Transform hook) =>
			hook == null ? wait : wait / GameplayEffects.BiteMultiplier(hook.position);

		private static IEnumerable<CodeInstruction> TrollingChanceTranspiler(IEnumerable<CodeInstruction> instructions)
		{
			var codes = new List<CodeInstruction>(instructions);
			var range = AccessTools.Method(typeof(UnityEngine.Random), nameof(UnityEngine.Random.Range),
				new[] { typeof(int), typeof(int) });
			var matchingCalls = new List<int>();
			for (var index = 0; index < codes.Count; index++)
				if (codes[index].Calls(range))
					matchingCalls.Add(index);

			if (matchingCalls.Count != 1) throw new InvalidOperationException("RollForFish IL changed");

			int call = matchingCalls[0];
			// The foreach current object is stored immediately before the two Range arguments.
			if (call < 3 || !(codes[call - 3].opcode == OpCodes.Stloc || codes[call - 3].opcode == OpCodes.Stloc_S ||
			                  codes[call - 3].opcode == OpCodes.Stloc_0 || codes[call - 3].opcode == OpCodes.Stloc_1 ||
			                  codes[call - 3].opcode == OpCodes.Stloc_2 || codes[call - 3].opcode == OpCodes.Stloc_3))
				throw new InvalidOperationException("RollForFish current-point IL changed");
			var store = codes[call - 3];
			CodeInstruction load;
			if (store.opcode == OpCodes.Stloc_0) load = new CodeInstruction(OpCodes.Ldloc_0);
			else if (store.opcode == OpCodes.Stloc_1) load = new CodeInstruction(OpCodes.Ldloc_1);
			else if (store.opcode == OpCodes.Stloc_2) load = new CodeInstruction(OpCodes.Ldloc_2);
			else if (store.opcode == OpCodes.Stloc_3) load = new CodeInstruction(OpCodes.Ldloc_3);
			else load = new CodeInstruction(OpCodes.Ldloc, store.operand);
			codes[call].operand = AccessTools.Method(typeof(FishingEffects), nameof(AdjustTrollingRoll));
			codes.Insert(call, load);
			return codes;
		}

		private static int AdjustTrollingRoll(int minimum, int maximum, GameObject point)
		{
			float multiplier = GameplayEffects.BiteMultiplier(point.transform.position);
			if (multiplier <= 1f) return UnityEngine.Random.Range(minimum, maximum);
			return UnityEngine.Random.value < 0.25f * multiplier ? 0 : 1;
		}

		private static Exception RodGetFishFinalizer(Exception __exception)
		{
			if (RodSizeBias != null && RodSizeBias.Count > 0)
				RodSizeBias.Pop();
			return __exception;
		}

		private static void FishAwakePostfix(Fish __instance)
		{
			if (RodSizeBias == null || RodSizeBias.Count == 0)
				return;

			var size = RodSizeBias.Peek();
			if (size.Bias >= 1f)
				return;

			var minimumWeight = (float)FishMinWeight.GetValue(__instance);
			var maximumWeight = (float)FishMaxWeight.GetValue(__instance);
			var minimumLength = (float)FishMinLength.GetValue(__instance);
			var maximumLength = (float)FishMaxLength.GetValue(__instance);
			var adjustedT = size.Adjusted;
			var weight = minimumWeight + (maximumWeight - minimumWeight) * adjustedT;
			var length = adjustedT * (maximumLength - minimumLength) + minimumLength;

			FishWeight.SetValue(__instance, weight);
			FishLength.SetValue(__instance, length);
			var scale = length / minimumLength;
			__instance.transform.localScale = new Vector3(scale, scale, scale);
		}

		private static IEnumerable<CodeInstruction> FishSizeTranspiler(IEnumerable<CodeInstruction> instructions)
		{
			var codes = new List<CodeInstruction>(instructions);
			var power = AccessTools.Method(typeof(Mathf), nameof(Mathf.Pow));
			int count = 0;
			foreach (var code in codes)
				if (code.Calls(power))
				{
					code.operand = AccessTools.Method(typeof(FishingEffects), nameof(CaptureSize));
					count++;
				}

			if (count != 1) throw new InvalidOperationException("Fish size IL changed");
			return codes;
		}

		private static float CaptureSize(float sample, float exponent)
		{
			if (RodSizeBias != null && RodSizeBias.Count > 0)
			{
				var size = RodSizeBias.Peek();
				size.Adjusted = Mathf.Pow(sample, exponent * size.Bias);
			}

			return Mathf.Pow(sample, exponent);
		}

		private static void TrollingPrefix(List<GameObject> pool)
		{
			if (TrollingDepth++ > 0)
				return;

			var points = TrollingPoints ?? (TrollingPoints = new HashSet<FishPoint>());
			points.Clear();
			foreach (var item in pool)
				if (item != null)
				{
					var point = item.GetComponent<FishPoint>();
					if (point != null)
					{
						points.Add(point);
					}
				}
		}

		private static Exception TrollingFinalizer(Exception __exception)
		{
			if (TrollingDepth > 0 && --TrollingDepth == 0)
				TrollingPoints?.Clear();
			return __exception;
		}

		private static void SetScalePrefix(FishPoint __instance, ref float scale)
		{
			if (TrollingPoints == null || !TrollingPoints.Contains(__instance))
				return;

			Rewards.Remove(__instance.fish);
			if (GameplayEffects.SizeBias >= 1f) return;

			var normalized = Mathf.Clamp01((scale - __instance.minScale) / (__instance.maxScale - __instance.minScale));
			var boosted = Mathf.Pow(normalized, GameplayEffects.SizeBias);
			// One extra native cargo unit for each crossed quarter of the species' size range.
			int extra = Mathf.FloorToInt(boosted * 4f) - Mathf.FloorToInt(normalized * 4f);
			Rewards.Add(__instance.fish, new CatchReward { Units = 1 + extra });
			scale = __instance.minScale + (__instance.maxScale - __instance.minScale) * boosted;
		}

		private static IEnumerable<CodeInstruction> DepositTranspiler(IEnumerable<CodeInstruction> instructions)
		{
			var codes = new List<CodeInstruction>(instructions);
			var add = AccessTools.Method(typeof(FishCargoManager), nameof(FishCargoManager.AddFish));
			int count = 0;
			for (int i = 0; i < codes.Count; i++)
			{
				if (!codes[i].Calls(add)) continue;
				codes[i].opcode = OpCodes.Call;
				codes[i].operand = AccessTools.Method(typeof(FishingEffects), nameof(DepositCatch));
				codes.Insert(i++, new CodeInstruction(OpCodes.Ldarg_0));
				count++;
			}

			if (count != 1) throw new InvalidOperationException("Fish hold deposit IL changed");
			return codes;
		}

		private static void DepositCatch(FishCargoManager hold, ObjectInteractionHandler fish)
		{
			// The hook's fish object is used again for the next catch: the bonus is taken once, and
			// after that the fish counts as the game counts it, whoever put it on the hook.
			int units = 1;
			if (Rewards.TryGetValue(fish.gameObject, out var reward))
			{
				units = reward.Units;
				Rewards.Remove(fish.gameObject);
			}

			for (int i = 0; i < units; i++) hold.AddFish();
		}
	}
}

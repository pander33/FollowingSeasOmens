# Gameplay implementation handoff

Date: 2026-10-17. Implements `GAMEPLAY_PLAN.md`. Uncommitted in Omens and in the sibling Co-op;
nothing published. Release 0.1.0 (tag on `7c845f3`, dark-night fix `f48d813`) does not contain this work.

## Current state

- Omens Release build: 0 errors, 0 warnings (2026-10-17, after the review fixes below).
  Co-op was last built on 2026-10-17, before these fixes, which do not touch it (0/0, ProtocolSmoke 204/204).
- No in-game run of any new mechanic yet. DevConsole (port 7778) was not reachable.

## Files

- `Plugin.cs`: gameplay config; `GameplayEffects` is added before `OmenDirector`.
- `Omen.cs`: `EffectiveStrength` (0 unless drawn this frame). `OmenDirector.cs`: calls
  `GameplayEffects.Apply` after the visuals, `Reset` in `StopAll`.
- `GameplayEffects.cs`: strengths, bite cap 2, compass oscillator, host snapshot / guest use, and
  `PatchGroup`: six Harmony groups (rod bite, rod size, trolling bite, trolling size, ship compass,
  handheld compass), each installed or taken out on its own with one warning.
- `FishingEffects.cs`: rod wait transpiler, rod size (difficulty kept), trolling chance, trolling size as
  extra cargo units (one per crossed quarter, at most +1 at 0.65), hold deposit.
- `CompassEffects.cs`: ship `Compass_Dial_Holder` needles and every `HandheldCompassManager` on axis y;
  bearing text replaced before `savedAzimuth` copies it. The game uses that component only on the
  inventory compass: `handheld_sun_compass` in `gameplay.unity` (level3, inventory slot 5) and
  `handheld_compass` in `storm_saettia` / `other_place_1`. The first build filtered on the name
  `handheld_compass` and so did nothing in the main world (seen in game 2026-10-17, fixed same day).
- `FishingPatches.cs`: 500 m cells, chance 0.2, radius 40 m, 8 rendered; host-authoritative zones.
- `GameplayLink.cs`: channel `com.followingseas.omens.gameplay`, hello/ack per roster member, chunked
  snapshot, 5 s timeouts, log lines when the effects come on or stay off.
- Sibling Co-op (`Runtime/ModChannel.cs`, `Runtime/CoopBehaviour.cs`, `Sync/ControlsSync.Hull.cs`):
  `RegisterPeer`, `Members`, `SessionIdentity`, `CatchUnits`/`ReceiveCatchUnits`; Api stays 1,
  wire messages unchanged. Omens finds the new members by reflection; an older Co-op means no gameplay.

## Review fixes, 2026-10-17 (built, not run)

- Patches: land test is raycasts against the player's ground masks at sea level, ignoring rigidbodies
  (ships, cargo); Crest is no longer asked there (one `SampleHeightHelper` cannot serve many points
  in a frame). `Influence` no longer re-tests water, so a guest uses the host's zones as they are.
  The host drops near zones that turn out to be land. Each ring has its own height query, keeps its
  zone while it stays near, and draws a soft glow plus a ripple running outwards (one shared material,
  `MaterialPropertyBlock` for alpha).
- Hold deposit takes an existing bonus once and removes it; a fish without an entry counts 1 as in the
  game. Before, a consumed marker stayed on the reused hook fish and a guest whose host sent no units
  put 0 fish in the hold.
- Guest compass clock is set from the host only when it drifts by more than 0.25 s.
- Host roster read every 0.25 s without string building; compass instrument checks cached by instance id.
- Unused reflection requirements removed from `GameplayLink.Resolve`; its failure is logged.

## Next

1. Back up the test copy's saves, restart the game with the new Omens and Co-op DLLs.
2. Run the matrix in `GAMEPLAY_PLAN.md` §10. First: patch visibility (Sprites/Default, glow and ripple
   at Low and higher quality), land rejection near shore and piers, compass axis of the ship dial,
   bearing text and saved azimuth, trolling extra units in single player and with a guest collecting.
3. Co-op: host + guest, guest without Omens (host log must say who is missing), late join.
4. Update README / CLAUDE.md with what was seen; commit only when asked.

## Paths

- Decompile (read only): `B:\FollowingSeasModTest\_decompiled\build-25792767`; installed
  `Assembly-CSharp.dll` SHA-256 `DD0965B76FE6DEC0A98F17705CB97D0A7C30394E68E2D56D54B45071CDE81ADB`.
- Crest is in `Following Seas_Data\Managed\Crest.dll` (not in the decompile; `ilspycmd` reads it).
- Co-op sibling: `B:\FollowingSeasModTest\FollowingSeasCoop`.

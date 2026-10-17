# 🌙 Following Seas Omens

[![Version](https://img.shields.io/badge/version-0.2.0-blue)](#-install)
[![Game](https://img.shields.io/badge/game-Following%20Seas-1b2838?logo=steam&logoColor=white)](https://store.steampowered.com/app/2895440/)
[![BepInEx](https://img.shields.io/badge/BepInEx-5.4.23%2B-orange)](https://github.com/BepInEx/BepInEx/releases)
[![Omens](https://img.shields.io/badge/omens-5-purple)](#-the-omens)
[![Status](https://img.shields.io/badge/status-early%20version-yellow)](#-if-something-goes-wrong)
[![License](https://img.shields.io/badge/license-MIT-green)](LICENSE)

Now and then a night at sea in [Following Seas](https://store.steampowered.com/app/2895440/) is not like
the others: a blood red moon rises, the northern lights burn across the sky, the sea glows in your wake,
stars fall from the sky, or the night is so dark that only a lantern shows anything.

Unofficial; not made or endorsed by Whipstaff Games.

## 🌌 The omens

| Omen | What you see |
|---|---|
| **Blood moon** | A large full moon, dark red. The sky and the night light turn crimson. |
| **Northern lights** | Curtains of green light across the sky, purple at the top; red in a great storm. |
| **Glowing sea** | Glowing plankton: the bow wave, the wake and the breaking crests shine blue, and the water around them glows. |
| **Meteor shower** | Every second or two a falling star flashes away from one point of the sky; now and then a slow green, orange or blue fireball throws sparks, flares, lights the deck and leaves a glowing train. |
| **Dark night** | No moon, no stars, a black sky and a black sea. Without a lantern you see nothing. |

With gameplay effects enabled, a blood moon brings more bites and larger fish without harder fights,
northern lights make magnetic compasses tremble, and glowing feeding patches offer better fishing.
The ship, weather and wind are unchanged. Dark night adds no hidden penalties: its danger is poor visibility.
Disable `Gameplay / Enabled` to keep the visual omens without the new fishing and compass effects.

## 🧭 How it works

- **Each night is rolled at dusk.** Every omen has its own chance, so most nights have none and a
  few have two at once. A dark night never comes together with another omen: if one rolls, it wins.
- **An omen comes slowly** as the sky darkens, is strongest in the middle of the night, and fades
  at dawn.
- **You are told.** A line comes up at the top of the screen, and the **captain's log** gets an entry
  with the place and the date.
- **Saving and loading are safe.** Load the game in the middle of a night and the same omens come
  back; the log does not get the entry twice.

The game's clock does not show local time: night falls around **04:00** and ends around **12:00** by
it (a little earlier or later, depending on where you sail). Look for the omens then.

## 📦 Install

You need Following Seas on Windows and **BepInEx 5** (x64, version 5.4.23 or newer), a free loader for
game mods.

Download [version 0.2.0](https://github.com/pander33/FollowingSeasOmens/releases/tag/v0.2.0):
choose `FollowingSeasOmens-0.2.0.zip` with BepInEx included, or
`FollowingSeasOmens-0.2.0-plugin-only.zip` if BepInEx is already installed.
Close the game and unpack one archive into the game folder. When upgrading, replace both
`FollowingSeasOmens.dll` and `omens.bundle`.

1. **Close the game and find its folder.** In Steam: right-click Following Seas → **Manage** →
   **Browse local files**. It is the folder with `Following Seas.exe`.
2. **No BepInEx yet?** Download `BepInEx_win_x64_5.4.23.x.zip` from
   [BepInEx releases](https://github.com/BepInEx/BepInEx/releases) and unpack it into that folder, so
   that `winhttp.dll` lies next to `Following Seas.exe`. Start the game once and close it: BepInEx
   makes its folders.
3. **Put the mod in.** Create the folder `BepInEx\plugins\FollowingSeasOmens` and put
   `FollowingSeasOmens.dll` and `omens.bundle` (the meteor shower's shader) into it.
4. **Start the game.** Load your world, wait for night, or press **F9** at night to see an omen at
   once (see [Keys](#-keys)).

**To remove the mod**, delete the `BepInEx\plugins\FollowingSeasOmens` folder. Your saves keep
nothing of it except the lines it wrote in the captain's log.

## ⌨️ Keys

| Key | What it does |
|---|---|
| **F9** | Calls the omens by hand, one press after another: blood moon → northern lights → glowing sea → dark night → meteor shower → all of them but the dark night → none. An omen called this way comes in 2 seconds. Pressed by day, it waits for the night. Nothing is written in the log. |

The keys do nothing while the game's menu is open.

## ⚙️ Settings

The settings file is `BepInEx\config\com.followingseas.omens.cfg`. It appears after the first start
with the mod; open it in Notepad with the game closed.

| Setting | Default | What it does |
|---|---|---|
| `Chance / BloodMoon` | `0.04` | Chance that a night has a blood moon: `0` never, `1` every night. |
| `Chance / Aurora` | `0.06` | The same for the northern lights. |
| `Chance / GlowingSea` | `0.10` | The same for the glowing sea. |
| `Chance / DarkNight` | `0.08` | The same for a dark night. |
| `Chance / Meteors` | `0.06` | The same for a meteor shower. |
| `Look / FadeSeconds` | `20` | How many seconds an omen takes to come and to go. |
| `Keys / SummonKey` | `F9` | The key that calls the omens by hand. `None` switches it off. |
| `Keys / NightKey` | `None` | For testing: a key (for example `F10`) that moves the clock to the middle of the night, or by night to noon. It changes the time in your world. |
| `Journal / WriteJournal` | `true` | Whether an omen writes an entry in the captain's log. |
| `UI / Announce` | `true` | Whether the line at the top of the screen is shown. |
| `Gameplay / Enabled` | `true` | Enable the new fishing and compass mechanics; visual omens remain when disabled. |
| `BloodMoon / FishingEnabled` | `true` | More frequent bites and larger fish, without increased fighting difficulty. |
| `BloodMoon / BiteMultiplier` | `1.5` | Bite frequency at full strength. |
| `BloodMoon / SizeBiasMultiplier` | `0.65` | Smaller values favour larger fish within their normal size limits; `1` leaves size unchanged. |
| `Aurora / CompassEnabled` | `true` | Magnetic compass disturbance during northern lights. |
| `Aurora / CompassAmplitudeDegrees` | `8` | Maximum compass deviation at full strength. |
| `GlowingSea / FishingPatchesEnabled` | `true` | Glowing feeding patches with a real local fishing bonus. |
| `GlowingSea / PatchBiteMultiplier` | `1.5` | Bite frequency at the centre of a feeding patch. Combined fishing bonuses never exceed `2`. |

Feeding patches are stable for the night and reward fishing inside them, not merely standing nearby.
They do not guarantee a catch or increase fish size. The blood moon size bonus is retained by the catch
after dawn. A dark night lasts until dawn and excludes all other omens.

Trolling catches use the game's native cargo units rather than individual fish weights.
Each quarter of the normalised size range crossed by the blood-moon bonus adds one cargo unit
when that catch enters the hold. Ordinary catches still give one unit; the bonus is fixed at capture.
The host sends the catch reward with the existing trolling-line state, and the collecting player's
normal economy update carries the resulting hold change once.

Gameplay rules in co-op come from the host. New gameplay effects require compatible Omens and Co-op
builds on every participant; unsupported sessions keep only the available visual effects,
and the BepInEx log says which player has not confirmed a compatible build.
These gameplay additions are included in 0.2.0, not in the previously published 0.1.0 archives.
In-game validation of the new fishing, compass, patch rendering and multiplayer paths is still required.
The meteor shader bundle was built with Unity 6000.4; loading it in the game's Unity 6000.3
has not yet been verified. Back up your saves before trying this early version.

Want to see omens often? Set a chance to `0.5` or `1`.

## ❓ If something goes wrong

- **Nothing happens at all.** Check that the file is at
  `BepInEx\plugins\FollowingSeasOmens\FollowingSeasOmens.dll` and that `BepInEx\LogOutput.log` has the
  line `Following Seas Omens ... loaded`. No such log file means BepInEx is not installed.
- **No meteor shower, and the log says `Could not load ...omens.bundle` or `Meteor shower failed`.**
  `omens.bundle` is missing from the mod's folder, or does not load in this version of the game. The
  other omens are not affected.
- **F9 shows a line, but the sky does not change.** It is day, or dawn is near: omens show only at
  night and are faint at dusk and dawn. Press F9 once and wait a few seconds; each press switches to
  the next omen.

Found a bug? Send `BepInEx\LogOutput.log` along with what you did.

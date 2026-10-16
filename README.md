# 🌙 Following Seas Omens

[![Version](https://img.shields.io/badge/version-0.1.0-blue)](#-install)
[![Game](https://img.shields.io/badge/game-Following%20Seas-1b2838?logo=steam&logoColor=white)](https://store.steampowered.com/app/2895440/)
[![BepInEx](https://img.shields.io/badge/BepInEx-5.4.23%2B-orange)](https://github.com/BepInEx/BepInEx/releases)
[![Omens](https://img.shields.io/badge/omens-4-purple)](#-the-omens)
[![Status](https://img.shields.io/badge/status-early%20version-yellow)](#-if-something-goes-wrong)
[![License](https://img.shields.io/badge/license-MIT-green)](LICENSE)

Now and then a night at sea in [Following Seas](https://store.steampowered.com/app/2895440/) is not like
the others: a blood red moon rises, the northern lights burn across the sky, the sea glows in your wake,
or the night is so dark that only a lantern shows anything.

Unofficial; not made or endorsed by Whipstaff Games.

## 🌌 The omens

| Omen | What you see |
|---|---|
| **Blood moon** | A large full moon, dark red. The sky and the night light turn crimson. |
| **Northern lights** | Curtains of green light across the sky, purple at the top; red in a great storm. |
| **Glowing sea** | Glowing plankton: the bow wave, the wake and the breaking crests shine blue, and the water around them glows. |
| **Dark night** | No moon, no stars, a black sky and a black sea. Without a lantern you see nothing. |

The omens only change how the night looks. Your ship, the weather, the wind and the fish are the same
as on any other night.

## 🧭 How it works

- **Each night is rolled at dusk.** Every omen has its own chance, so most nights have none and a
  few have two at once. A dark night never comes together with a blood moon, northern lights or a glowing sea.
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

1. **Close the game and find its folder.** In Steam: right-click Following Seas → **Manage** →
   **Browse local files**. It is the folder with `Following Seas.exe`.
2. **No BepInEx yet?** Download `BepInEx_win_x64_5.4.23.x.zip` from
   [BepInEx releases](https://github.com/BepInEx/BepInEx/releases) and unpack it into that folder, so
   that `winhttp.dll` lies next to `Following Seas.exe`. Start the game once and close it: BepInEx
   makes its folders.
3. **Put the mod in.** Create the folder `BepInEx\plugins\FollowingSeasOmens` and put
   `FollowingSeasOmens.dll` into it.
4. **Start the game.** Load your world, wait for night, or press **F9** at night to see an omen at
   once (see [Keys](#-keys)).

**To remove the mod**, delete the `BepInEx\plugins\FollowingSeasOmens` folder. Your saves keep
nothing of it except the lines it wrote in the captain's log.

## ⌨️ Keys

| Key | What it does |
|---|---|
| **F9** | Calls the omens by hand, one press after another: blood moon → northern lights → glowing sea → dark night → all of them but the dark night → none. An omen called this way comes in 2 seconds. Pressed by day, it waits for the night. Nothing is written in the log. |

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
| `Look / FadeSeconds` | `20` | How many seconds an omen takes to come and to go. |
| `Keys / SummonKey` | `F9` | The key that calls the omens by hand. `None` switches it off. |
| `Keys / NightKey` | `None` | For testing: a key (for example `F10`) that moves the clock to the middle of the night, or by night to noon. It changes the time in your world. |
| `Journal / WriteJournal` | `true` | Whether an omen writes an entry in the captain's log. |
| `UI / Announce` | `true` | Whether the line at the top of the screen is shown. |

Want to see omens often? Set a chance to `0.5` or `1`.

## ❓ If something goes wrong

- **Nothing happens at all.** Check that the file is at
  `BepInEx\plugins\FollowingSeasOmens\FollowingSeasOmens.dll` and that `BepInEx\LogOutput.log` has the
  line `Following Seas Omens ... loaded`. No such log file means BepInEx is not installed.
- **F9 shows a line, but the sky does not change.** It is day, or dawn is near: omens show only at
  night and are faint at dusk and dawn. Press F9 once and wait a few seconds; each press switches to
  the next omen.

Found a bug? Send `BepInEx\LogOutput.log` along with what you did.

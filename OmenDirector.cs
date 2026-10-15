using System;
using System.Globalization;
using System.Text;
using Enviro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FollowingSeasOmens
{
    /// <summary>
    /// Decides which omens a night has and drives them. Each time night falls (Enviro's <c>isNight</c>
    /// turns true, and once when a world is loaded at night) every omen is rolled with its chance;
    /// at dawn the night's omens fade out. The summon key steps through them by hand.
    ///
    /// Nothing happens outside a loaded world. During a Following Seas Co-op session the host rolls
    /// and calls the omens as in a game of its own and sends them to the guests (<see cref="CoopLink"/>);
    /// a guest shows what the host has and rolls nothing. The journal line is the host's: the co-op mod
    /// carries it. With a co-op build that cannot carry them, omens are off during a session.
    /// </summary>
    internal sealed class OmenDirector : MonoBehaviour
    {
        /// <summary>How often the host repeats its omens to the guests when nothing changed.</summary>
        private const float ResendSeconds = 2f;
        /// <summary>How fast an omen switched by the key comes and goes: the player wants to see it now.</summary>
        private const float SummonFadeSeconds = 2f;

        private Omen[] _omens;
        private Func<float>[] _chances;
        private bool? _wasNight;
        private int _summoned;
        private Key _summonKey, _nightKey;
        private float _statusAt;

        private CoopLink _coop;
        private string _sent;
        private float _sentAt;
        private int _toastId;
        private string _toastText = "";
        private int _hostToast = -1;

        private string _toast;
        private float _toastUntil;
        private GUIStyle _style;

        private void Start()
        {
            _omens = new Omen[] { new BloodMoon(), new Aurora(transform), new GlowingSea(), new DarkNight() };
            _chances = new Func<float>[]
            {
                () => Plugin.BloodMoonChance.Value,
                () => Plugin.AuroraChance.Value,
                () => Plugin.GlowingSeaChance.Value,
                () => Plugin.DarkNightChance.Value
            };
            _coop = new CoopLink(OnHostText);
            _summonKey = ParseKey(Plugin.SummonKey.Value, "SummonKey");
            _nightKey = ParseKey(Plugin.NightKey.Value, "NightKey");
        }

        private void Update()
        {
            var dp = DataPersistenceManager.dpInstance;
            EnviroManager enviro = dp != null && dp.player != null ? EnviroManager.instance : null;
            if (enviro == null)
            {
                StopAll();
                _wasNight = null;
                _summoned = 0;
                return;
            }

            bool pressed = Pressed(dp, _summonKey);
            bool nightPressed = Pressed(dp, _nightKey);
            int role = _coop.Role();
            if (role == CoopLink.Guest && _coop.CanShare)
            {
                // The host's omens, as its last message said; nothing is rolled here.
                if (pressed || nightPressed) Toast("During a co-op session the host calls the omens");
                _wasNight = null;
                _summoned = 0;
            }
            else if (role != CoopLink.None && !_coop.CanShare)
            {
                if (pressed || nightPressed) Toast("Omens are off during a co-op session");
                foreach (var omen in _omens) omen.Wanted = false;
                _wasNight = null;
                _summoned = 0;
            }
            else
            {
                if (pressed) Summon(enviro);
                if (nightPressed) JumpTime(enviro);
                bool night = enviro.isNight;
                if (_wasNight != night)
                {
                    // Night falling (false → true) or a world come into the night (null → true).
                    if (night) RollTheNight(enviro, _wasNight == false);
                    else
                    {
                        foreach (var omen in _omens) omen.Wanted = false;
                        _summoned = 0;
                    }
                    _wasNight = night;
                }
                if (role == CoopLink.Host) SendToGuests();
            }
            if (role != CoopLink.Guest) _hostToast = -1;

            if (_statusAt > 0f && Time.time >= _statusAt)
            {
                _statusAt = 0f;
                Plugin.Log.LogInfo("After the fade: " + Status(enviro));
            }

            float fade = Plugin.FadeSeconds.Value;
            foreach (var omen in _omens)
            {
                // One that waits for a conflicting omen to go keeps its level, so it still fades in after.
                if (!omen.Active && Blocked(omen, false)) continue;
                omen.Tick(Time.deltaTime, omen.Summoned ? SummonFadeSeconds : fade);
            }
        }

        private void LateUpdate()
        {
            var dp = DataPersistenceManager.dpInstance;
            EnviroManager enviro = dp != null && dp.player != null ? EnviroManager.instance : null;
            if (enviro == null) return;
            float darkness = Darkness(enviro);
            foreach (var omen in _omens)
                if (omen.Active || !Blocked(omen, false)) omen.Apply(enviro, darkness);
        }

        private void OnDestroy() => StopAll();

        private void StopAll()
        {
            if (_omens == null) return;
            foreach (var omen in _omens) omen.Stop();
        }

        /// <summary>1 in the dark of the night, 0 by day, a ramp through dusk and dawn. Enviro's solar
        /// time is 0.5 with the sun on the horizon and calls it night below 0.45.</summary>
        private static float Darkness(EnviroManager enviro) => Mathf.InverseLerp(0.48f, 0.40f, enviro.solarTime);

        /// <summary>
        /// Rolls the night's omens. The dice are the night's own (<see cref="NightNumber"/>): a world
        /// loaded again in the same night — by the player, or by the game itself — gets the same omens
        /// back. The log line is written only when night falls, not when a world comes into one: the
        /// save already holds the line written at dusk.
        /// </summary>
        private void RollTheNight(EnviroManager enviro, bool dusk)
        {
            int night = NightNumber(enviro);
            var dice = new System.Random(unchecked(night * 486187739 + 1013904223));
            string announced = null;
            for (int i = 0; i < _omens.Length; i++)
            {
                // Both are drawn for every omen, rolled or not, so each keeps its own numbers.
                double roll = dice.NextDouble();
                int seed = dice.Next(1, int.MaxValue);
                Omen omen = _omens[i];
                if (omen.Broken || Blocked(omen, true) || roll >= Mathf.Clamp01(_chances[i]())) continue;
                omen.Wanted = true;
                omen.Summoned = false;
                omen.Seed = seed;
                Plugin.Log.LogInfo(omen.Name + " tonight (night " + night + (dusk ? ", dusk" : ", world loaded") + ")");
                announced = announced == null ? omen.Announcement : announced + ". " + omen.Announcement;
                if (dusk && Plugin.WriteJournal.Value) WriteJournal(omen.JournalText);
            }
            if (announced != null) Announce(announced);
        }

        /// <summary>
        /// The number of the night on the game's calendar: days since year 1, shifted so that a night
        /// from dusk to dawn has one number though the date changes at midnight on the clock.
        /// </summary>
        private static int NightNumber(EnviroManager enviro)
        {
            EnviroTimeModule time = enviro.Time;
            if (time == null || time.Settings == null) return 0;
            int year = Mathf.Clamp(time.years, 1, 9999);
            int month = Mathf.Clamp(time.months, 1, 12);
            int day = Mathf.Clamp(time.days, 1, DateTime.DaysInMonth(year, month));
            double hours = new DateTime(year, month, day).Ticks / (double)TimeSpan.TicksPerDay * 24.0 + time.Settings.timeOfDay;
            return (int)Math.Floor((hours - LocalMidnight(time) + 12.0) / 24.0);
        }

        /// <summary>The hour on Enviro's clock when the sun is lowest. The clock is local time (UTC plus
        /// <c>utcOffset</c>); the sun is lowest at UTC midnight shifted by the longitude (negative is west).</summary>
        private static float LocalMidnight(EnviroTimeModule time) =>
            Mathf.Repeat(-time.Settings.longitude / 15f + time.Settings.utcOffset, 24f);

        private static int NewSeed() => UnityEngine.Random.Range(1, int.MaxValue);

        /// <summary>
        /// A dark night has no moon and nothing alight in the sky, so it never shares the night with a blood
        /// moon or northern lights (the blood moon also takes the same Enviro gradients). True when a
        /// conflicting omen is on the screen, or, with <paramref name="wanted"/>, already wanted.
        /// </summary>
        private bool Blocked(Omen omen, bool wanted)
        {
            foreach (var other in _omens)
                if (other != omen && Conflict(omen, other) && (other.Active || wanted && other.Wanted)) return true;
            return false;
        }

        private static bool Conflict(Omen a, Omen b) => a is DarkNight ? b is BloodMoon || b is Aurora
                                                       : b is DarkNight && (a is BloodMoon || a is Aurora);

        /// <summary>Next in the cycle: each omen alone, then all of them that go together, then none.</summary>
        private void Summon(EnviroManager enviro)
        {
            _summoned = (_summoned + 1) % (_omens.Length + 2);
            bool all = _summoned == _omens.Length + 1;
            for (int i = 0; i < _omens.Length; i++)
            {
                bool was = _omens[i].Wanted;
                _omens[i].Wanted = all ? !(_omens[i] is DarkNight) : _summoned == i + 1;
                if (_omens[i].Wanted && !was) _omens[i].Seed = NewSeed();
                if (_omens[i].Wanted || _omens[i].Level > 0f) _omens[i].Summoned = true;
            }

            string what = _summoned == 0 ? "none" : all ? "all but the dark night" : _omens[_summoned - 1].Name;
            Plugin.Log.LogInfo("Summoned: " + what + "; " + Status(enviro));
            _statusAt = Time.time + SummonFadeSeconds + 3f;
            Announce("Omen: " + what +(_summoned != 0 && !enviro.isNight ? " (shows at night)" : ""));
        }

        /// <summary>For testing: by day to the middle of the night, by night to noon.</summary>
        private void JumpTime(EnviroManager enviro)
        {
            if (enviro.Time == null || enviro.Time.Settings == null) return;
            float midnight = LocalMidnight(enviro.Time);
            bool toNoon = enviro.isNight;
            enviro.Time.SetTimeOfDay(toNoon ? Mathf.Repeat(midnight + 12f, 24f) : midnight);
            Toast((toNoon ? "Time: noon " : "Time: midnight ") + enviro.Time.GetTimeString());
            Plugin.Log.LogInfo("Time set to " + enviro.Time.GetTimeString());
        }

        private static bool Pressed(DataPersistenceManager dp, Key key) =>
            !dp.paused && key != Key.None && Keyboard.current != null && Keyboard.current[key].wasPressedThisFrame;

        /// <summary>Each omen's state and the night's, for the log.</summary>
        private string Status(EnviroManager enviro)
        {
            string s = "night=" + enviro.isNight + " solar=" + enviro.solarTime.ToString("0.000", CultureInfo.InvariantCulture) +
                       " darkness=" + Darkness(enviro).ToString("0.00", CultureInfo.InvariantCulture) +
                       " time=" + (enviro.Time != null ? enviro.Time.GetTimeString() : "?");
            foreach (var omen in _omens)
                s += " | " + omen.Name + ": wanted=" + omen.Wanted + " level=" +
                     omen.Level.ToString("0.00", CultureInfo.InvariantCulture) + " active=" + omen.Active +
                     (omen.Broken ? " BROKEN" : "");
            return s;
        }

        /// <summary>A line in the captain's log, in the game's own form: text, position, date.</summary>
        private static void WriteJournal(string text)
        {
            var data = DataPersistenceManager.dpInstance != null ? DataPersistenceManager.dpInstance.gameData : null;
            EnviroManager enviro = EnviroManager.instance;
            if (data == null || data.logs == null || enviro == null || enviro.Time == null) return;
            float lat = Mathf.Round(enviro.Time.Settings.latitude * 1000f) / 1000f;
            float lon = Mathf.Round(enviro.Time.Settings.longitude * 1000f) / 1000f;
            // The game writes a positive longitude as west; the log should read the same as its own lines.
            string position = (lat > 0f ? "N " : "S ") + Math.Abs(lat).ToString(CultureInfo.InvariantCulture) + "-" +
                              (lon > 0f ? "W " : "E ") + Math.Abs(lon).ToString(CultureInfo.InvariantCulture);
            string date = DateTime.Now.ToString("MMM d, hh:mm", CultureInfo.InvariantCulture);
            data.logs.Add(text + "=" + position + "=" + date);
        }

        // ---- co-op ----

        // The host's omens travel as one line of text:
        //   1;<wanted>,<summoned>,<seed>/... per omen in _omens order;<toast number>;<toast text>
        // sent when it changes and every ResendSeconds, so a guest who has just come in has it soon.

        private void SendToGuests()
        {
            var sb = new StringBuilder("1;");
            for (int i = 0; i < _omens.Length; i++)
            {
                Omen omen = _omens[i];
                if (i > 0) sb.Append('/');
                sb.Append(omen.Wanted ? '1' : '0').Append(',').Append(omen.Summoned ? '1' : '0').Append(',')
                  .Append(omen.Seed.ToString(CultureInfo.InvariantCulture));
            }
            sb.Append(';').Append(_toastId.ToString(CultureInfo.InvariantCulture)).Append(';').Append(_toastText);
            string text = sb.ToString();
            if (text == _sent && Time.unscaledTime - _sentAt < ResendSeconds) return;
            _sent = text;
            _sentAt = Time.unscaledTime;
            _coop.Send(text);
        }

        /// <summary>A guest: the host's omens. Called by the co-op mod from its own <c>Update</c>.</summary>
        private void OnHostText(string text)
        {
            if (_omens == null || _coop.Role() != CoopLink.Guest) return;
            string[] parts = text.Split(new[] { ';' }, 4);
            if (parts.Length < 4 || parts[0] != "1") return;
            string[] omens = parts[1].Split('/');
            for (int i = 0; i < _omens.Length && i < omens.Length; i++)
            {
                string[] f = omens[i].Split(',');
                if (f.Length < 3) continue;
                Omen omen = _omens[i];
                omen.Wanted = f[0] == "1";
                omen.Summoned = f[1] == "1";
                if (!omen.Active && int.TryParse(f[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int seed))
                    omen.Seed = seed;
            }
            // A line the host showed while this guest was here; not the one it finds on coming in.
            if (int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int toast))
            {
                if (_hostToast >= 0 && toast != _hostToast && parts[3].Length > 0) Toast(parts[3]);
                _hostToast = toast;
            }
        }

        // ---- on screen ----

        /// <summary>A line for this player and, on a host, for its guests too.</summary>
        private void Announce(string text)
        {
            Toast(text);
            _toastId++;
            _toastText = text;
        }

        private void Toast(string text)
        {
            if (!Plugin.Announce.Value) return;
            _toast = text;
            _toastUntil = Time.unscaledTime + 5f;
        }

        private void OnGUI()
        {
            float left = _toastUntil - Time.unscaledTime;
            if (left <= 0f || string.IsNullOrEmpty(_toast)) return;

            if (_style == null)
                _style = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Italic, alignment = TextAnchor.UpperCenter };
            float alpha = Mathf.Clamp01(left);
            var rect = new Rect(0f, Screen.height * 0.12f, Screen.width, 40f);
            var color = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.7f * alpha);
            GUI.Label(new Rect(rect.x + 1, rect.y + 1, rect.width, rect.height), _toast, _style);
            GUI.color = new Color(1f, 1f, 1f, alpha);
            GUI.Label(rect, _toast, _style);
            GUI.color = color;
        }

        private static Key ParseKey(string text, string setting)
        {
            if (Enum.TryParse(text, true, out Key key)) return key;
            Plugin.Log.LogWarning("Unknown key '" + text + "' in Keys." + setting + "; the key is off");
            return Key.None;
        }
    }
}

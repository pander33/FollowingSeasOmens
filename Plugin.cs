using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEngine;

namespace FollowingSeasOmens
{
    /// <summary>BepInEx entry point: reads the config and attaches the behaviour that does the work.</summary>
    [BepInPlugin(Guid, Name, Version)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.followingseas.omens";
        public const string Name = "Following Seas Omens";
        public const string Version = "0.1.0";

        internal static ManualLogSource Log;

        internal static ConfigEntry<float> BloodMoonChance;
        internal static ConfigEntry<float> AuroraChance;
        internal static ConfigEntry<float> GlowingSeaChance;
        internal static ConfigEntry<float> DarkNightChance;
        internal static ConfigEntry<float> FadeSeconds;
        internal static ConfigEntry<string> SummonKey;
        internal static ConfigEntry<string> NightKey;
        internal static ConfigEntry<bool> WriteJournal;
        internal static ConfigEntry<bool> Announce;

        private void Awake()
        {
            Log = Logger;

            BloodMoonChance = Config.Bind("Chance", "BloodMoon", 0.04f,
                "Chance that a night has a blood moon, from 0 to 1. Each night is rolled at dusk.");
            AuroraChance = Config.Bind("Chance", "Aurora", 0.06f,
                "Chance that a night has northern lights (a solar storm), from 0 to 1.");
            GlowingSeaChance = Config.Bind("Chance", "GlowingSea", 0.10f,
                "Chance that a night has glowing plankton in the sea, from 0 to 1.");
            DarkNightChance = Config.Bind("Chance", "DarkNight", 0.08f,
                "Chance that a night is pitch dark (no moon, no stars: only a lantern shows anything), from 0 to 1. " +
                "Never on the same night as a blood moon or northern lights.");
            FadeSeconds = Config.Bind("Look", "FadeSeconds", 20f,
                "How many seconds an omen takes to come and to go.");
            SummonKey = Config.Bind("Keys", "SummonKey", "F9",
                "Key that steps through the omens by hand: blood moon, northern lights, glowing sea, dark night, " +
                "all of them but the dark night, none (a UnityEngine.InputSystem.Key name; None switches it off). They show only at night.");
            NightKey = Config.Bind("Keys", "NightKey", "None",
                "For testing only: a key that moves the clock, by day to the middle of the night, by night to " +
                "noon (a UnityEngine.InputSystem.Key name, e.g. F10). Off by default: it changes the time of " +
                "the world. On the host only during a co-op session.");
            WriteJournal = Config.Bind("Journal", "WriteJournal", true,
                "Write a line in the captain's log when an omen comes by itself (not when it is summoned).");
            Announce = Config.Bind("UI", "Announce", true,
                "Show a line at the top of the screen when an omen comes.");

            var go = new GameObject("FollowingSeasOmens");
            DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideAndDontSave;
            go.AddComponent<OmenDirector>();

            Log.LogInfo(Name + " " + Version + " loaded");
        }
    }
}

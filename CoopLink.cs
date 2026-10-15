using System;
using System.Reflection;
using BepInEx.Bootstrap;

namespace FollowingSeasOmens
{
    /// <summary>
    /// What this mod knows of Following Seas Co-op, reached by reflection so that neither mod needs
    /// the other. A co-op build with <c>FollowingSeasCoop.Runtime.ModChannel</c> carries the host's
    /// omens to its guests; an older one is only asked whether a session is on, and omens stay off
    /// during it.
    /// </summary>
    internal sealed class CoopLink
    {
        public const int None = 0, Host = 1, Guest = 2;
        private const string CoopGuid = "com.followingseas.coop";
        private const string Channel = "com.followingseas.omens";

        private readonly Action<string> _onText;
        private Func<int> _role;
        private MethodInfo _send;
        private bool _resolved;

        /// <param name="onText">Where the host's text goes on a guest.</param>
        public CoopLink(Action<string> onText) => _onText = onText;

        /// <summary>The co-op mod can carry the omens (it has the mod channel).</summary>
        public bool CanShare { get; private set; }

        /// <summary><see cref="None"/>, <see cref="Host"/> or <see cref="Guest"/>. A co-op mod whose
        /// state cannot be read counts as a guest: omens then follow nobody and stay off.</summary>
        public int Role()
        {
            if (!_resolved)
            {
                _resolved = true;
                Resolve(_onText);
            }
            if (_role == null) return None;
            try { return _role(); }
            catch (Exception) { return Guest; }
        }

        public void Send(string text)
        {
            if (_send == null) return;
            try { _send.Invoke(null, new object[] { Channel, text }); }
            catch (Exception e) { Plugin.Log.LogWarning("Sending the omens to the co-op guests failed: " + e.Message); }
        }

        private void Resolve(Action<string> onText)
        {
            if (!Chainloader.PluginInfos.TryGetValue(CoopGuid, out var info) || info.Instance == null) return;
            Assembly coop = info.Instance.GetType().Assembly;
            try
            {
                Type channel = coop.GetType("FollowingSeasCoop.Runtime.ModChannel", false);
                if (channel != null)
                {
                    var role = channel.GetProperty("Role", BindingFlags.Public | BindingFlags.Static);
                    _send = channel.GetMethod("Send", BindingFlags.Public | BindingFlags.Static);
                    channel.GetMethod("Register", BindingFlags.Public | BindingFlags.Static)
                           .Invoke(null, new object[] { Channel, onText });
                    _role = () => (int)role.GetValue(null);
                    CanShare = true;
                    return;
                }

                var type = coop.GetType("FollowingSeasCoop.Runtime.CoopBehaviour", true);
                var instance = type.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
                var net = type.GetProperty("Net", BindingFlags.Public | BindingFlags.Instance);
                var netRole = net.PropertyType.GetProperty("Role", BindingFlags.Public | BindingFlags.Instance);
                _role = () =>
                {
                    object behaviour = instance.GetValue(null);
                    object n = behaviour != null ? net.GetValue(behaviour) : null;
                    return n != null ? Convert.ToInt32(netRole.GetValue(n)) : None;
                };
                Plugin.Log.LogInfo("This co-op build cannot carry omens; they stay off during a session");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("The co-op mod is installed but cannot be read (" + e.Message +
                                      "); omens stay off, in a session and out of one");
                _send = null;
                CanShare = false;
                _role = () => Guest;
            }
        }
    }
}

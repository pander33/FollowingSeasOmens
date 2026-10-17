using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using BepInEx.Bootstrap;
using UnityEngine;

namespace FollowingSeasOmens
{
	internal sealed class GameplayLink : IDisposable
	{
		private const string CoopGuid = "com.followingseas.coop";
		private const string Channel = "com.followingseas.omens.gameplay";
		private const string Schema = "followingseas-omens-gameplay-v1";
		private const float RetrySeconds = 0.5f;
		private const float TimeoutSeconds = 5f;

		/// <summary>How often the host looks at who is in the session.</summary>
		private const float RosterSeconds = 0.25f;

		/// <summary>The host's own player id in the co-op roster.</summary>
		private const uint HostId = 1;

		private readonly Action<string> _onHostSnapshot;
		private MethodInfo _send;
		private MethodInfo _register;
		private PropertyInfo _role;
		private string _session;
		private string _snapshot;
		private float _nextSend;
		private float _lastHostMessage;
		private float _lastSnapshot;
		private int _expectedGuests = -1;
		private int _currentRole;
		private bool _resolved;
		private bool _disposed;
		private bool _ready;
		private readonly Dictionary<uint, float> _acks = new Dictionary<uint, float>();
		private uint[] _members;
		private float _nextRoster;

		/// <summary>Since when the effects have been off in this session, and what the log last said:
		/// 1 on, -1 off, 0 nothing yet.</summary>
		private float _waitingSince;

		private int _reported;
		private object _identity;
		private PropertyInfo _sessionIdentity;
		private PropertyInfo _membersProperty;
		private PropertyInfo _catchUnits, _receiveCatchUnits;
		private string[] _chunks;
		private int _sequence, _incomingSequence = -1;
		public int Generation { get; private set; }

		public GameplayLink(Action<string> onHostSnapshot) => _onHostSnapshot = onHostSnapshot;

		public bool Ready => _ready && (ReadRole() != CoopLink.Guest ||
		                                _snapshot != null && Time.unscaledTime - _lastSnapshot <= TimeoutSeconds);

		public void Tick()
		{
			if (_disposed) return;
			Resolve();
			int role = ReadRole();
			object identity = _sessionIdentity?.GetValue(null);
			if (role != _currentRole || !ReferenceEquals(identity, _identity))
			{
				ClearSession();
				_currentRole = role;
				_identity = identity;
			}

			float now = Time.unscaledTime;
			if (role == CoopLink.None)
			{
				SetReady(false);
				return;
			}

			if (role == CoopLink.Host)
			{
				if (_session == null || now >= _nextRoster)
				{
					_nextRoster = now + RosterSeconds;
					var members = _membersProperty?.GetValue(null) as uint[];
					if (_session == null || !SameMembers(members, _members))
					{
						_members = members;
						_acks.Clear();
						Generation++;
						_expectedGuests = members == null ? -1 : members.Length - 1;
						_session = Guid.NewGuid().ToString("N");
						_snapshot = null;
						SetReady(false);
						_nextSend = 0f;
						_waitingSince = now;
						_reported = 0;
					}
				}

				bool ready = _expectedGuests >= 0 && AllConfirmed(now);
				SetReady(ready);
				if (ShouldReport(ready, now))
				{
					if (ready)
						Plugin.Log.LogInfo("Gameplay co-op effects are on: " + _expectedGuests +
						                   " guest(s) run a compatible Omens build.");
					else
						Plugin.Log.LogWarning(
							"Gameplay co-op effects are off until every player runs a compatible Omens and Co-op build; " +
							"only the omens' look is shared. Not confirmed: player " + Unconfirmed(now) + ".");
				}

				if (now >= _nextSend)
				{
					_nextSend = now + RetrySeconds;
					Send("H;" + Schema + ";" + _session + ";" + _expectedGuests + ";" + (_ready ? "1" : "0"));
					if (_ready && _snapshot != null) SendSnapshot();
				}

				return;
			}

			bool silent = _session == null || now - _lastHostMessage > TimeoutSeconds;
			if (_session != null && silent) SetReady(false);
			else if (now >= _nextSend && !string.IsNullOrEmpty(_session))
			{
				_nextSend = now + RetrySeconds;
				Send("A;" + Schema + ";" + _session);
			}

			bool guestReady = Ready;
			if (ShouldReport(guestReady, now))
			{
				if (guestReady)
					Plugin.Log.LogInfo("Gameplay co-op effects are on: the host's rules apply.");
				else
					Plugin.Log.LogWarning(silent
						? "Gameplay co-op effects are off: the host does not run a compatible Omens and Co-op build; " +
						  "only the omens' look is shared."
						: "Gameplay co-op effects are off: the host waits until every player runs a compatible " +
						  "Omens and Co-op build; only the omens' look is shared.");
			}
		}

		/// <summary>True once when the effects come on, and once when they have stayed off for a while,
		/// so the log says why without repeating itself every frame.</summary>
		private bool ShouldReport(bool ready, float now)
		{
			if (ready)
			{
				if (_reported == 1) return false;
				_reported = 1;
				return true;
			}

			if (_reported == 1)
			{
				_reported = 0;
				_waitingSince = now;
			}

			if (_reported != 0 || now - _waitingSince <= TimeoutSeconds) return false;
			_reported = -1;
			return true;
		}

		private bool AllConfirmed(float now)
		{
			foreach (uint member in _members)
				if (member != HostId && (!_acks.TryGetValue(member, out float seen) || now - seen > TimeoutSeconds))
					return false;
			return true;
		}

		/// <summary>The guests that have not answered lately, for the log.</summary>
		private string Unconfirmed(float now)
		{
			if (_members == null) return "?";
			string missing = null;
			foreach (uint member in _members)
				if (member != HostId && (!_acks.TryGetValue(member, out float seen) || now - seen > TimeoutSeconds))
				{
					string id = member.ToString(CultureInfo.InvariantCulture);
					missing = missing == null ? id : missing + ", " + id;
				}

			return missing;
		}

		private static bool SameMembers(uint[] a, uint[] b)
		{
			if (ReferenceEquals(a, b)) return true;
			if (a == null || b == null || a.Length != b.Length) return false;
			for (int i = 0; i < a.Length; i++)
				if (a[i] != b[i])
					return false;
			return true;
		}

		public void Publish(string payload)
		{
			if (_disposed || ReadRole() != CoopLink.Host || payload == null ||
			    payload.Length > 24000) return;
			_snapshot = payload;
			if (_ready) SendSnapshot();
		}

		public void Dispose()
		{
			if (_disposed) return;
			_disposed = true;
			_catchUnits?.SetValue(null, null);
			_receiveCatchUnits?.SetValue(null, null);
			if (_register != null)
			{
				try
				{
					_register.Invoke(null, new object[] { Channel, null });
				}
				catch (Exception exception)
				{
					Plugin.Log.LogWarning("Unregistering gameplay channel failed: " + exception.Message);
				}
			}

			SetReady(false);
			_send = null;
		}

		private void Resolve()
		{
			if (_resolved) return;
			_resolved = true;
			if (!Chainloader.PluginInfos.TryGetValue(CoopGuid, out var info) || info.Instance == null) return;

			try
			{
				Assembly assembly = info.Instance.GetType().Assembly;
				Type channel = assembly.GetType("FollowingSeasCoop.Runtime.ModChannel", false);
				if (channel == null) return;
				_role = channel.GetProperty("Role", BindingFlags.Public | BindingFlags.Static);
				_send = channel.GetMethod("Send", BindingFlags.Public | BindingFlags.Static);
				_register = channel.GetMethod("RegisterPeer", BindingFlags.Public | BindingFlags.Static);
				_sessionIdentity = channel.GetProperty("SessionIdentity");
				_membersProperty = channel.GetProperty("Members");
				_catchUnits = channel.GetProperty("CatchUnits");
				_receiveCatchUnits = channel.GetProperty("ReceiveCatchUnits");
				if (_role == null || _send == null || _register == null || _sessionIdentity == null ||
				    _membersProperty == null || _catchUnits == null || _receiveCatchUnits == null)
				{
					_role = null;
					_send = null;
					Plugin.Log.LogInfo(
						"Gameplay co-op effects are off: this Co-op build lacks peer-aware channels and catch rewards.");
					return;
				}

				_register.Invoke(null, new object[] { Channel, (Action<uint, string>)OnPeerText });
				_catchUnits?.SetValue(null, (Func<GameObject, int>)FishingEffects.CatchUnits);
				_receiveCatchUnits?.SetValue(null, (Action<GameObject, int>)FishingEffects.ReceiveCatchUnits);
			}
			catch (Exception e)
			{
				_send = null;
				_register = null;
				_role = null;
				Plugin.Log.LogWarning("Gameplay co-op effects are off: the Co-op channel could not be reached: " +
				                      e.Message);
			}
		}

		private int ReadRole()
		{
			if (_role == null) return CoopLink.None;
			try
			{
				return Convert.ToInt32(_role.GetValue(null, null));
			}
			catch (Exception)
			{
				return CoopLink.None;
			}
		}

		private void OnPeerText(uint sender, string text)
		{
			if (_disposed || string.IsNullOrEmpty(text) || text.Length > 24000) return;
			string[] fields = text.Split(new[] { ';' }, 5);
			int role = ReadRole();
			if (role == CoopLink.Host)
			{
				if (fields.Length == 3 && fields[0] == "A" && fields[1] == Schema && fields[2] == _session)
					_acks[sender] = Time.unscaledTime;
				return;
			}

			if (sender != 1) return;
			if (role != CoopLink.Guest) return;
			if (fields.Length == 5 && fields[0] == "H" && fields[1] == Schema &&
			    fields[2].Length == 32 && int.TryParse(fields[3], out int count) && count >= 0 &&
			    (fields[4] == "0" || fields[4] == "1"))
			{
				_lastHostMessage = Time.unscaledTime;
				if (_session != fields[2])
				{
					Generation++;
					_chunks = null;
					_incomingSequence = -1;
					_onHostSnapshot(string.Empty);
					_session = fields[2];
					_snapshot = null;
					_nextSend = 0f;
				}

				SetReady(fields[4] == "1");
				return;
			}

			string[] pieces = text.Split(new[] { ';' }, 6);
			if (pieces.Length != 6 || pieces[0] != "S" || pieces[1] != _session || !_ready ||
			    !int.TryParse(pieces[2], out int sequence) || !int.TryParse(pieces[3], out int index) ||
			    !int.TryParse(pieces[4], out int total) || total < 1 || total > 8 || index < 0 || index >= total ||
			    pieces[5].Length > 3500 || sequence < _incomingSequence) return;
			if (sequence != _incomingSequence)
			{
				_incomingSequence = sequence;
				_chunks = new string[total];
			}

			if (_chunks == null || _chunks.Length != total) return;
			_chunks[index] = pieces[5];
			foreach (string chunk in _chunks)
				if (chunk == null)
					return;
			_snapshot = string.Concat(_chunks);
			if (_snapshot.Length > 24000)
			{
				_chunks = null;
				_snapshot = null;
				_onHostSnapshot(string.Empty);
				return;
			}

			_lastSnapshot = Time.unscaledTime;
			_lastHostMessage = Time.unscaledTime;
			_onHostSnapshot(_snapshot);
			_chunks = null;
		}

		private void SendSnapshot()
		{
			string payload = _snapshot;
			int sequence = ++_sequence;
			int total = Math.Max(1, (payload.Length + 3499) / 3500);
			for (int i = 0; i < total; i++)
			{
				Send("S;" + _session + ";" + sequence + ";" + i + ";" + total + ";" +
				     payload.Substring(i * 3500, Math.Min(3500, payload.Length - i * 3500)));
				if (!_ready) return;
			}
		}

		private void Send(string text)
		{
			if (_send == null) return;
			try
			{
				if (!Equals(_send.Invoke(null, new object[] { Channel, text }), true)) SetReady(false);
			}
			catch (Exception)
			{
				SetReady(false);
			}
		}

		private void SetReady(bool ready)
		{
			if (_ready == ready) return;

			_ready = ready;
			if (!ready)
			{
				_snapshot = null;
				_chunks = null;
				_onHostSnapshot?.Invoke(string.Empty);
			}
		}

		private void ClearSession()
		{
			Generation++;
			_acks.Clear();
			_members = null;
			_nextRoster = 0f;
			_waitingSince = Time.unscaledTime;
			_reported = 0;
			_chunks = null;
			_incomingSequence = -1;
			_onHostSnapshot(string.Empty);
			_session = null;
			_snapshot = null;
			_expectedGuests = -1;
			_lastHostMessage = 0f;
			_nextSend = 0f;
			SetReady(false);
		}
	}
}

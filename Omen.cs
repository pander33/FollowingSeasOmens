using System;
using Enviro;
using UnityEngine;

namespace FollowingSeasOmens
{
    /// <summary>
    /// One kind of night event. <see cref="Level"/> fades towards 1 while it is wanted and back to 0
    /// after; the effect is drawn at <c>Level</c> times how dark it is. The game's values are taken at
    /// <see cref="Begin"/> and put back at <see cref="End"/>, which runs as soon as the level is 0
    /// again, when the world goes away, or when the omen fails.
    /// </summary>
    internal abstract class Omen
    {
        public abstract string Name { get; }
        /// <summary>The line on the screen when the omen comes by itself.</summary>
        public abstract string Announcement { get; }
        /// <summary>The line in the captain's log.</summary>
        public abstract string JournalText { get; }

        public bool Wanted;
        /// <summary>Switched by the summon key: comes and goes quickly. Cleared once it is gone.</summary>
        public bool Summoned;
        /// <summary>Picked when the omen is called; whatever is random in its look comes from it, so a
        /// co-op host and its guests see the same.</summary>
        public int Seed;
        public float Level { get; private set; }
        public bool Active { get; private set; }
        /// <summary>Threw once: stays off until the game is restarted.</summary>
        public bool Broken { get; private set; }

        public void Tick(float dt, float fadeSeconds)
        {
            float target = Wanted && !Broken ? 1f : 0f;
            Level = Mathf.MoveTowards(Level, target, dt / Mathf.Max(0.1f, fadeSeconds));
            if (Level <= 0f && !Wanted) Summoned = false;
        }

        /// <summary>Called from <c>LateUpdate</c>, after Enviro has written its values for the frame.</summary>
        public void Apply(EnviroManager enviro, float darkness)
        {
            try
            {
                if (Level <= 0f)
                {
                    if (Active) Finish();
                    return;
                }
                if (!Active)
                {
                    if (!Begin(enviro)) return;
                    Active = true;
                }
                Draw(enviro, Level * darkness);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning(Name + " failed and is off until the game restarts: " + e);
                Broken = true;
                Stop();
            }
        }

        /// <summary>Off at once, the game's values back.</summary>
        public void Stop()
        {
            Wanted = false;
            Summoned = false;
            Level = 0f;
            if (Active) Finish();
        }

        private void Finish()
        {
            Active = false;
            try { End(); }
            catch (Exception e) { Plugin.Log.LogWarning(Name + ": putting the game's values back failed: " + e); }
        }

        /// <summary>Take what will be changed; false when it cannot start yet.</summary>
        protected abstract bool Begin(EnviroManager enviro);
        /// <summary>Strength 0..1: the fade times the darkness.</summary>
        protected abstract void Draw(EnviroManager enviro, float strength);
        protected abstract void End();
    }

    /// <summary>
    /// Stands in for one of Enviro's gradients while an omen tints it: a gradient of one colour,
    /// the original's colour at the current time multiplied by the tint, eased in by the strength.
    /// The original object is put back at <see cref="End"/>.
    /// </summary>
    internal sealed class GradientTint
    {
        private readonly Func<Gradient> _get;
        private readonly Action<Gradient> _set;
        private readonly Gradient _mine = new Gradient();
        private readonly GradientColorKey[] _colors = new GradientColorKey[2];
        private readonly GradientAlphaKey[] _alphas = { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) };
        private Gradient _original;

        public GradientTint(Func<Gradient> get, Action<Gradient> set)
        {
            _get = get;
            _set = set;
        }

        public void Begin()
        {
            Gradient current = _get();
            if (current == null || current == _mine) return;
            _original = current;
            _set(_mine);
        }

        public void Update(float time, Color tint, float strength)
        {
            if (_original == null) return;
            Color c = _original.Evaluate(time);
            Color tinted = new Color(c.r * tint.r, c.g * tint.g, c.b * tint.b, c.a);
            Color v = Color.Lerp(c, tinted, strength);
            _colors[0] = new GradientColorKey(v, 0f);
            _colors[1] = new GradientColorKey(v, 1f);
            _mine.SetKeys(_colors, _alphas);
        }

        public void End()
        {
            if (_original != null && _get() == _mine) _set(_original);
            _original = null;
        }
    }
}

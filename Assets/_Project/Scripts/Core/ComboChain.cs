namespace LightMMORPG.Core
{
    public readonly struct ComboHit
    {
        /// <summary>0-based index in the chain (0..3).</summary>
        public readonly int Index;
        public readonly float Damage;
        /// <summary>True on the last hit of the chain.</summary>
        public readonly bool Knockback;

        public ComboHit(int index, float damage, bool knockback)
        {
            Index = index;
            Damage = damage;
            Knockback = knockback;
        }
    }

    /// <summary>
    /// 4-hit light combo. Each hit opens a window; attacking again within it advances the chain, otherwise it restarts.
    /// Time is any monotonic clock in seconds (server time or tick * tickDelta) so it is deterministic.
    /// </summary>
    public sealed class ComboChain
    {
        private readonly float[] _damage;
        private readonly float _window;
        private int _nextIndex;
        private double _lastHitTime = double.NegativeInfinity;

        public ComboChain() : this(CombatConstants.LightComboDamage, CombatConstants.ComboWindow) { }

        public ComboChain(float[] damagePerHit, float window)
        {
            _damage = (float[])damagePerHit.Clone();
            _window = window;
        }

        public int Length => _damage.Length;
        /// <summary>Index the next hit will have if it lands inside the window.</summary>
        public int NextIndex => _nextIndex;

        public bool IsWindowOpen(double now) => _nextIndex > 0 && now - _lastHitTime <= _window;

        public ComboHit RegisterHit(double now)
        {
            if (!IsWindowOpen(now))
                _nextIndex = 0;

            int index = _nextIndex;
            bool finisher = index == _damage.Length - 1;
            _lastHitTime = now;
            _nextIndex = finisher ? 0 : index + 1;
            return new ComboHit(index, _damage[index], finisher);
        }

        public void Reset()
        {
            _nextIndex = 0;
            _lastHitTime = double.NegativeInfinity;
        }
    }
}

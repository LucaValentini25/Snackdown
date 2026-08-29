namespace Snackdown.Input
{
    /// <summary>
    /// Which way a pair of on-screen direction buttons is asking to go.
    /// </summary>
    /// <remarks>
    /// <para><b>The most recent press wins.</b> Two thumbs on a phone hold both buttons far more
    /// easily than ten fingers on a keyboard do, and the obvious rule — cancel to zero when both are
    /// down — turns pressing right while still holding left into a dead stop. The player asked to go
    /// right; they get to go right, and releasing it hands them back to left rather than to nothing.
    /// </para>
    /// <para>Kept out of the platform gate on purpose. Everything that touches a screen or a device
    /// is compiled for phones only, but this is arithmetic, and arithmetic behind an
    /// <c>#if UNITY_ANDROID</c> is arithmetic no test on Luca's machine could ever run.</para>
    /// </remarks>
    public class TouchDirection
    {
        bool _left;
        bool _right;
        sbyte _latest;

        /// <summary>-1, 0 or 1: the same three values the simulation accepts.</summary>
        public sbyte Value
        {
            get
            {
                if (_left && _right) return _latest;
                if (_left) return -1;
                if (_right) return 1;
                return 0;
            }
        }

        public void Press(sbyte direction)
        {
            if (direction < 0) _left = true;
            else if (direction > 0) _right = true;
            else return;

            _latest = direction < 0 ? (sbyte)-1 : (sbyte)1;
        }

        public void Release(sbyte direction)
        {
            if (direction < 0) _left = false;
            else if (direction > 0) _right = false;
        }

        /// <summary>Forgets both buttons. For a panel being hidden with a thumb still down.</summary>
        public void ReleaseAll()
        {
            _left = false;
            _right = false;
        }
    }
}

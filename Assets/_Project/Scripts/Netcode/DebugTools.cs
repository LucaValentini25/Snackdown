namespace Snackdown.Netcode
{
    /// <summary>
    /// Whether the netcode's debug instrumentation runs at all.
    /// </summary>
    /// <remarks>
    /// <para>One constant, read by everything that costs something only a developer wants: the
    /// on-screen readout, the run recorder that writes a CSV per session, and the ghost that draws
    /// where the server thinks a character is. Spreading <c>#if</c> through those files instead
    /// would mean the policy lived in four places and could disagree with itself.</para>
    /// <para><c>static readonly</c> rather than <c>const</c>, which was tried first and reverted.
    /// A constant folds, and every <c>if (!DebugTools.Enabled) return;</c> then compiles to
    /// unreachable code and a warning — in the editor, where the value is true. Warnings that appear
    /// only in one configuration are how a build starts being read past. The runtime branch costs
    /// nothing measurable; what it prevents is the work behind it.</para>
    /// <para><b>The cost this exists to remove is measured.</b> The audit put the overlay's IMGUI
    /// pass at roughly 97% of all managed allocation on the host — 320–600 KB/s against about 12
    /// KB/s from the entire simulation path. It is the single most expensive thing in the project
    /// and it exists to demonstrate the project.</para>
    /// <para>Development builds keep it. That is where it earns its place: a build handed to
    /// somebody to try, with the correction count ticking up while the character keeps moving, is
    /// the whole thesis on screen. A player build is the one place it is only a cost.</para>
    /// </remarks>
    public static class DebugTools
    {
        /// <summary>True in the editor and in development builds, false in a shipped one.</summary>
        public static readonly bool Enabled =
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            true;
#else
            false;
#endif
    }
}

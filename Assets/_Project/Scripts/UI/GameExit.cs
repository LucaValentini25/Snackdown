using UnityEngine;

namespace Snackdown.UI
{
    /// <summary>
    /// Closes the game, from a build or from the editor.
    /// </summary>
    /// <remarks>
    /// <para><c>Application.Quit</c> does nothing at all in the editor, which is where every button
    /// in this project has been tested. A quit button that silently did nothing on the machine it
    /// was written on would have been reported as broken by the first person to run a build, and
    /// nowhere before that.</para>
    /// <para>One place rather than two, because the main menu and the escape menu both need it and
    /// the editor branch is exactly the kind of thing that gets copied once and updated once.</para>
    /// </remarks>
    public static class GameExit
    {
        public static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}

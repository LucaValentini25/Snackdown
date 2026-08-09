using UnityEngine;
using UnityEngine.InputSystem;

namespace Snackdown.UI
{
    /// <summary>Where a player's remaining life is drawn.</summary>
    public enum LifeBarPlacement
    {
        /// <summary>A bar floating above each character, in the world.</summary>
        OverTheCharacter = 0,

        /// <summary>A strip along the bottom of the screen, one entry per player.</summary>
        AlongTheBottom = 1
    }

    /// <summary>
    /// Which of the two life-bar presentations is currently on screen, and the key that swaps them.
    /// </summary>
    /// <remarks>
    /// <para>Both presentations are built and both are shipped, because which one reads better is a
    /// judgement that cannot be made from a description — it has to be seen with four characters
    /// moving. The toggle exists so the comparison happens in one session instead of across two
    /// builds.</para>
    /// <para>Purely local and purely visual: nothing here is replicated, and nothing reads it except
    /// the two views. Two players in the same match can be looking at different presentations
    /// without any consequence, which is the test of whether this belongs on the wire at all.</para>
    /// </remarks>
    public class LifeBarStyle : MonoBehaviour
    {
        [Tooltip("Presentation used when the game starts.")]
        [SerializeField] LifeBarPlacement _placement = LifeBarPlacement.AlongTheBottom;

        [Tooltip("Key that swaps between the two, for comparing them live.")]
        [SerializeField] Key _toggleKey = Key.F3;

        /// <summary>The presentation every life-bar view should currently be drawing.</summary>
        /// <remarks>
        /// Defaults to the bottom strip so a scene with no <see cref="LifeBarStyle"/> in it still
        /// shows life somewhere, rather than showing nothing and looking broken.
        /// </remarks>
        public static LifeBarPlacement Placement { get; private set; } = LifeBarPlacement.AlongTheBottom;

        void OnEnable() => Placement = _placement;

        void Update()
        {
            if (Keyboard.current == null || !Keyboard.current[_toggleKey].wasPressedThisFrame) return;

            Placement = Placement == LifeBarPlacement.OverTheCharacter
                ? LifeBarPlacement.AlongTheBottom
                : LifeBarPlacement.OverTheCharacter;
        }
    }
}

using System.Collections.Generic;
using Snackdown.Connection;
using Snackdown.Gameplay.Match;
using Snackdown.Gameplay.Player;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Snackdown.UI
{
    /// <summary>
    /// The menu Escape opens over a running match, and the only way out of a build.
    /// </summary>
    /// <remarks>
    /// <para>It lives in the bootstrap scene beside the other overlays, because it has to survive
    /// the lobby being unloaded and the arena being loaded under it. Whether it is allowed on screen
    /// at any moment is <see cref="EscapeMenu"/>'s decision, asked once on the keypress and again
    /// every frame it stays up.</para>
    /// <para><b>Nothing is paused.</b> There is no pause in a networked match — the other players go
    /// on regardless — so the character stays under its owner's control while this is open. The
    /// alternative, taking input away, would leave somebody standing still and defenceless because
    /// they wanted to read a menu.</para>
    /// </remarks>
    public class EscapeMenuController : MonoBehaviour
    {
        [SerializeField] UIDocument _document;

        [Tooltip("Scenes this never unloads on the way out: the ones that were there before a match.")]
        [SerializeField] string[] _permanentScenes = { "Bootstrap", "Lobby" };

        VisualElement _root;
        Button _resume;
        Button _leave;
        Button _quit;
        Label _detail;

        InputAction _toggle;
        bool _open;
        bool _leaving;

        void Awake()
        {
            // Built here rather than read from the actions asset, which is how InputReader does it:
            // one place to look for what a key does, and no asset to keep in step with the code.
            _toggle = new InputAction("ToggleEscapeMenu", InputActionType.Button);
            _toggle.AddBinding("<Keyboard>/escape");
            _toggle.AddBinding("<Gamepad>/start");
        }

        void OnEnable()
        {
            _root = _document.rootVisualElement.Q<VisualElement>("escape-root");
            _resume = _root.Q<Button>("resume-button");
            _leave = _root.Q<Button>("leave-match-button");
            _quit = _root.Q<Button>("quit-button");
            _detail = _root.Q<Label>("escape-detail");

            _resume.clicked += Close;
            _leave.clicked += OnLeaveClicked;
            _quit.clicked += GameExit.Quit;

            _toggle.performed += OnTogglePressed;
            _toggle.Enable();

            Apply(false);
        }

        void OnDisable()
        {
            _toggle.Disable();
            _toggle.performed -= OnTogglePressed;

            _resume.clicked -= Close;
            _leave.clicked -= OnLeaveClicked;
            _quit.clicked -= GameExit.Quit;
        }

        void OnDestroy() => _toggle?.Dispose();

        /// <remarks>
        /// Asked every frame rather than only on the keypress. A match starting, a round ending or a
        /// host walking out can all happen while the menu is up, and the menu has to leave the
        /// screen when the thing it was opened over stops being there.
        /// </remarks>
        void Update()
        {
            if (_open && !MayBeOpen()) Close();
        }

        void OnTogglePressed(InputAction.CallbackContext _)
        {
            if (_open) { Close(); return; }
            if (!MayBeOpen()) return;

            _detail.text = IsHosting
                ? "The match keeps running. Leaving ends it for everybody."
                : "The match keeps running.";

            Apply(true);
        }

        static bool MayBeOpen()
        {
            MatchDirector director = MatchDirector.Current;
            bool inSession = SessionConnection.Current != null && SessionConnection.Current.InSession;

            return EscapeMenu.MayBeOpen(inSession, director != null ? director.Phase : MatchPhase.Lobby);
        }

        static bool IsHosting => SessionConnection.Current != null && SessionConnection.Current.IsHosting;

        void Close() => Apply(false);

        void Apply(bool open)
        {
            _open = open;
            _root.EnableInClassList("hidden", !open);
        }

        /// <summary>
        /// Ends this machine's part in the match and goes back to the menu.
        /// </summary>
        /// <remarks>
        /// <para>The same call the lobby's Leave button makes, so a host deletes the session and a
        /// client merely departs — the difference already lives in the provider and is not worth a
        /// second copy here.</para>
        /// <para>The arena has to be unloaded by hand afterwards. Nothing else will: the director
        /// that loaded it is a networked object, and by the time this returns it has been despawned
        /// along with the session, so the scene it opened would otherwise stay under the menu.</para>
        /// </remarks>
        async void OnLeaveClicked()
        {
            if (_leaving) return;
            _leaving = true;
            Close();

            List<string> arenas = LoadedArenas();

            SessionConnection session = SessionConnection.Current;
            if (session != null && session.Provider != null) await session.Provider.LeaveAsync();
            if (session != null) session.Forget();

            foreach (string arena in arenas)
            {
                Scene scene = SceneManager.GetSceneByName(arena);
                // Discarded on purpose: the unload finishes on its own and nothing here waits on
                // it. Without the discard the compiler reads an un-awaited call inside an async
                // method as a mistake, and this method is async for the leave above it.
                if (scene.isLoaded) _ = SceneManager.UnloadSceneAsync(scene);
            }

            _leaving = false;
        }

        /// <summary>Every loaded scene that a match put there, gathered before the session ends.</summary>
        List<string> LoadedArenas()
        {
            var names = new List<string>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (System.Array.IndexOf(_permanentScenes, scene.name) < 0) names.Add(scene.name);
            }

            return names;
        }
    }
}

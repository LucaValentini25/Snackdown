using Snackdown.Connection;
using Snackdown.Gameplay.Match;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UIElements;

namespace Snackdown.UI
{
    /// <summary>
    /// Shows the result over the finished arena, and gives the host the way back to the lobby.
    /// </summary>
    /// <remarks>
    /// <para>A reconciler, like <see cref="LoadingScreenController"/> and for the same reason: it
    /// asks every frame whether the match is over rather than waiting to be told it just ended. A
    /// listener would be wrong for a client that met the referee already decided — a late joiner,
    /// or anyone whose notification landed in the same frame as another.</para>
    /// <para>Only the host gets the button. Returning to the lobby moves the phase, and the phase is
    /// the server's to move; a client that could set it would end a match everyone else is still
    /// playing. Clients are told to wait rather than shown a button that does nothing, because a
    /// dead control is indistinguishable from a broken one.</para>
    /// </remarks>
    [RequireComponent(typeof(UIDocument))]
    public class EndScreenController : MonoBehaviour
    {
        UIDocument _document;
        VisualElement _root;
        Label _title;
        Label _detail;
        Label _hint;
        Button _returnButton;

        void Awake() => _document = GetComponent<UIDocument>();

        void OnEnable()
        {
            VisualElement root = _document.rootVisualElement;

            _root = root.Q<VisualElement>("end-root");
            _title = root.Q<Label>("end-title");
            _detail = root.Q<Label>("end-detail");
            _hint = root.Q<Label>("end-hint");
            _returnButton = root.Q<Button>("return-button");

            if (_returnButton != null) _returnButton.clicked += ReturnToLobby;
        }

        void OnDisable()
        {
            if (_returnButton != null) _returnButton.clicked -= ReturnToLobby;
        }

        void Update()
        {
            MatchDirector director = MatchDirector.Current;
            bool ended = director != null && director.Phase == MatchPhase.Ended;

            _root.EnableInClassList("hidden", !ended);
            if (!ended) return;

            RoundReferee referee = RoundReferee.Current;
            _title.text = TitleFor(referee);
            _detail.text = DetailFor(referee);

            bool isHost = NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;
            _returnButton.EnableInClassList("hidden", !isHost);
            _hint.text = isHost ? string.Empty : "Waiting for the host";
        }

        string TitleFor(RoundReferee referee)
        {
            if (referee == null || !referee.HasWinner) return "Draw";

            bool wonIt = NetworkManager.Singleton != null
                         && NetworkManager.Singleton.LocalClientId == referee.WinnerClientId;

            return wonIt ? "You win" : $"{NameOf(referee.WinnerClientId)} wins";
        }

        static string DetailFor(RoundReferee referee)
        {
            if (referee == null) return string.Empty;

            switch (referee.Outcome)
            {
                case MatchOutcome.LastStanding: return "Last one standing";
                case MatchOutcome.TimeUp: return "Most life left when time ran out";
                case MatchOutcome.NoWinner: return "Nobody was left standing";
                default: return string.Empty;
            }
        }

        static string NameOf(ulong clientId)
        {
            SessionRoster roster = FindFirstObjectByType<SessionRoster>();
            if (roster == null) return $"Player {clientId}";

            for (int i = 0; i < roster.Count; i++)
                if (roster[i].ClientId == clientId)
                    return roster[i].Nickname.ToString();

            return $"Player {clientId}";
        }

        static void ReturnToLobby()
        {
            MatchDirector director = MatchDirector.Current;
            if (director != null && director.IsServer) director.ServerReturnToLobby();
        }
    }
}

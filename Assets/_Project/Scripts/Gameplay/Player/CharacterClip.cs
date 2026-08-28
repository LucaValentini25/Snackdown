namespace Snackdown.Gameplay.Player
{
    /// <summary>
    /// The five things a character can be seen doing.
    /// </summary>
    /// <remarks>
    /// <para>Deliberately fewer than the sprite pack ships. Double jump and wall jump have sheets in
    /// it and no equivalent in <see cref="Snackdown.Simulation.PlayerMotor"/>, and a clip that plays
    /// for a state the simulation cannot be in is a promise the game does not keep.</para>
    /// <para>This is a view of the simulation, not an input to it. Nothing in
    /// <see cref="Snackdown.Simulation.PlayerState"/> is added to serve this enum, which is what
    /// keeps animation off the wire entirely.</para>
    /// </remarks>
    public enum CharacterClip
    {
        Idle = 0,
        Run = 1,
        Jump = 2,
        Fall = 3,
        Hit = 4
    }
}

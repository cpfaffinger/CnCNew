namespace CnCNew.Game.Shell;

/// <summary>
/// What the in-game save and load popup (<see cref="Screens.LoadGameScreen"/> over a running match) needs to save: the description it offers and the way to turn the
/// running game into a file under a given title.
/// </summary>
/// <param name="DefaultTitle">The description the entry field starts with.</param>
/// <param name="Write">Makes the file of the running game, titled by the person's description (<c>LiveMatch.Save</c>).</param>
public sealed record InGameSave(string DefaultTitle, Func<string, byte[]> Write);

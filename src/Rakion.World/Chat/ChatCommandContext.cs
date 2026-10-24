using Rakion.World.Session;

namespace Rakion.World.Chat;

/// <summary>
/// What a chat command may do beyond replying: find an online character, disconnect a session and close the
/// sender's room. The packet handler provides these so commands do not depend on it.
/// </summary>
public sealed record ChatCommandContext(
    WorldSession Sender,
    Func<string, WorldSession?> FindOnlineCharacter,
    Func<WorldSession, CancellationToken, ValueTask> DisconnectAsync,
    Func<WorldSession, CancellationToken, ValueTask<bool>> CloseRoomAsync);

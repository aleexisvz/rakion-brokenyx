namespace Rakion.World.Chat;

/// <summary>
/// Contract C-5: the channel and room chat ask the interceptor before broadcasting. A handled message is not
/// broadcast.
/// </summary>
public interface IChatCommandInterceptor
{
    /// <param name="text">The chat text without the client's "Name : " prefix.</param>
    ValueTask<bool> TryHandleAsync(ChatCommandContext context, string text, CancellationToken cancellationToken);
}

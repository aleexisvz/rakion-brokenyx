using System.Globalization;
using Microsoft.Extensions.Logging;
using Rakion.Core.Packets;
using Rakion.Database.Repositories;
using Rakion.World.Chat;
using Rakion.World.Session;

namespace Rakion.World.Features.Admin;

/// <summary>
/// GM chat commands (roadmap 7.3). The syntax is our own design: the retail client only has /notice (sent as
/// opcode 0x05) and the vote /kick. Every gold, cash and item change is transactional and logged.
/// </summary>
public sealed class GmChatCommands : IChatCommandInterceptor
{
    /// <summary>Help lines, each short enough for one notice (the client crashes on notices over 128 characters).</summary>
    public static readonly IReadOnlyList<string> Help =
    [
        "GM: /givegold <pj> <n>, /givecash <pj> <n>, /giveitem <pj> <id>, /present <pj> <id>",
        "GM: /ban <pj> <dias, 0 = siempre> [motivo], /unban <pj>",
        "GM: /kickout <pj>, /close, /notice <texto>, /rankupdate, /abusers",
    ];

    private const int SlotsPerBag = 30;
    private const int BoxSlots = 120;

    private readonly ISessionSender _sender;
    private readonly ICharacterRepository _characters;
    private readonly IUserRepository _users;
    private readonly IAccountBalanceRepository _balances;
    private readonly IAdminRepository _admin;
    private readonly IRankingTrigger? _ranking;
    private readonly IPresentRepository? _presents;
    private readonly IAbuserRepository? _abusers;
    private readonly ILogger<GmChatCommands> _logger;

    public GmChatCommands(
        ISessionSender sender,
        ICharacterRepository characters,
        IUserRepository users,
        IAccountBalanceRepository balances,
        IAdminRepository admin,
        ILogger<GmChatCommands> logger,
        IRankingTrigger? ranking = null,
        IPresentRepository? presents = null,
        IAbuserRepository? abusers = null)
    {
        _presents = presents;
        _abusers = abusers;
        _sender = sender;
        _characters = characters;
        _users = users;
        _balances = balances;
        _admin = admin;
        _ranking = ranking;
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(ChatCommandContext context, string text, CancellationToken cancellationToken)
    {
        if (!text.StartsWith('/') || !GmPolicy.IsGm(context.Sender.User))
        {
            return false;
        }

        string[] parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string reply = parts[0].ToLowerInvariant() switch
        {
            "/gm" or "/gmhelp" => string.Join('\n', Help),
            "/givegold" => await GiveCurrencyAsync(context, parts, gold: true, cancellationToken).ConfigureAwait(false),
            "/givecash" => await GiveCurrencyAsync(context, parts, gold: false, cancellationToken).ConfigureAwait(false),
            "/giveitem" => await GiveItemAsync(context, parts, cancellationToken).ConfigureAwait(false),
            "/present" => await SendPresentAsync(parts, cancellationToken).ConfigureAwait(false),
            "/abusers" => await ListAbusersAsync(cancellationToken).ConfigureAwait(false),
            "/ban" => await BanAsync(context, parts, cancellationToken).ConfigureAwait(false),
            "/unban" => await UnbanAsync(parts, cancellationToken).ConfigureAwait(false),
            "/kickout" => await KickOutAsync(context, parts, cancellationToken).ConfigureAwait(false),
            "/rankupdate" => _ranking is null
                ? "El ranking no está disponible."
                : $"Ranking actualizado: {await _ranking.RunAsync(cancellationToken).ConfigureAwait(false)} personajes (se ve al volver a entrar).",
            "/close" => await context.CloseRoomAsync(context.Sender, cancellationToken).ConfigureAwait(false) ? "Sala cerrada." : "No estás en una sala.",
            _ => string.Empty,
        };
        if (reply.Length == 0)
        {
            return false;
        }

        _logger.LogInformation("GM '{Gm}' ran '{Command}': {Reply}", context.Sender.User!.Username, text, reply);
        foreach (string line in reply.Split('\n'))
        {
            await _sender.SendAsync(context.Sender, new SystemNoticePacket(line), cancellationToken).ConfigureAwait(false);
        }

        return true;
    }

    private async Task<string> GiveCurrencyAsync(ChatCommandContext context, string[] parts, bool gold, CancellationToken ct)
    {
        if (parts.Length < 3 || !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int amount) || amount <= 0)
        {
            return gold ? "Uso: /givegold <pj> <cantidad>" : "Uso: /givecash <pj> <cantidad>";
        }

        if (await _characters.GetCharacterByNameAsync(parts[1], ct).ConfigureAwait(false) is not { } character)
        {
            return $"No existe el personaje '{parts[1]}'.";
        }

        bool done = gold
            ? await _balances.AddGoldAsync(character.UserId, amount, ct).ConfigureAwait(false)
            : await _balances.AddCashAsync(character.UserId, amount, ct).ConfigureAwait(false);
        if (done && gold && context.FindOnlineCharacter(character.Name)?.GameInfo is { } info)
        {
            info.Gold += amount;
        }

        string currency = gold ? "oro" : "cash";
        return done ? $"{amount} de {currency} para {character.Name} (se ve al volver a entrar)." : "No se pudo dar.";
    }

    private async Task<string> GiveItemAsync(ChatCommandContext context, string[] parts, CancellationToken ct)
    {
        if (parts.Length < 3 || !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int itemId))
        {
            return "Uso: /giveitem <pj> <id del ítem>";
        }

        if (await _characters.GetCharacterByNameAsync(parts[1], ct).ConfigureAwait(false) is not { } character)
        {
            return $"No existe el personaje '{parts[1]}'.";
        }

        var gameInfo = context.FindOnlineCharacter(character.Name)?.GameInfo
            ?? await _users.GetGameInfoByUserIdAsync(character.UserId, ct).ConfigureAwait(false);
        int boxSlots = Math.Min(Math.Max((int)(gameInfo?.BagCount ?? 1), 1) * SlotsPerBag, BoxSlots);
        byte? slot = await _admin.GiveItemAsync(character.UserId, itemId, boxSlots, 0, ct).ConfigureAwait(false);
        return slot is { } s
            ? $"Ítem {itemId} en el slot {s} del box de {character.Name} (se ve al volver a entrar)."
            : $"No se pudo: el ítem {itemId} no existe o el box está lleno.";
    }

    /// <summary>Queues a present in the character's account present box; the player takes it from the client's present window.</summary>
    private async Task<string> SendPresentAsync(string[] parts, CancellationToken ct)
    {
        if (parts.Length < 3 || !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int itemId))
        {
            return "Uso: /present <pj> <id del ítem>";
        }

        if (_presents is null)
        {
            return "Los regalos no están disponibles.";
        }

        if (await _characters.GetCharacterByNameAsync(parts[1], ct).ConfigureAwait(false) is not { } character)
        {
            return $"No existe el personaje '{parts[1]}'.";
        }

        int pendingId = await _presents.SendAsync(character.UserId, itemId, ct).ConfigureAwait(false);
        _logger.LogInformation("GM present {PendingId}: item {ItemId} to '{Character}'.", pendingId, itemId, character.Name);
        return $"Regalo {itemId} enviado a {character.Name}.";
    }

    /// <summary>The latest characters flagged by the daily abuser check, one short line each (notices over 128 characters crash the client).</summary>
    private async Task<string> ListAbusersAsync(CancellationToken ct)
    {
        if (_abusers is null)
        {
            return "El control de abusos no está disponible.";
        }

        var flags = await _abusers.GetRecentFlagsAsync(5, ct).ConfigureAwait(false);
        return flags.Count == 0
            ? "Ningún jugador marcado."
            : string.Join('\n', flags.Select(f => $"{f.CheckedAt:dd/MM HH:mm} {f.CharacterName}: W{f.Wins} exp {f.Exp} oro {f.Gold}"));
    }

    private async Task<string> BanAsync(ChatCommandContext context, string[] parts, CancellationToken ct)
    {
        if (parts.Length < 3 || !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int days) || days < 0)
        {
            return "Uso: /ban <pj> <días, 0 = permanente> [motivo]";
        }

        if (await _characters.GetCharacterByNameAsync(parts[1], ct).ConfigureAwait(false) is not { } character)
        {
            return $"No existe el personaje '{parts[1]}'.";
        }

        string reason = parts.Length > 3 ? string.Join(" ", parts[3..]) : "GM";
        DateTime? until = days == 0 ? null : DateTime.UtcNow.AddDays(days);
        if (!await _admin.BanAsync(character.UserId, until, reason, ct).ConfigureAwait(false))
        {
            return "No se pudo banear.";
        }

        if (context.FindOnlineCharacter(character.Name) is { } online)
        {
            await context.DisconnectAsync(online, ct).ConfigureAwait(false);
        }

        return days == 0 ? $"{character.Name} baneado para siempre." : $"{character.Name} baneado {days} días.";
    }

    private async Task<string> UnbanAsync(string[] parts, CancellationToken ct)
    {
        if (parts.Length < 2)
        {
            return "Uso: /unban <pj>";
        }

        if (await _characters.GetCharacterByNameAsync(parts[1], ct).ConfigureAwait(false) is not { } character)
        {
            return $"No existe el personaje '{parts[1]}'.";
        }

        return await _admin.UnbanAsync(character.UserId, ct).ConfigureAwait(false) ? $"{character.Name} desbaneado." : "No se pudo desbanear.";
    }

    private static async Task<string> KickOutAsync(ChatCommandContext context, string[] parts, CancellationToken ct)
    {
        if (parts.Length < 2)
        {
            return "Uso: /kickout <pj>";
        }

        if (context.FindOnlineCharacter(parts[1]) is not { } online)
        {
            return $"'{parts[1]}' no está conectado.";
        }

        await context.DisconnectAsync(online, ct).ConfigureAwait(false);
        return $"{parts[1]} desconectado.";
    }
}

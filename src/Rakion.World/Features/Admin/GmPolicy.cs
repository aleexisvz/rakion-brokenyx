using Rakion.Database.Entities;

namespace Rakion.World.Features.Admin;

/// <summary>
/// Who counts as a GM. The legacy server checks an account state of 0x34 (RakionWorldServ.exe 0x41F2D5, Observed);
/// here an account with authority 100 or more is a GM, which <c>--create-user --admin</c> creates (Inferred
/// equivalent, our own policy).
/// </summary>
public static class GmPolicy
{
    public const int GmAuthority = 100;

    public static bool IsGm(UserEntity? user) => user is not null && user.Authority >= GmAuthority;
}

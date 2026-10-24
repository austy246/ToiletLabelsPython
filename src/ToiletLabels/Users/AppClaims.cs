namespace ToiletLabels.Users;

public static class AppClaims
{
    /// <summary>Claim "is_admin" == "true" označuje administrátora (smí nahrávat a upravovat štítky).</summary>
    public const string IsAdmin = "is_admin";
}

namespace HorseRacingPrediction.Contracts;

public static class CollectionDefinitionRevisions
{
    // Preserve cancelled/excluded card entries and refresh participation status on existing cards.
    public const int RaceDetail = 5;
    public const int HorseProfile = 4;
    public const int JockeyProfile = 3;
    public const int TrainerProfile = 3;
    public const int OwnerIdentity = 1;

    public static int Subject(string definition) => definition switch
    {
        "horse-profile" => HorseProfile,
        "jockey-profile" => JockeyProfile,
        "trainer-profile" => TrainerProfile,
        "owner-identity" => OwnerIdentity,
        _ => throw new ArgumentException("Unknown subject definition.", nameof(definition))
    };
}

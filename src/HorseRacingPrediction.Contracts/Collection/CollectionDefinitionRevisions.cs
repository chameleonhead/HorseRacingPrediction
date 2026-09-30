
namespace HorseRacingPrediction.Contracts.Collection;

public static class CollectionDefinitionRevisions
{
    // Re-extract DOM names and retry profiles with corrected official navigation/identity rules.
    public const int RaceDetail = 6;
    public const int HorseProfile = 5;
    public const int JockeyProfile = 4;
    public const int TrainerProfile = 4;
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

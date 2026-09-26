using HorseRacingPrediction.Contracts;

namespace HorseRacingPrediction.CollectionOperations.CollectionPlatform;

public sealed record SubjectCollectionDefinition(ResourceType ResourceType, CollectionDefinitionId Definition,
    string SubjectType, string IdPrefix, bool PersistProfile, int CurrentRevision, string Name, string RevisionDescription);

public static class SubjectCollectionDefinitions
{
    public static IReadOnlyList<SubjectCollectionDefinition> All { get; } =
    [
        new(ResourceType.Horse, new("horse-profile"), "Horse", "horse", true, CollectionDefinitionRevisions.HorseProfile,
            "Horse profile", "Wait for the semantic profile view and isolate structural failures"),
        new(ResourceType.Jockey, new("jockey-profile"), "Jockey", "jockey", true, CollectionDefinitionRevisions.JockeyProfile,
            "Jockey profile", "Wait for the semantic profile view and isolate structural failures"),
        new(ResourceType.Trainer, new("trainer-profile"), "Trainer", "trainer", true, CollectionDefinitionRevisions.TrainerProfile,
            "Trainer profile", "Wait for the semantic profile view and isolate structural failures"),
        new(ResourceType.Owner, new("owner-identity"), "Owner", "owner", false, CollectionDefinitionRevisions.OwnerIdentity,
            "Owner identity", "Initial"),
    ];

    public static SubjectCollectionDefinition For(ResourceType type) => All.Single(x => x.ResourceType == type);

    public static async Task RegisterAsync(CollectionPlatformStore store, CancellationToken token = default)
    {
        foreach (var definition in All)
            await store.RegisterDefinitionAsync(definition.Definition, definition.Name, definition.ResourceType,
                definition.CurrentRevision, definition.RevisionDescription, definition.PersistProfile, token);
    }
}

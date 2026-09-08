using EventFlow.Aggregates;
namespace HorseRacingPrediction.Domain.Trainers;

public partial class TrainerAggregate : IEmit<TrainerJraProfileCollected>
{
    public void CollectJraProfile(CollectedSubjectProfile profile)
    {
        if (!_state.IsRegistered) throw new InvalidOperationException("対象が登録されていません。");
        UpdateProfile(displayName: profile.Name, affiliationCode: string.IsNullOrWhiteSpace(profile.Fields.GetValueOrDefault("所属")) ? null : profile.Fields["所属"]);
        Emit(new TrainerJraProfileCollected(profile));
    }
    public void Apply(TrainerJraProfileCollected e) { }
}

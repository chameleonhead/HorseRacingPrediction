using EventFlow.Aggregates;
namespace HorseRacingPrediction.Domain.Horses;

public partial class HorseAggregate : IEmit<HorseJraProfileCollected>
{
    public void CollectJraProfile(CollectedSubjectProfile profile)
    {
        if (!_state.IsRegistered) throw new InvalidOperationException("対象が登録されていません。");
        UpdateProfile(registeredName: profile.Name, sexCode: profile.Fields.GetValueOrDefault("性別") switch
        { "牡" => "M", "牝" => "F", "せん" or "セン" => "G", _ => null },
        birthDate: DateOnly.TryParseExact(profile.Fields.GetValueOrDefault("生年月日"), "yyyy年M月d日", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var birth) ? birth : null,
        ownerName: string.IsNullOrWhiteSpace(profile.Fields.GetValueOrDefault("馬主名")) ? null : profile.Fields["馬主名"]);
        Emit(new HorseJraProfileCollected(profile));
    }
    public void Apply(HorseJraProfileCollected e) { }
}

using EventFlow.Aggregates;
using EventFlow.ReadStores;
using HorseRacingPrediction.Domain.Memos;

namespace HorseRacingPrediction.Application.Queries.ReadModels;

public class MemoBySubjectLocator : IReadModelLocator
{
    public IEnumerable<string> GetReadModelIds(IDomainEvent domainEvent)
    {
        switch (domainEvent)
        {
            case IDomainEvent<MemoAggregate, MemoId, MemoCreated> e:
                foreach (var s in e.AggregateEvent.Subjects)
                    yield return MakeKey(s.SubjectType, s.SubjectId);
                break;

            case IDomainEvent<MemoAggregate, MemoId, MemoUpdated> e:
                foreach (var s in e.AggregateEvent.CurrentSubjects)
                    yield return MakeKey(s.SubjectType, s.SubjectId);
                break;

            case IDomainEvent<MemoAggregate, MemoId, MemoDeleted> e:
                foreach (var s in e.AggregateEvent.Subjects)
                    yield return MakeKey(s.SubjectType, s.SubjectId);
                break;

            case IDomainEvent<MemoAggregate, MemoId, MemoSubjectsChanged> e:
                var allSubjects = e.AggregateEvent.AddedSubjects
                    .Concat(e.AggregateEvent.RemovedSubjects);
                foreach (var s in allSubjects)
                    yield return MakeKey(s.SubjectType, s.SubjectId);
                break;
        }
    }

    public static string MakeKey(MemoSubjectType subjectType, string subjectId)
        => $"{subjectType}:{subjectId}";
}

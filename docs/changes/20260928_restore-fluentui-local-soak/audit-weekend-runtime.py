import json
import pathlib
import re
import sqlite3
import sys
from urllib.parse import urlparse

root = pathlib.Path(sys.argv[1])

def open_readonly(name):
    path = root / name
    return sqlite3.connect(f"file:{path.as_posix()}?mode=ro", uri=True, timeout=15)

def tables(db):
    return {row[0] for row in db.execute("select name from sqlite_master where type='table'")}

def columns(db, table):
    return [row[1] for row in db.execute(f"pragma table_info('{table}')")]

def endpoint_class(url):
    if not url:
        return "not_recorded"
    parsed = urlparse(url)
    host = (parsed.hostname or "").lower()
    if host in {"jra.jp", "www.jra.jp", "jra.go.jp", "www.jra.go.jp"}:
        return "official_jra"
    if host in {"localhost", "127.0.0.1", "::1"}:
        return "local_api" if parsed.path.startswith("/api/") else "local_other"
    return "other_external"

try:
    with open_readonly("platform/collection-platform.db") as db:
        rows = db.execute("""
            select r.ResourceId, t.Status, count(*), sum(t.AttemptCount)
            from collection_tasks t join collection_resources r on r.ResourcePk=t.ResourcePk
            where t.DefinitionId='race-detail'
              and (r.ResourceId like '20260926:Nakayama:%' or r.ResourceId like '20260926:Hanshin:%'
                or r.ResourceId like '20260927:Nakayama:%' or r.ResourceId like '20260927:Hanshin:%')
            group by r.ResourceId,t.Status order by r.ResourceId,t.Status
        """).fetchall()
        target = set()
        for date in ("20260926", "20260927"):
            for course in ("Nakayama", "Hanshin"):
                for number in range(1, 13):
                    target.add(f"{date}:{course}:{number}")
        found = {row[0] for row in rows}
        status_counts = {}
        by_race = {race: {} for race in sorted(target)}
        for resource, status, count, attempts in rows:
            status_counts[status] = status_counts.get(status, 0) + count
            by_race.setdefault(resource, {})[status] = {"tasks": count, "attempts": attempts or 0}
        non_target_active = db.execute("""
            select count(*) from collection_tasks t join collection_resources r on r.ResourcePk=t.ResourcePk
            where t.Status in ('Ready','Running','RetryWaiting') and not (
              t.DefinitionId='race-detail' and (r.ResourceId like '20260926:Nakayama:%'
                or r.ResourceId like '20260926:Hanshin:%' or r.ResourceId like '20260927:Nakayama:%'
                or r.ResourceId like '20260927:Hanshin:%'))
        """).fetchone()[0]
        result = {
            "targetRaceCount": 48,
            "targetRaceTaskResources": len(found & target),
            "targetTaskStatusCounts": status_counts,
            "nonTargetActiveTasks": non_target_active,
            "targetByRace": by_race,
            "platformIntegrity": db.execute("pragma integrity_check").fetchone()[0],
            "undispatchedOutboxRows": db.execute(
                "select count(*) from collection_task_outbox where DispatchedAt is null").fetchone()[0],
        }
        target_parameters = sorted(target)
        placeholders = ",".join("?" for _ in target_parameters)
        request_window = db.execute(f"""
            select count(*),count(distinct r.ResourceId),min(q.RequestedAt),max(q.RequestedAt)
            from collection_requests q join collection_resources r on r.ResourcePk=q.ResourcePk
            where q.Reason='ManualRefresh' and q.DefinitionId='race-detail'
              and q.RequestedAt >= '2026-09-28 11:10:00'
              and r.ResourceId in ({placeholders})
        """, target_parameters).fetchone()
        result["targetManualRefreshRequestWindow"] = {
            "requestCount": request_window[0], "distinctRaceResources": request_window[1],
            "firstRequestedAt": request_window[2], "lastRequestedAt": request_window[3],
        }
        target_attempts = db.execute(f"""
            select a.Result,count(*),min(a.StartedAt),max(a.FinishedAt)
            from collection_attempts a join collection_tasks t on t.TaskId=a.TaskId
            join collection_resources r on r.ResourcePk=t.ResourcePk
            where t.DefinitionId='race-detail' and a.StartedAt >= '2026-09-28 11:10:00'
              and r.ResourceId in ({placeholders})
            group by a.Result order by a.Result
        """, target_parameters).fetchall()
        result["targetAttemptsSinceStart"] = [
            {"result": row[0], "count": row[1], "firstStartedAt": row[2], "lastFinishedAt": row[3]}
            for row in target_attempts
        ]
        failure_rows = db.execute("""
            select r.Type,a.Result,a.ErrorCode,a.HttpStatusCode,a.RequestedUrl,a.FinalUrl,count(*)
            from collection_attempts a join collection_tasks t on a.TaskId=t.TaskId
            join collection_resources r on t.ResourcePk=r.ResourcePk
            where a.Result not in ('Succeeded','ResourceNotYetAvailable')
            group by r.Type,a.Result,a.ErrorCode,a.HttpStatusCode,a.RequestedUrl,a.FinalUrl
        """).fetchall()
        result["sanitizedFailureCategories"] = [
            {"resourceType": row[0], "result": row[1], "errorCode": row[2],
             "httpStatus": row[3], "requestedEndpointClass": endpoint_class(row[4]),
             "finalEndpointClass": endpoint_class(row[5]), "count": row[6]}
            for row in failure_rows
        ]
    with open_readonly("eventstore.db") as db:
        names = tables(db)
        result["domainTables"] = {}
        for table in ("RaceSummaries", "RaceResults", "Horses", "EventEntity"):
            if table in names:
                result["domainTables"][table] = {
                    "rows": db.execute(f'select count(*) from "{table}"').fetchone()[0],
                    "columns": columns(db, table),
                }
        course_names = {"中山": "Nakayama", "阪神": "Hanshin"}
        expected = {(date, course, number) for date in ("2026-09-26", "2026-09-27")
                    for course in course_names for number in range(1, 13)}
        results = db.execute("""
            select RaceId,RaceDate,RacecourseCode,RaceNumber,Status,EntryCount,ResultDeclaredAt,
                   EntryResults,EntryIndexes,PayoutResult
            from RaceResults where RaceDate between '2026-09-26' and '2026-09-27'
        """).fetchall()
        summaries = db.execute("""
            select RaceId,RaceDate,RacecourseCode,RaceNumber,Status,EntryCount
            from RaceSummaries where RaceDate between '2026-09-26' and '2026-09-27'
        """).fetchall()
        summary_by_key = {(row[1], row[2], row[3]): row for row in summaries}
        result_keys = [(row[1], row[2], row[3]) for row in results]
        summary_keys = [(row[1], row[2], row[3]) for row in summaries]
        per_race = {}
        payouts_modeled = 0
        payout_anomalies = []
        for row in results:
            race_id, date, course_code, number, status, entry_count, declared_at, entries_json, indexes_json, payout_json = row
            key = (date, course_code, number)
            summary = summary_by_key.get(key)
            entries = json.loads(entries_json or "[]")
            indexes = json.loads(indexes_json or "[]")
            payouts = json.loads(payout_json or "{}")
            horse_ids = [item.get("horseId") for item in entries if isinstance(item, dict) and item.get("horseId")]
            finishes = [item.get("finishPosition") for item in entries if isinstance(item, dict) and item.get("finishPosition") is not None]
            tie_positions = sorted({position for position in finishes if finishes.count(position) > 1})
            entry_horse_numbers = [item.get("horseNumber") for item in entries if isinstance(item, dict)]
            entry_horse_ids = [item.get("horseId") for item in entries if isinstance(item, dict) and item.get("horseId")]
            dead_heat_marker_consistent = all(
                sum(bool(item.get("isDeadHeat")) for item in entries if isinstance(item, dict)
                    and item.get("finishPosition") == position) == 1
                for position in tie_positions
            ) and all(
                not item.get("isDeadHeat") or item.get("finishPosition") in tie_positions
                for item in entries if isinstance(item, dict)
            )
            payout_types = [name for name, values in payouts.items() if isinstance(values, list) and values]
            payouts_modeled += int(bool(payouts))
            course_label = course_names.get(course_code, "unknown")
            race_key = f"{date.replace('-', '')}:{course_label}:{number}"
            for category, values in payouts.items():
                if not isinstance(values, list):
                    continue
                for item in values:
                    if not isinstance(item, dict):
                        payout_anomalies.append({"race": race_key, "category": category, "issue": "entry_shape"})
                        continue
                    combination = item.get("combination")
                    amount = item.get("amount")
                    if not isinstance(combination, str) or not re.fullmatch(r"\d+(?:-\d+){0,2}", combination):
                        payout_anomalies.append({"race": race_key, "category": category, "issue": "invalid_combination"})
                    if not isinstance(amount, (int, float)) or amount <= 0:
                        payout_anomalies.append({"race": race_key, "category": category, "issue": "nonpositive_or_missing_amount"})
            per_race[race_key] = {
                "raceId": race_id,
                "resultStatus": status,
                "resultDeclared": bool(declared_at),
                "entryCount": entry_count,
                "summaryEntryCount": summary[5] if summary else None,
                "summaryResultEntryCountMatches": bool(summary and summary[5] == entry_count),
                "entryResultsCount": len(entries),
                "uniqueHorseIdentities": len(set(horse_ids)),
                "missingHorseIdentityCount": len(entries) - len(horse_ids),
                "entryIndexCount": len(indexes),
                "entryIndexHorseIdentityMatches": {item.get("horseId") for item in entries if isinstance(item, dict)}
                    == {item.get("horseId") for item in indexes if isinstance(item, dict)},
                "finishPositionCount": len(finishes),
                "deadHeatPositionValues": tie_positions,
                "deadHeatMarkerCount": sum(bool(item.get("isDeadHeat")) for item in entries if isinstance(item, dict)),
                "deadHeatMarkedHorseNumbers": [item.get("horseNumber") for item in entries
                                                if isinstance(item, dict) and item.get("isDeadHeat")],
                "deadHeatMarkerConsistentWithTiedPositions": dead_heat_marker_consistent,
                "duplicateHorseNumbers": len(entry_horse_numbers) - len(set(entry_horse_numbers)),
                "duplicateHorseIdentities": len(entry_horse_ids) - len(set(entry_horse_ids)),
                "finishStatusConsistent": all(
                    (item.get("finishPosition") is not None) == (not item.get("abnormalResultCode"))
                    for item in entries if isinstance(item, dict)
                ),
                "abnormalResultCodeCount": sum(bool(item.get("abnormalResultCode")) for item in entries if isinstance(item, dict)),
                "abnormalResultCodes": sorted({item.get("abnormalResultCode") for item in entries
                                                if isinstance(item, dict) and item.get("abnormalResultCode")}),
                "payoutCategoriesPresent": payout_types,
                "refundModeled": any("refund" in field.lower() for field in payouts),
            }
        result["domainReconciliation"] = {
            "expectedHeldRaces": len(expected),
            "raceSummaryRows": len(summaries),
            "raceResultRows": len(results),
            "summaryDuplicateNaturalKeys": len(summary_keys) - len(set(summary_keys)),
            "resultDuplicateNaturalKeys": len(result_keys) - len(set(result_keys)),
            "resultsWithoutSummary": len(set(result_keys) - set(summary_keys)),
            "summariesWithoutResult": len(set(summary_keys) - set(result_keys)),
            "missingResultRaces": [f"{date.replace('-', '')}:{course_names[course]}:{number}" for date, course, number in sorted(expected - set(result_keys))],
            "unexpectedResultRaces": [f"{date.replace('-', '')}:{course_names.get(course, 'unknown')}:{number}" for date, course, number in sorted(set(result_keys) - expected)],
            "resultEntryCountMatches": sum(item["entryCount"] == item["entryResultsCount"] == item["entryIndexCount"] for item in per_race.values()),
            "summaryResultEntryCountMatches": sum(item["summaryResultEntryCountMatches"] for item in per_race.values()),
            "resultEntryHorseIdentityComplete": sum(item["missingHorseIdentityCount"] == 0 and item["uniqueHorseIdentities"] == item["entryCount"] for item in per_race.values()),
            "entryIndexHorseIdentityConsistent": sum(item["entryIndexHorseIdentityMatches"] for item in per_race.values()),
            "deadHeatMarkerConsistentRaces": sum(item["deadHeatMarkerConsistentWithTiedPositions"] for item in per_race.values()),
            "entryHorseNumberUniqueRaces": sum(item["duplicateHorseNumbers"] == 0 for item in per_race.values()),
            "entryHorseIdentityUniqueRaces": sum(item["duplicateHorseIdentities"] == 0 for item in per_race.values()),
            "finishStatusConsistentRaces": sum(item["finishStatusConsistent"] for item in per_race.values()),
            "payoutResultsModeled": payouts_modeled,
            "modeledPayoutCategoryCoverage": {
                category: sum(category in item["payoutCategoriesPresent"] for item in per_race.values())
                for category in ("winPayouts", "placePayouts", "quinellaPayouts", "exactaPayouts", "trifectaPayouts")
            },
            "payoutAnomalyCount": len(payout_anomalies),
            "payoutAnomalies": payout_anomalies,
            "refundResultsModeled": False,
            "oddsModeledInRaceResultProjection": False,
            "perRace": per_race,
        }
        horse_ids = {row[0] for row in db.execute("select HorseId from Horses")}
        result_horse_ids = {horse_id for row in results for entry in json.loads(row[7] or "[]")
                            if isinstance(entry, dict) and (horse_id := entry.get("horseId"))}
        result["domainReconciliation"]["uniqueResultHorseIdentities"] = len(result_horse_ids)
        result["domainReconciliation"]["resultHorseIdsMissingHorseProjection"] = len(result_horse_ids - horse_ids)
        result["domainReconciliation"]["expectedOfficialHorseExceptions"] = [
            {"race": "20260926:Hanshin:5", "horseNumber": 7, "expectedCode": "除外"},
            {"race": "20260927:Nakayama:1", "horseNumber": 6, "expectedCode": "取消"},
            {"race": "20260927:Nakayama:8", "horseNumber": 12, "expectedCode": "中止"},
            {"race": "20260927:Nakayama:10", "horseNumber": 9, "expectedCode": None, "expectedParticipation": "finished", "expectedFinishPosition": 13},
            {"race": "20260927:Nakayama:11", "horseNumber": 4, "expectedCode": None, "expectedParticipation": "finished", "expectedFinishPosition": 8},
            {"race": "20260927:Hanshin:6", "horseNumber": 13, "expectedCode": "中止"},
        ]
        result["domainReconciliation"]["officialHorseExceptionAudit"] = []
        for date, course, number, horse_number, expected_code, expected_finish_position in (
            ("2026-09-26", "阪神", 5, 7, "除外", None),
            ("2026-09-27", "中山", 1, 6, "取消", None),
            ("2026-09-27", "中山", 8, 12, "中止", None),
            ("2026-09-27", "中山", 10, 9, None, 13),
            ("2026-09-27", "中山", 11, 4, None, 8),
            ("2026-09-27", "阪神", 6, 13, "中止", None),
        ):
            race = next((item for item in results if item[1] == date and item[2] == course and item[3] == number), None)
            entry = next((item for item in json.loads(race[7] or "[]") if isinstance(item, dict)
                          and item.get("horseNumber") == horse_number), None) if race else None
            observed = entry.get("abnormalResultCode") if entry else None
            expected_participation = "finished" if expected_code is None else "exception"
            finish_position = entry.get("finishPosition") if entry else None
            result["domainReconciliation"]["officialHorseExceptionAudit"].append({
                "race": f"{date.replace('-', '')}:{course_names[course]}:{number}",
                "horseNumber": horse_number, "expectedCode": expected_code,
                "expectedParticipation": expected_participation,
                "expectedFinishPosition": expected_finish_position,
                "observedCode": observed,
                "observedFinishPosition": finish_position,
                "observedParticipation": "finished" if entry and finish_position is not None else "exception" if entry else "missing",
                "matched": (observed == expected_code and bool(entry)
                            and (expected_participation != "finished" or finish_position == expected_finish_position)),
                "entryPresent": entry is not None,
            })
        event_aggregates = {row[0] for row in db.execute("select distinct AggregateId from EventEntity")}
        result_ids = {row[0] for row in results}
        result["domainReconciliation"]["resultAggregatesWithEvents"] = len(result_ids & event_aggregates)
        result["eventStoreIntegrity"] = db.execute("pragma integrity_check").fetchone()[0]
    with open_readonly("platform/local-collection-queue.db") as db:
        queue_tables = tables(db)
        queue_rows = db.execute('select count(*) from "local_collection_messages"').fetchone()[0] \
            if "local_collection_messages" in queue_tables else None
        result["localQueue"] = {
            "messageRows": queue_rows,
            "integrity": db.execute("pragma integrity_check").fetchone()[0],
        }
    print(json.dumps(result, sort_keys=True))
except Exception:
    print(json.dumps({"auditFailed": True, "category": "read_only_audit_failure"}))

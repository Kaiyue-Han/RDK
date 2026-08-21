"""Minimal policy tests for pilot_log_validator.py (standard library only)."""

from __future__ import annotations

import csv
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import pilot_log_validator as validator


def row(mark: str, evaluation_id: str = "R001-E001", **values: str) -> dict[str, str]:
    result = {field: "" for field in validator.EXPECTED_FIELDS}
    result.update(
        {
            "mark": mark,
            "participant_id": "P001",
            "session_id": "S001",
            "run_id": "R001",
            "trial_id": "R001-T001",
            "evaluation_id": evaluation_id,
            "trial_type": "NORMAL",
            "is_catch": "false",
            "catch_type": "NONE",
            "requested_theta_deg": "20",
            "event_start_time_sec": "100.000",
            "injection_start_time_sec": "100.200",
            "walking_speed_trigger_mps": "1.0",
            "walking_speed_injection_mps": "1.0",
        }
    )
    result.update(values)
    return result


def event_rows(terminal_mark: str, outcome: str, applied: str, terminal_time: str) -> list[dict[str, str]]:
    return [
        row(
            "STAIRCASE_EVAL_START",
            timeSec="100.000",
            testThetaDeg="20",
            currentStepDeg="2",
        ),
        row(
            "OCCLUSION_START",
            timeSec="100.000",
            extra="eventDurationSec=0.850;injectionWindowStart=100.191;injectionWindowEnd=100.659",
        ),
        row("INJECTION_START", timeSec="100.200", injectionOutcome="STARTED"),
        row(
            terminal_mark,
            timeSec=terminal_time,
            actual_applied_theta_deg=applied,
            signedInjectedThetaDeg="20",
            injectionOutcome=outcome,
        ),
    ]


def assert_no_fail(test: unittest.TestCase, result: validator.CheckResult) -> None:
    failures = [issue.reason for issue in result.issues if issue.status == "FAIL"]
    test.assertEqual([], failures, result.name)


def number_rows(rows: list[dict[str, str]]) -> list[dict[str, str]]:
    for row_number, source in enumerate(rows, start=2):
        source["_row_number"] = str(row_number)
    return rows


class InjectionValidityPolicyTests(unittest.TestCase):
    def test_minimal_csv_schema_round_trip(self) -> None:
        """The constructed rows use the real EventLogger CSV schema."""
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "events.csv"
            with path.open("w", encoding="utf-8", newline="") as handle:
                writer = csv.DictWriter(handle, fieldnames=validator.EXPECTED_FIELDS)
                writer.writeheader()
                writer.writerows(event_rows("INJECTION_COMPLETE", "COMPLETED", "20", "100.650"))
            header, rows, structural_errors = validator.read_csv(path)
        self.assertEqual("PASS", validator.check_schema(header, rows, structural_errors).status)

    def test_nominal_completion(self) -> None:
        rows = event_rows("INJECTION_COMPLETE", "COMPLETED", "20", "100.650")
        rows.append(row("STAIRCASE_EVAL_FINISH", timeSec="100.660", validTrial="true"))
        number_rows(rows)
        assert_no_fail(self, validator.check_requested_vs_applied(rows, 0.01))
        assert_no_fail(self, validator.check_injection_timing(rows, 0.01))

    def test_grace_completion(self) -> None:
        rows = event_rows("INJECTION_COMPLETE", "COMPLETED_IN_GRACE", "20", "100.750")
        rows.append(row("STAIRCASE_EVAL_FINISH", timeSec="100.760", validTrial="true"))
        number_rows(rows)
        assert_no_fail(self, validator.check_requested_vs_applied(rows, 0.01))
        assert_no_fail(self, validator.check_injection_timing(rows, 0.01))

    def test_completion_within_tolerance(self) -> None:
        rows = event_rows(
            "INJECTION_COMPLETE_WITHIN_TOLERANCE",
            "COMPLETED_WITHIN_TOLERANCE",
            "19.5",
            "100.850",
        )
        rows.append(row("STAIRCASE_EVAL_FINISH", timeSec="100.850", validTrial="true"))
        number_rows(rows)
        result = validator.check_requested_vs_applied(rows, 0.01)
        self.assertEqual("PASS", result.status)
        assert_no_fail(self, validator.check_injection_timing(rows, 0.01))

    def test_hard_deadline_incomplete_is_invalid_and_retried(self) -> None:
        rows = event_rows(
            "INJECTION_INCOMPLETE_HARD_DEADLINE",
            "INCOMPLETE_HARD_DEADLINE",
            "19.2",
            "100.850",
        )
        rows.append(
            row(
                "STAIRCASE_EVAL_FINISH",
                timeSec="100.850",
                validTrial="false",
                invalidReason="INJECTION_NOT_COMPLETED",
                currentStepDeg="2",
                staircaseDeltaDeg="0",
                testThetaDeg="20",
                nextThetaDeg="20",
                isReversal="false",
            )
        )
        rows.append(
            row(
                "STAIRCASE_EVAL_START",
                evaluation_id="R001-E002",
                timeSec="101.000",
                testThetaDeg="20",
                currentStepDeg="2",
            )
        )
        number_rows(rows)
        assert_no_fail(self, validator.check_hard_deadline_incomplete(rows, 0.01))
        assert_no_fail(self, validator.check_invalid_evaluations(rows, 0.01))


if __name__ == "__main__":
    unittest.main()

#!/usr/bin/env python3
"""Validate the formal RDW pilot events.csv using only Python's standard library.

The checks in this file are intentionally limited to facts represented by the
current EventLogger schema.  They do not infer unlogged Unity or Quest state.
"""

from __future__ import annotations

import argparse
import csv
import math
import re
import sys
from collections import Counter, defaultdict
from dataclasses import dataclass, field
from pathlib import Path
from typing import Iterable, Optional


EXPECTED_FIELDS = [
    "utc",
    "mark",
    "participant_id",
    "session_id",
    "run_id",
    "trial_id",
    "evaluation_id",
    "trial_type",
    "is_catch",
    "catch_type",
    "requested_theta_deg",
    "event_start_time_sec",
    "injection_start_time_sec",
    "response_deadline_time_sec",
    "response_time_sec",
    "response_rt_sec",
    "response_accepted",
    "walking_speed_trigger_mps",
    "walking_speed_injection_mps",
    "user_turn_congruency",
    "actual_applied_theta_deg",
    "sceneName",
    "conditionKey",
    "anchorMode",
    "motionMode",
    "occluderName",
    "occlusionRatio",
    "timeSec",
    "success",
    "testThetaDeg",
    "noticed",
    "validTrial",
    "invalidReason",
    "currentStepDeg",
    "staircaseDeltaDeg",
    "nextThetaDeg",
    "isReversal",
    "reversalIndex",
    "reversalCount",
    "estimatedThresholdDeg",
    "usedReversals",
    "allReversals",
    "baseYawRateAtInjection",
    "injectionSign",
    "signedInjectedThetaDeg",
    "injectionOutcome",
    "resetReason",
    "extra",
]

EVALUATION_MARKS = {
    "STAIRCASE_EVAL_START",
    "STAIRCASE_EVAL_FINISH",
    "CATCH_EVAL_START",
    "CATCH_EVAL_FINISH",
    "OCCLUSION_START",
    "OCCLUSION_END",
    "RESPONSE_WINDOW_OPEN",
    "RESPONSE_ACCEPTED",
    "RESPONSE_TIMEOUT",
    "STAIRCASE_REVERSAL",
    "STAIRCASE_RESULT",
}
EVALUATION_START_MARKS = {"STAIRCASE_EVAL_START", "CATCH_EVAL_START"}
EVALUATION_FINISH_MARKS = {"STAIRCASE_EVAL_FINISH", "CATCH_EVAL_FINISH"}
TERMINAL_MARKS = {"TRIAL_ABORT", "FULL_RESET", "TRIAL_RESET", "MANUAL_RESET"}
CLEANUP_MARKS = {
    "TRIAL_ABORT",
    "FULL_RESET",
    "TRIAL_RESET",
    "MANUAL_RESET",
    "INJECTION_CANCELLED",
    "RESET_TO_IDLE",
}

# Shared event-based injection policy implemented by RotationInjectionController.
# Keep this separate from --float-tolerance: 0.01 is useful for CSV rounding and
# cross-row float comparisons, but it is not the experimental completion rule.
FORMAL_EVENT_DURATION_SEC = 0.85
NOMINAL_INJECTION_START_OFFSET_SEC = 0.19125
NOMINAL_INJECTION_END_OFFSET_SEC = 0.65875
COMPLETION_TOLERANCE_DEG = 0.5

COMPLETION_MARKS = {
    "INJECTION_COMPLETE",
    "INJECTION_COMPLETE_WITHIN_TOLERANCE",
}
COMPLETION_OUTCOMES = {
    "COMPLETED",
    "COMPLETED_IN_GRACE",
    "COMPLETED_WITHIN_TOLERANCE",
}
HARD_DEADLINE_INCOMPLETE_MARK = "INJECTION_INCOMPLETE_HARD_DEADLINE"
HARD_DEADLINE_INCOMPLETE_OUTCOME = "INCOMPLETE_HARD_DEADLINE"

STATUS_ORDER = {"PASS": 0, "NOT CHECKABLE": 0, "WARN": 1, "FAIL": 2}


@dataclass
class Issue:
    status: str
    reason: str
    row_number: Optional[int] = None
    trial_id: str = ""
    evaluation_id: str = ""


@dataclass
class CheckResult:
    name: str
    pass_message: str = "check completed"
    issues: list[Issue] = field(default_factory=list)
    not_checkable_reason: str = ""

    def add(self, status: str, reason: str, row: Optional[dict[str, str]] = None) -> None:
        self.issues.append(
            Issue(
                status=status,
                reason=reason,
                row_number=int(row["_row_number"]) if row is not None else None,
                trial_id=clean(row.get("trial_id")) if row is not None else "",
                evaluation_id=clean(row.get("evaluation_id")) if row is not None else "",
            )
        )

    @property
    def status(self) -> str:
        if self.issues:
            return max(self.issues, key=lambda item: STATUS_ORDER[item.status]).status
        if self.not_checkable_reason:
            return "NOT CHECKABLE"
        return "PASS"


def clean(value: object) -> str:
    return "" if value is None else str(value).strip()


def parse_float(row: dict[str, str], field_name: str) -> tuple[Optional[float], Optional[str]]:
    raw = clean(row.get(field_name))
    if not raw:
        return None, None
    try:
        value = float(raw)
    except ValueError:
        return None, f"{field_name} is not numeric: {raw!r}"
    if not math.isfinite(value):
        return None, f"{field_name} is not finite: {raw!r}"
    return value, None


def parse_bool(row: dict[str, str], field_name: str) -> tuple[Optional[bool], Optional[str]]:
    raw = clean(row.get(field_name)).lower()
    if not raw:
        return None, None
    if raw == "true":
        return True, None
    if raw == "false":
        return False, None
    return None, f"{field_name} is not true/false: {raw!r}"


def parse_extra(row: dict[str, str]) -> dict[str, str]:
    values: dict[str, str] = {}
    for token in clean(row.get("extra")).split(";"):
        if "=" not in token:
            continue
        key, value = token.split("=", 1)
        key = key.strip()
        if key:
            values[key] = value.strip()
    return values


def mark(row: dict[str, str]) -> str:
    return clean(row.get("mark"))


def is_evaluation_mark(row: dict[str, str]) -> bool:
    value = mark(row)
    return value in EVALUATION_MARKS or value.startswith("INJECTION_")


def run_key(row: dict[str, str]) -> tuple[str, str, str]:
    return (
        clean(row.get("participant_id")),
        clean(row.get("session_id")),
        clean(row.get("run_id")),
    )


def evaluation_key(row: dict[str, str]) -> tuple[str, str, str, str]:
    return (*run_key(row), clean(row.get("evaluation_id")))


def almost_equal(left: float, right: float, tolerance: float) -> bool:
    return abs(left - right) <= tolerance


def first_number(
    rows: Iterable[dict[str, str]], field_name: str
) -> tuple[Optional[float], Optional[dict[str, str]], Optional[str]]:
    for row in rows:
        value, error = parse_float(row, field_name)
        if error:
            return None, row, error
        if value is not None:
            return value, row, None
    return None, None, None


def group_evaluations(rows: list[dict[str, str]]) -> dict[tuple[str, str, str, str], list[dict[str, str]]]:
    groups: dict[tuple[str, str, str, str], list[dict[str, str]]] = defaultdict(list)
    for row in rows:
        if clean(row.get("evaluation_id")):
            groups[evaluation_key(row)].append(row)
    return dict(groups)


def read_csv(path: Path) -> tuple[list[str], list[dict[str, str]], list[str]]:
    structural_errors: list[str] = []
    with path.open("r", encoding="utf-8-sig", newline="") as handle:
        reader = csv.DictReader(handle)
        header = list(reader.fieldnames or [])
        rows: list[dict[str, str]] = []
        for row_number, source in enumerate(reader, start=2):
            overflow = source.pop(None, None)
            if overflow:
                structural_errors.append(
                    f"row {row_number} has {len(overflow)} extra column(s)"
                )
            row = {key: clean(value) for key, value in source.items()}
            row["_row_number"] = str(row_number)
            rows.append(row)
    return header, rows, structural_errors


def check_schema(
    header: list[str], rows: list[dict[str, str]], structural_errors: list[str]
) -> CheckResult:
    result = CheckResult("CSV schema", f"all {len(EXPECTED_FIELDS)} current EventLogger fields are present")
    if not header:
        result.add("FAIL", "CSV has no header")
        return result

    duplicates = sorted(name for name, count in Counter(header).items() if count > 1)
    if duplicates:
        result.add("FAIL", f"duplicate header field(s): {', '.join(duplicates)}")

    missing = [name for name in EXPECTED_FIELDS if name not in header]
    if missing:
        result.add("FAIL", f"missing current EventLogger field(s): {', '.join(missing)}")

    unexpected = [name for name in header if name not in EXPECTED_FIELDS]
    if unexpected:
        result.add("WARN", f"unexpected field(s): {', '.join(unexpected)}")

    for error in structural_errors:
        result.add("FAIL", error)

    if not rows:
        result.add("WARN", "CSV contains a header but no data rows")
    return result


def check_identifiers(rows: list[dict[str, str]]) -> CheckResult:
    result = CheckResult("identifier integrity", "participant/session/run/trial/evaluation mappings are internally consistent")
    if not rows:
        result.not_checkable_reason = "no data rows"
        return result

    for row in rows:
        if not clean(row.get("participant_id")):
            result.add("FAIL", "participant_id is missing", row)
        if not clean(row.get("session_id")):
            result.add("FAIL", "session_id is missing", row)

        if is_evaluation_mark(row):
            for field_name in ("run_id", "trial_id", "evaluation_id"):
                if not clean(row.get(field_name)):
                    result.add("FAIL", f"{field_name} is missing on evaluation-scoped row", row)

        run_id = clean(row.get("run_id"))
        trial_id = clean(row.get("trial_id"))
        evaluation_id = clean(row.get("evaluation_id"))
        if run_id and not re.fullmatch(r"R\d+", run_id):
            result.add("WARN", f"run_id has unexpected format: {run_id}", row)
        if run_id and trial_id and not trial_id.startswith(f"{run_id}-T"):
            result.add("FAIL", f"trial_id {trial_id} does not belong to run_id {run_id}", row)
        if run_id and evaluation_id and not evaluation_id.startswith(f"{run_id}-E"):
            result.add("FAIL", f"evaluation_id {evaluation_id} does not belong to run_id {run_id}", row)

    eval_groups = group_evaluations(rows)
    stable_eval_fields = (
        "participant_id",
        "session_id",
        "run_id",
        "trial_id",
        "trial_type",
        "is_catch",
        "catch_type",
        "requested_theta_deg",
    )
    for grouped_rows in eval_groups.values():
        for field_name in stable_eval_fields:
            values = {clean(row.get(field_name)) for row in grouped_rows if clean(row.get(field_name))}
            if len(values) > 1:
                result.add(
                    "FAIL",
                    f"one evaluation_id maps to conflicting {field_name} values: {sorted(values)}",
                    grouped_rows[0],
                )

        starts = [row for row in grouped_rows if mark(row) in EVALUATION_START_MARKS]
        occlusion_starts = [row for row in grouped_rows if mark(row) == "OCCLUSION_START"]
        if len(starts) > 1:
            result.add("FAIL", "duplicate evaluation-start rows for one evaluation_id", starts[1])
        elif len(starts) == 0 and any(is_evaluation_mark(row) for row in grouped_rows):
            result.add("WARN", "evaluation has scoped rows but no evaluation-start row (possibly truncated CSV)", grouped_rows[0])
        if len(occlusion_starts) > 1:
            result.add("FAIL", "duplicate OCCLUSION_START rows for one evaluation_id", occlusion_starts[1])

    trial_groups: dict[tuple[str, str, str, str], list[dict[str, str]]] = defaultdict(list)
    for row in rows:
        trial_id = clean(row.get("trial_id"))
        if trial_id:
            trial_groups[(*run_key(row), trial_id)].append(row)
    for grouped_rows in trial_groups.values():
        for field_name in ("trial_type", "is_catch", "catch_type", "requested_theta_deg"):
            values = {clean(row.get(field_name)) for row in grouped_rows if clean(row.get(field_name))}
            if len(values) > 1:
                result.add(
                    "FAIL",
                    f"one trial_id maps to conflicting {field_name} values: {sorted(values)}",
                    grouped_rows[0],
                )
        valid_finishes = []
        for row in grouped_rows:
            valid, _ = parse_bool(row, "validTrial")
            if mark(row) in EVALUATION_FINISH_MARKS and valid is True:
                valid_finishes.append(row)
        if len(valid_finishes) > 1:
            result.add("FAIL", "one trial_id has more than one valid finish", valid_finishes[1])

    run_groups: dict[tuple[str, str, str], list[dict[str, str]]] = defaultdict(list)
    for row in rows:
        if clean(row.get("run_id")):
            run_groups[run_key(row)].append(row)
    for grouped_rows in run_groups.values():
        starts = [row for row in grouped_rows if mark(row) == "TRIAL_START"]
        if len(starts) > 1:
            result.add("FAIL", "duplicate TRIAL_START for the same participant/session/run_id", starts[1])
        conditions = {clean(row.get("conditionKey")) for row in grouped_rows if clean(row.get("conditionKey"))}
        if len(conditions) > 1:
            result.add("FAIL", f"run_id maps to multiple conditions: {sorted(conditions)}", grouped_rows[0])
    return result


def check_requested_vs_applied(
    rows: list[dict[str, str]], tolerance: float
) -> CheckResult:
    result = CheckResult(
        "requested theta vs applied theta",
        "valid injection outcomes satisfy the 0.5 deg completion-tolerance policy",
    )
    checked = 0
    for grouped_rows in group_evaluations(rows).values():
        complete_rows = [row for row in grouped_rows if mark(row) in COMPLETION_MARKS]
        valid_finish_rows = []
        for row in grouped_rows:
            valid, error = parse_bool(row, "validTrial")
            if error:
                result.add("FAIL", error, row)
            if mark(row) in EVALUATION_FINISH_MARKS and valid is True:
                valid_finish_rows.append(row)

        if len(complete_rows) > 1:
            result.add("FAIL", "duplicate injection-completion rows", complete_rows[1])

        if valid_finish_rows and not complete_rows:
            result.add("FAIL", "valid evaluation has no injection-completion row", valid_finish_rows[0])

        if not complete_rows:
            continue

        checked += 1
        row = complete_rows[0]
        requested, requested_row, error = first_number(grouped_rows, "requested_theta_deg")
        if error:
            result.add("FAIL", error, requested_row)
        applied, applied_error = parse_float(row, "actual_applied_theta_deg")
        signed_requested, signed_error = parse_float(row, "signedInjectedThetaDeg")
        for parse_error in (applied_error, signed_error):
            if parse_error:
                result.add("FAIL", parse_error, row)

        if requested is None:
            result.add("FAIL", "requested_theta_deg is missing for completed injection", row)
        if applied is None:
            result.add("FAIL", "actual_applied_theta_deg is missing on injection completion", row)

        outcome = clean(row.get("injectionOutcome"))
        if outcome not in COMPLETION_OUTCOMES:
            result.add("FAIL", f"completion row has unexpected injectionOutcome={outcome!r}", row)

        difference: Optional[float] = None
        if requested is not None and applied is not None:
            difference = abs(abs(applied) - abs(requested))
            if difference > COMPLETION_TOLERANCE_DEG:
                result.add(
                    "FAIL",
                    f"requested/applied difference is {difference:.3f} deg, above the {COMPLETION_TOLERANCE_DEG:.1f} deg completion tolerance",
                    row,
                )
            elif outcome in {"COMPLETED", "COMPLETED_IN_GRACE"} and difference > tolerance:
                result.add(
                    "WARN",
                    f"{outcome} differs from requested theta by {difference:.3f} deg; valid by 0.5 deg policy but unexpected for an exact completion",
                    row,
                )
        if requested is not None and signed_requested is not None and not almost_equal(abs(signed_requested), abs(requested), tolerance):
            result.add(
                "FAIL",
                f"requested theta {requested:.3f} deg but signedInjectedThetaDeg is {signed_requested:.3f} deg",
                row,
            )

    if checked == 0 and not result.issues:
        result.not_checkable_reason = "no completed injection was logged"
    return result


def check_injection_timing(
    rows: list[dict[str, str]], tolerance: float
) -> CheckResult:
    result = CheckResult(
        "injection timing",
        "logged injection timing matches the nominal window, grace interval, and 0.85 s hard deadline",
    )
    checked = 0
    for grouped_rows in group_evaluations(rows).values():
        injection_rows = [row for row in grouped_rows if mark(row) == "INJECTION_START"]
        if not injection_rows:
            continue
        checked += 1
        injection_row = injection_rows[0]
        event_start, source_row, error = first_number(grouped_rows, "event_start_time_sec")
        if error:
            result.add("FAIL", error, source_row)
        injection_start, source_row, error = first_number(grouped_rows, "injection_start_time_sec")
        if error:
            result.add("FAIL", error, source_row)

        event_duration: Optional[float] = None
        logged_window_start: Optional[float] = None
        logged_window_end: Optional[float] = None
        actual_event_end: Optional[float] = None
        occlusion_start_rows = [row for row in grouped_rows if mark(row) == "OCCLUSION_START"]
        if occlusion_start_rows:
            raw_duration = parse_extra(occlusion_start_rows[0]).get("eventDurationSec", "")
            if raw_duration:
                try:
                    event_duration = float(raw_duration)
                except ValueError:
                    result.add("FAIL", f"extra.eventDurationSec is not numeric: {raw_duration!r}", occlusion_start_rows[0])
            extras = parse_extra(occlusion_start_rows[0])
            for extra_name, label in (
                ("injectionWindowStart", "logged_window_start"),
                ("injectionWindowEnd", "logged_window_end"),
            ):
                raw_value = extras.get(extra_name, "")
                if not raw_value:
                    result.add("WARN", f"extra.{extra_name} is missing; nominal window cannot be fully checked", occlusion_start_rows[0])
                    continue
                try:
                    value = float(raw_value)
                except ValueError:
                    result.add("FAIL", f"extra.{extra_name} is not numeric: {raw_value!r}", occlusion_start_rows[0])
                    continue
                if label == "logged_window_start":
                    logged_window_start = value
                else:
                    logged_window_end = value
        end_rows = [row for row in grouped_rows if mark(row) == "OCCLUSION_END"]
        if end_rows:
            actual_event_end, end_error = parse_float(end_rows[-1], "timeSec")
            if end_error:
                result.add("FAIL", end_error, end_rows[-1])

        if event_duration is not None and not almost_equal(event_duration, FORMAL_EVENT_DURATION_SEC, tolerance):
            result.add("FAIL", f"formal eventDurationSec is {event_duration:.3f}, expected {FORMAL_EVENT_DURATION_SEC:.3f}", occlusion_start_rows[0])

        nominal_event_end = (
            event_start + event_duration
            if event_start is not None and event_duration is not None
            else actual_event_end
        )
        if event_start is None:
            result.add("FAIL", "event_start_time_sec is missing for started injection", injection_row)
        if injection_start is None:
            result.add("FAIL", "injection_start_time_sec is missing on INJECTION_START", injection_row)
        if nominal_event_end is None:
            result.add("WARN", "event end is unavailable; cannot bound injection against event interval", injection_row)
            continue
        expected_window_start = (
            event_start + NOMINAL_INJECTION_START_OFFSET_SEC
            if event_start is not None
            else None
        )
        expected_window_end = (
            event_start + NOMINAL_INJECTION_END_OFFSET_SEC
            if event_start is not None
            else None
        )
        if logged_window_start is not None and expected_window_start is not None and not almost_equal(logged_window_start, expected_window_start, tolerance):
            result.add("FAIL", f"logged nominal start {logged_window_start:.3f} != expected {expected_window_start:.3f}", occlusion_start_rows[0])
        if logged_window_end is not None and expected_window_end is not None and not almost_equal(logged_window_end, expected_window_end, tolerance):
            result.add("FAIL", f"logged nominal end {logged_window_end:.3f} != expected {expected_window_end:.3f}", occlusion_start_rows[0])
        window_start = logged_window_start if logged_window_start is not None else expected_window_start
        window_end = logged_window_end if logged_window_end is not None else expected_window_end
        if injection_start is not None and window_start is not None and injection_start < window_start - tolerance:
            result.add("FAIL", "injection started before the nominal injection window", injection_row)
        if injection_start is not None and injection_start > nominal_event_end + tolerance:
            result.add("FAIL", "injection started after the event interval ended", injection_row)

        complete_rows = [row for row in grouped_rows if mark(row) in COMPLETION_MARKS]
        if complete_rows:
            completion_time, completion_error = parse_float(complete_rows[-1], "timeSec")
            if completion_error:
                result.add("FAIL", completion_error, complete_rows[-1])
            elif completion_time is None:
                result.add("FAIL", "timeSec is missing on injection-completion row", complete_rows[-1])
            else:
                if injection_start is not None and completion_time < injection_start - tolerance:
                    result.add("FAIL", "injection completed before actual injection start", complete_rows[-1])
                outcome = clean(complete_rows[-1].get("injectionOutcome"))
                if outcome == "COMPLETED" and window_end is not None and completion_time > window_end + tolerance:
                    result.add(
                        "FAIL",
                        f"COMPLETED was logged at {completion_time:.3f}, after nominal window end {window_end:.3f}; expected grace outcome",
                        complete_rows[-1],
                    )
                if outcome == "COMPLETED_IN_GRACE" and window_end is not None and completion_time < window_end - tolerance:
                    result.add(
                        "FAIL",
                        f"COMPLETED_IN_GRACE was logged at {completion_time:.3f}, before nominal window end {window_end:.3f}",
                        complete_rows[-1],
                    )
                if completion_time > nominal_event_end + tolerance:
                    result.add(
                        "WARN",
                        f"completion callback was logged at {completion_time:.3f}, after hard deadline {nominal_event_end:.3f}; CSV has no per-frame rotation trace to prove a post-deadline delta",
                        complete_rows[-1],
                    )

    if checked == 0 and not result.issues:
        result.not_checkable_reason = "no INJECTION_START row was logged"
    return result


def check_hard_deadline_incomplete(
    rows: list[dict[str, str]], tolerance: float
) -> CheckResult:
    result = CheckResult(
        "hard-deadline incomplete injection",
        "incomplete hard-deadline evaluations are invalid and retain the same planned theta for retry",
    )
    checked = 0
    for grouped_rows in group_evaluations(rows).values():
        incomplete_rows = [
            row for row in grouped_rows
            if mark(row) == HARD_DEADLINE_INCOMPLETE_MARK
            or clean(row.get("injectionOutcome")) == HARD_DEADLINE_INCOMPLETE_OUTCOME
        ]
        if not incomplete_rows:
            continue
        checked += 1
        if len(incomplete_rows) > 1:
            result.add("FAIL", "duplicate INCOMPLETE_HARD_DEADLINE terminal rows", incomplete_rows[1])
        terminal = incomplete_rows[0]
        if clean(terminal.get("injectionOutcome")) != HARD_DEADLINE_INCOMPLETE_OUTCOME:
            result.add("FAIL", "hard-deadline incomplete mark lacks injectionOutcome=INCOMPLETE_HARD_DEADLINE", terminal)
        if any(mark(row) in COMPLETION_MARKS for row in grouped_rows):
            result.add("FAIL", "hard-deadline incomplete evaluation also logged a completion row", terminal)

        requested, requested_row, requested_error = first_number(grouped_rows, "requested_theta_deg")
        applied, applied_error = parse_float(terminal, "actual_applied_theta_deg")
        for error, source in ((requested_error, requested_row), (applied_error, terminal)):
            if error:
                result.add("FAIL", error, source)
        if requested is None or applied is None:
            result.add("FAIL", "hard-deadline incomplete row lacks requested or actual applied theta", terminal)
        elif abs(abs(requested) - abs(applied)) <= COMPLETION_TOLERANCE_DEG:
            result.add(
                "FAIL",
                f"INCOMPLETE_HARD_DEADLINE has only {abs(abs(requested) - abs(applied)):.3f} deg remaining; it should have completed within tolerance",
                terminal,
            )

        finish_rows = [row for row in grouped_rows if mark(row) in EVALUATION_FINISH_MARKS]
        if not finish_rows:
            result.add("WARN", "no evaluation finish after INCOMPLETE_HARD_DEADLINE (possibly truncated log)", terminal)
            continue
        for finish in finish_rows:
            valid, valid_error = parse_bool(finish, "validTrial")
            if valid_error:
                result.add("FAIL", valid_error, finish)
            if valid is not False:
                result.add("FAIL", "INCOMPLETE_HARD_DEADLINE evaluation is not logged as validTrial=false", finish)
            if clean(finish.get("trial_type")) == "NORMAL":
                delta, delta_error = parse_float(finish, "staircaseDeltaDeg")
                test_theta, test_error = parse_float(finish, "testThetaDeg")
                next_theta, next_error = parse_float(finish, "nextThetaDeg")
                reversal, reversal_error = parse_bool(finish, "isReversal")
                for error in (delta_error, test_error, next_error, reversal_error):
                    if error:
                        result.add("FAIL", error, finish)
                if delta is None or not almost_equal(delta, 0.0, tolerance):
                    result.add("FAIL", "incomplete normal evaluation updated staircaseDeltaDeg", finish)
                if test_theta is None or next_theta is None or not almost_equal(test_theta, next_theta, tolerance):
                    result.add("FAIL", "incomplete normal evaluation changed nextThetaDeg", finish)
                if reversal is not False:
                    result.add("FAIL", "incomplete normal evaluation is marked as reversal", finish)

    if checked == 0 and not result.issues:
        result.not_checkable_reason = "no INCOMPLETE_HARD_DEADLINE outcome was logged"
    return result


def check_response_timing(
    rows: list[dict[str, str]], tolerance: float
) -> CheckResult:
    result = CheckResult("response timing and RT", "accepted responses use actual injection start and remain inside the 3.0 s deadline")
    checked = 0
    for grouped_rows in group_evaluations(rows).values():
        accepted_rows = [row for row in grouped_rows if mark(row) == "RESPONSE_ACCEPTED"]
        timeout_rows = [row for row in grouped_rows if mark(row) == "RESPONSE_TIMEOUT"]
        if not accepted_rows and not timeout_rows:
            continue
        checked += 1

        for row in accepted_rows:
            injection_start, error1 = parse_float(row, "injection_start_time_sec")
            deadline, error2 = parse_float(row, "response_deadline_time_sec")
            response_time, error3 = parse_float(row, "response_time_sec")
            response_rt, error4 = parse_float(row, "response_rt_sec")
            for error in (error1, error2, error3, error4):
                if error:
                    result.add("FAIL", error, row)
            accepted, bool_error = parse_bool(row, "response_accepted")
            noticed, noticed_error = parse_bool(row, "noticed")
            for error in (bool_error, noticed_error):
                if error:
                    result.add("FAIL", error, row)
            if accepted is not True:
                result.add("FAIL", "RESPONSE_ACCEPTED row does not have response_accepted=true", row)
            if noticed is not True:
                result.add("FAIL", "RESPONSE_ACCEPTED row does not have noticed=true", row)
            if None in (injection_start, deadline, response_time, response_rt):
                result.add("FAIL", "accepted response is missing injection/deadline/response/RT timestamp", row)
                continue
            assert injection_start is not None and deadline is not None
            assert response_time is not None and response_rt is not None
            if not almost_equal(deadline - injection_start, 3.0, tolerance):
                result.add("FAIL", f"response window is {deadline - injection_start:.3f} s, expected 3.000 s", row)
            expected_rt = response_time - injection_start
            if response_rt < -tolerance or expected_rt < -tolerance:
                result.add("FAIL", f"negative response time: RT={response_rt:.3f} s", row)
            if not almost_equal(response_rt, expected_rt, tolerance):
                result.add("FAIL", f"logged RT {response_rt:.3f} != responseTime-injectionStart {expected_rt:.3f}", row)
            if response_time > deadline + tolerance:
                result.add("FAIL", "accepted response occurred after response deadline", row)

        for row in timeout_rows:
            injection_start, error1 = parse_float(row, "injection_start_time_sec")
            deadline, error2 = parse_float(row, "response_deadline_time_sec")
            row_time, error3 = parse_float(row, "timeSec")
            for error in (error1, error2, error3):
                if error:
                    result.add("FAIL", error, row)
            accepted, bool_error = parse_bool(row, "response_accepted")
            if bool_error:
                result.add("FAIL", bool_error, row)
            if accepted is not False:
                result.add("FAIL", "RESPONSE_TIMEOUT row does not have response_accepted=false", row)
            if clean(row.get("response_time_sec")) or clean(row.get("response_rt_sec")):
                result.add("FAIL", "timeout row unexpectedly contains response time or RT", row)
            if injection_start is None or deadline is None or row_time is None:
                result.add("FAIL", "timeout row is missing injection/deadline/current timestamp", row)
                continue
            if not almost_equal(deadline - injection_start, 3.0, tolerance):
                result.add("FAIL", f"response window is {deadline - injection_start:.3f} s, expected 3.000 s", row)
            if row_time < deadline - tolerance:
                result.add("FAIL", "RESPONSE_TIMEOUT was logged before response deadline", row)

    if checked == 0 and not result.issues:
        result.not_checkable_reason = "no accepted response or response timeout was logged"
    return result


def check_duplicate_responses(rows: list[dict[str, str]]) -> CheckResult:
    result = CheckResult("duplicate accepted response", "no evaluation has more than one accepted response")
    groups = group_evaluations(rows)
    checked = 0
    for grouped_rows in groups.values():
        accepted = [row for row in grouped_rows if mark(row) == "RESPONSE_ACCEPTED"]
        timeouts = [row for row in grouped_rows if mark(row) == "RESPONSE_TIMEOUT"]
        if accepted or timeouts:
            checked += 1
        if len(accepted) > 1:
            result.add("FAIL", f"evaluation has {len(accepted)} RESPONSE_ACCEPTED rows", accepted[1])
        if accepted and timeouts:
            result.add("FAIL", "evaluation contains both accepted response and timeout", timeouts[0])
        if len(timeouts) > 1:
            result.add("FAIL", f"evaluation has {len(timeouts)} RESPONSE_TIMEOUT rows", timeouts[1])
    if checked == 0 and not result.issues:
        result.not_checkable_reason = "no resolved response evaluation was logged"
    return result


def canonical_trial_type(grouped_rows: list[dict[str, str]]) -> str:
    for row in grouped_rows:
        value = clean(row.get("trial_type"))
        if value:
            return value
    return ""


def check_catch_types(rows: list[dict[str, str]], tolerance: float) -> CheckResult:
    result = CheckResult("catch type and theta", "Catch Zero/High flags and requested theta are correct")
    catch_groups: list[list[dict[str, str]]] = []
    valid_catches_by_run: dict[tuple[str, str, str], list[tuple[int, str, dict[str, str]]]] = defaultdict(list)

    for grouped_rows in group_evaluations(rows).values():
        trial_type = canonical_trial_type(grouped_rows)
        any_catch_flag = any(clean(row.get("is_catch")).lower() == "true" for row in grouped_rows)
        if trial_type not in {"CATCH_ZERO", "CATCH_HIGH"} and not any_catch_flag:
            continue
        catch_groups.append(grouped_rows)
        representative = grouped_rows[0]
        expected_type = "Zero" if trial_type == "CATCH_ZERO" else "High" if trial_type == "CATCH_HIGH" else ""
        expected_theta = 0.0 if trial_type == "CATCH_ZERO" else 25.0 if trial_type == "CATCH_HIGH" else None
        if expected_theta is None:
            result.add("FAIL", f"is_catch=true with unsupported trial_type={trial_type!r}", representative)
            continue
        requested, source_row, error = first_number(grouped_rows, "requested_theta_deg")
        if error:
            result.add("FAIL", error, source_row)
        if requested is None:
            result.add("FAIL", "catch requested_theta_deg is missing", representative)
        elif not almost_equal(requested, expected_theta, tolerance):
            result.add("FAIL", f"{trial_type} requested theta is {requested:.3f}, expected {expected_theta:.3f}", representative)

        for row in grouped_rows:
            is_catch, bool_error = parse_bool(row, "is_catch")
            if bool_error:
                result.add("FAIL", bool_error, row)
            if is_catch is not True:
                result.add("FAIL", f"{trial_type} row does not have is_catch=true", row)
            if clean(row.get("catch_type")) != expected_type:
                result.add("FAIL", f"{trial_type} row has catch_type={clean(row.get('catch_type'))!r}, expected {expected_type!r}", row)

        valid_finishes = []
        for row in grouped_rows:
            valid, _ = parse_bool(row, "validTrial")
            if mark(row) == "CATCH_EVAL_FINISH" and valid is True:
                valid_finishes.append(row)
        if valid_finishes:
            valid_catches_by_run[run_key(representative)].append(
                (int(valid_finishes[0]["_row_number"]), trial_type, valid_finishes[0])
            )

    for catches in valid_catches_by_run.values():
        catches.sort(key=lambda item: item[0])
        for previous, current in zip(catches, catches[1:]):
            if previous[1] == current[1]:
                result.add("FAIL", f"consecutive valid catches repeat {current[1]} instead of alternating", current[2])

    if not catch_groups and not result.issues:
        result.not_checkable_reason = "no catch evaluation was logged"
    return result


def check_catch_staircase(rows: list[dict[str, str]], tolerance: float) -> CheckResult:
    result = CheckResult("catch staircase state", "logged theta, step, and reversal state remain unchanged across valid catches")
    valid_catch_finishes: list[dict[str, str]] = []
    for row in rows:
        valid, _ = parse_bool(row, "validTrial")
        if mark(row) == "CATCH_EVAL_FINISH" and valid is True:
            valid_catch_finishes.append(row)

    if not valid_catch_finishes:
        result.not_checkable_reason = "no valid catch finish was logged"
        return result

    for catch_row in valid_catch_finishes:
        catch_number = int(catch_row["_row_number"])
        key = run_key(catch_row)
        eval_id = clean(catch_row.get("evaluation_id"))
        extra = parse_extra(catch_row)
        try:
            held_theta = float(extra["heldStaircaseThetaDeg"])
        except (KeyError, ValueError):
            held_theta = None
            result.add("FAIL", "catch finish lacks numeric extra.heldStaircaseThetaDeg", catch_row)

        same_evaluation = [row for row in rows if evaluation_key(row) == evaluation_key(catch_row)]
        if any(mark(row) in {"STAIRCASE_REVERSAL", "STAIRCASE_RESULT", "STAIRCASE_EVAL_FINISH"} for row in same_evaluation):
            result.add("FAIL", "catch evaluation emitted a staircase update/result mark", catch_row)
        for field_name in ("staircaseDeltaDeg", "nextThetaDeg", "reversalIndex"):
            if clean(catch_row.get(field_name)):
                result.add("FAIL", f"catch finish unexpectedly populates {field_name}", catch_row)
        is_reversal, bool_error = parse_bool(catch_row, "isReversal")
        if bool_error:
            result.add("FAIL", bool_error, catch_row)
        if is_reversal is True:
            result.add("FAIL", "catch finish is marked as a reversal", catch_row)

        previous_candidates = []
        next_candidates = []
        for row in rows:
            if run_key(row) != key:
                continue
            number = int(row["_row_number"])
            valid, _ = parse_bool(row, "validTrial")
            if number < catch_number and mark(row) == "STAIRCASE_EVAL_FINISH" and valid is True:
                previous_candidates.append(row)
            if number > catch_number and mark(row) == "STAIRCASE_EVAL_START":
                next_candidates.append(row)
        previous = previous_candidates[-1] if previous_candidates else None
        following = next_candidates[0] if next_candidates else None

        if previous is None:
            result.add("WARN", "no preceding valid normal finish; pre-catch staircase state cannot be compared", catch_row)
        if following is None:
            result.add("WARN", "no following normal start; post-catch staircase state cannot be compared", catch_row)
        if previous is None or following is None:
            continue

        previous_theta, error1 = parse_float(previous, "nextThetaDeg")
        next_theta, error2 = parse_float(following, "testThetaDeg")
        previous_step, error3 = parse_float(previous, "currentStepDeg")
        next_step, error4 = parse_float(following, "currentStepDeg")
        previous_reversals, error5 = parse_float(previous, "reversalCount")
        next_reversals, error6 = parse_float(following, "reversalCount")
        for error, source in (
            (error1, previous), (error2, following), (error3, previous),
            (error4, following), (error5, previous), (error6, following),
        ):
            if error:
                result.add("FAIL", error, source)

        if held_theta is not None and previous_theta is not None and not almost_equal(held_theta, previous_theta, tolerance):
            result.add("FAIL", f"held staircase theta {held_theta:.3f} != preceding next theta {previous_theta:.3f}", catch_row)
        if held_theta is not None and next_theta is not None and not almost_equal(held_theta, next_theta, tolerance):
            result.add("FAIL", f"following normal theta {next_theta:.3f} != held catch theta {held_theta:.3f}", following)
        if previous_step is not None and next_step is not None and not almost_equal(previous_step, next_step, tolerance):
            result.add("FAIL", f"staircase step changed across catch: {previous_step:.3f} -> {next_step:.3f}", following)
        if previous_reversals is not None and next_reversals is not None and not almost_equal(previous_reversals, next_reversals, tolerance):
            result.add("FAIL", f"reversal count changed across catch: {previous_reversals:.0f} -> {next_reversals:.0f}", following)

        if eval_id and clean(following.get("evaluation_id")) == eval_id:
            result.add("FAIL", "following normal evaluation reused the catch evaluation_id", following)
    return result


def check_invalid_evaluations(rows: list[dict[str, str]], tolerance: float) -> CheckResult:
    result = CheckResult("invalid evaluation staircase", "invalid evaluations do not update logged staircase state")
    invalid_finishes: list[dict[str, str]] = []
    for row in rows:
        valid, error = parse_bool(row, "validTrial")
        if error:
            result.add("FAIL", error, row)
        if mark(row) in EVALUATION_FINISH_MARKS and valid is False:
            invalid_finishes.append(row)

    if not invalid_finishes and not result.issues:
        result.not_checkable_reason = "no invalid evaluation finish was logged"
        return result

    for finish in invalid_finishes:
        trial_type = clean(finish.get("trial_type"))
        grouped_rows = [row for row in rows if evaluation_key(row) == evaluation_key(finish)]
        if any(mark(row) in {"STAIRCASE_REVERSAL", "STAIRCASE_RESULT"} for row in grouped_rows):
            result.add("FAIL", "invalid evaluation emitted staircase reversal/result", finish)

        if trial_type == "NORMAL":
            delta, error1 = parse_float(finish, "staircaseDeltaDeg")
            test_theta, error2 = parse_float(finish, "testThetaDeg")
            next_theta, error3 = parse_float(finish, "nextThetaDeg")
            reversal, error4 = parse_bool(finish, "isReversal")
            for error in (error1, error2, error3, error4):
                if error:
                    result.add("FAIL", error, finish)
            if delta is None or not almost_equal(delta, 0.0, tolerance):
                result.add("FAIL", f"invalid normal evaluation has staircase delta {delta!r}, expected 0", finish)
            if test_theta is None or next_theta is None or not almost_equal(test_theta, next_theta, tolerance):
                result.add("FAIL", "invalid normal evaluation changed next theta", finish)
            if reversal is not False:
                result.add("FAIL", "invalid normal evaluation is marked as reversal", finish)

        finish_number = int(finish["_row_number"])
        trial_id = clean(finish.get("trial_id"))
        retry_starts = [
            row for row in rows
            if int(row["_row_number"]) > finish_number
            and clean(row.get("trial_id")) == trial_id
            and mark(row) in EVALUATION_START_MARKS
        ]
        if not retry_starts:
            result.add("WARN", "no later retry of the same trial_id was found (possibly truncated log)", finish)
            continue
        retry = retry_starts[0]
        if clean(retry.get("evaluation_id")) == clean(finish.get("evaluation_id")):
            result.add("FAIL", "invalid retry reused evaluation_id", retry)
        if clean(retry.get("trial_type")) != trial_type:
            result.add("FAIL", "invalid retry changed trial_type", retry)
        original_theta, _, error1 = first_number(grouped_rows, "requested_theta_deg")
        retry_theta, error2 = parse_float(retry, "requested_theta_deg")
        for error, source in ((error1, finish), (error2, retry)):
            if error:
                result.add("FAIL", error, source)
        if original_theta is not None and retry_theta is not None and not almost_equal(original_theta, retry_theta, tolerance):
            result.add("FAIL", f"invalid retry changed requested theta: {original_theta:.3f} -> {retry_theta:.3f}", retry)
    return result


def check_walking_speed(rows: list[dict[str, str]], warn_threshold: float) -> CheckResult:
    result = CheckResult(
        "walking speed values",
        f"speed fields are present, finite, non-negative, and <= {warn_threshold:g} m/s heuristic threshold",
    )
    starts = [row for row in rows if mark(row) in EVALUATION_START_MARKS]
    injections = [row for row in rows if mark(row) == "INJECTION_START"]
    if not starts and not injections:
        result.not_checkable_reason = "no evaluation/injection start was logged"
        return result

    for row in starts:
        value, error = parse_float(row, "walking_speed_trigger_mps")
        if error:
            result.add("FAIL", error, row)
        elif value is None:
            result.add("FAIL", "walking_speed_trigger_mps is missing on evaluation start", row)
    for row in injections:
        value, error = parse_float(row, "walking_speed_injection_mps")
        if error:
            result.add("FAIL", error, row)
        elif value is None:
            result.add("FAIL", "walking_speed_injection_mps is missing on injection start", row)

    for row in rows:
        for field_name in ("walking_speed_trigger_mps", "walking_speed_injection_mps"):
            value, error = parse_float(row, field_name)
            if error:
                result.add("FAIL", error, row)
                continue
            if value is None:
                continue
            if value < 0:
                result.add("FAIL", f"{field_name} is negative: {value:.3f} m/s", row)
            elif value > warn_threshold:
                result.add(
                    "WARN",
                    f"{field_name}={value:.3f} m/s exceeds configurable QA heuristic {warn_threshold:g} m/s",
                    row,
                )
    return result


def check_abort_reset_continuation(rows: list[dict[str, str]]) -> CheckResult:
    result = CheckResult("Abort/Reset continuation", "no experimental progress is logged after a terminal Abort/Reset in the same run")
    terminal_rows = [row for row in rows if mark(row) in TERMINAL_MARKS]
    checkable_terminals = [row for row in terminal_rows if clean(row.get("run_id"))]
    if not terminal_rows:
        result.not_checkable_reason = "no Abort/Reset terminal mark was logged"
        return result
    if not checkable_terminals:
        result.not_checkable_reason = "Abort/Reset rows have no run_id, so later rows cannot be linked reliably"
        return result

    for terminal in checkable_terminals:
        terminal_number = int(terminal["_row_number"])
        key = run_key(terminal)
        for later in rows:
            if int(later["_row_number"]) <= terminal_number or run_key(later) != key:
                continue
            later_mark = mark(later)
            if not later_mark or later_mark in CLEANUP_MARKS:
                continue
            if is_evaluation_mark(later) or later_mark in {"TRIAL_FINISH", "TRIAL_START"}:
                result.add(
                    "FAIL",
                    f"{later_mark} continues the same run after terminal {mark(terminal)} at row {terminal_number}",
                    later,
                )
    return result


def not_checkable_results() -> list[CheckResult]:
    results = []
    for name, reason in (
        (
            "catch hidden counters/threshold",
            "CSV does not log total valid-normal count and threshold estimator state on every catch row",
        ),
        (
            "walking speed provenance",
            "numeric speed can be checked, but CSV alone cannot prove that Quest artificial locomotion was excluded",
        ),
        (
            "Abort/Reset runtime cleanup",
            "CSV can detect later continuation, but cannot prove occluder, transforms, timers, and physical origins were cleared",
        ),
        (
            "per-frame rotation trajectory",
            "CSV logs requested and aggregate applied theta, not every per-frame rotation delta",
        ),
    ):
        result = CheckResult(name)
        result.not_checkable_reason = reason
        results.append(result)
    return results


def validate(
    header: list[str],
    rows: list[dict[str, str]],
    structural_errors: list[str],
    tolerance: float,
    speed_warn_mps: float,
) -> list[CheckResult]:
    results = [
        check_schema(header, rows, structural_errors),
        check_identifiers(rows),
        check_requested_vs_applied(rows, tolerance),
        check_injection_timing(rows, tolerance),
        check_hard_deadline_incomplete(rows, tolerance),
        check_response_timing(rows, tolerance),
        check_duplicate_responses(rows),
        check_catch_types(rows, tolerance),
        check_catch_staircase(rows, tolerance),
        check_invalid_evaluations(rows, tolerance),
        check_walking_speed(rows, speed_warn_mps),
        check_abort_reset_continuation(rows),
    ]
    results.extend(not_checkable_results())
    return results


def print_results(results: list[CheckResult], max_details: int) -> None:
    for result in results:
        if result.status == "NOT CHECKABLE":
            print(f"NOT CHECKABLE: {result.name} - {result.not_checkable_reason}")
            continue
        if result.status == "PASS":
            print(f"PASS: {result.name} - {result.pass_message}")
            continue

        print(f"{result.status}: {result.name} - {len(result.issues)} issue(s)")
        for issue in result.issues[:max_details]:
            location = []
            if issue.row_number is not None:
                location.append(f"row={issue.row_number}")
            if issue.trial_id:
                location.append(f"trial_id={issue.trial_id}")
            if issue.evaluation_id:
                location.append(f"evaluation_id={issue.evaluation_id}")
            prefix = " ".join(location) if location else "file"
            print(f"  {issue.status}: {prefix} - {issue.reason}")
        if len(result.issues) > max_details:
            print(f"  WARN: {len(result.issues) - max_details} additional issue(s) omitted")

    counts = Counter(result.status for result in results)
    print("\nSummary")
    for status in ("PASS", "WARN", "FAIL", "NOT CHECKABLE"):
        print(f"* {status}: {counts[status]}")


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description="Validate an RDW pilot events.csv produced by the current EventLogger schema."
    )
    parser.add_argument("events_csv", type=Path, help="path to events.csv")
    parser.add_argument(
        "--float-tolerance",
        type=float,
        default=0.01,
        help="absolute tolerance for logged float comparisons (default: 0.01)",
    )
    parser.add_argument(
        "--speed-warn-mps",
        type=float,
        default=5.0,
        help="configurable QA warning threshold for HMD XZ speed, not a protocol limit (default: 5.0)",
    )
    parser.add_argument(
        "--max-details",
        type=int,
        default=20,
        help="maximum issue details printed per check (default: 20)",
    )
    return parser


def main(argv: Optional[list[str]] = None) -> int:
    args = build_parser().parse_args(argv)
    if args.float_tolerance < 0:
        print("ERROR: --float-tolerance must be non-negative", file=sys.stderr)
        return 2
    if args.speed_warn_mps <= 0:
        print("ERROR: --speed-warn-mps must be positive", file=sys.stderr)
        return 2
    if args.max_details < 1:
        print("ERROR: --max-details must be at least 1", file=sys.stderr)
        return 2
    if not args.events_csv.is_file():
        print(f"ERROR: CSV file not found: {args.events_csv}", file=sys.stderr)
        return 2

    try:
        header, rows, structural_errors = read_csv(args.events_csv)
    except (OSError, UnicodeError, csv.Error) as error:
        print(f"ERROR: could not read CSV: {error}", file=sys.stderr)
        return 2

    results = validate(
        header,
        rows,
        structural_errors,
        args.float_tolerance,
        args.speed_warn_mps,
    )
    print(f"Pilot log: {args.events_csv}")
    print(f"Rows: {len(rows)}\n")
    print_results(results, args.max_details)
    return 1 if any(result.status == "FAIL" for result in results) else 0


if __name__ == "__main__":
    raise SystemExit(main())

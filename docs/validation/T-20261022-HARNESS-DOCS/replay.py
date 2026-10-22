"""Replay the support-document defect using a disposable copy of this project."""
from __future__ import annotations

import argparse
from datetime import datetime, timezone
import hashlib
import json
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

FILES = (
    "AGENTS.md", "CLAUDE.md", ".github/copilot-instructions.md",
    "docs/tasks/active.md", "docs/status.md", "docs/harness.md",
    ".github/pull_request_template.md",
)
PRESERVED = ("docs/status.md", "docs/harness.md", ".github/pull_request_template.md")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--skills-root", type=Path, required=True)
    args = parser.parse_args()
    source = args.skills_root.resolve()
    project = Path(__file__).resolve().parents[3]
    engine = source / "skills/bootstrap-harness/scripts/harness.py"
    baseline = subprocess.run(
        ["git", "-C", str(source), "show", "af63286:skills/bootstrap-harness/scripts/harness.py"],
        check=True, capture_output=True,
    ).stdout
    report = {
        "recorded_at": datetime.now(timezone.utc).isoformat(),
        "python_version": sys.version.split()[0],
        "baseline_ref": "af63286",
        "candidate_ref": subprocess.run(
            ["git", "-C", str(source), "rev-parse", "HEAD"],
            check=True, capture_output=True, text=True).stdout.strip(),
        "candidate_engine_sha256": hashlib.sha256(engine.read_bytes()).hexdigest(),
        "baseline_engine_sha256": hashlib.sha256(baseline).hexdigest(),
        "project_input_sha256": {name: hashlib.sha256((project/name).read_bytes()).hexdigest()
                                 for name in FILES},
        "runs": [],
    }
    with tempfile.TemporaryDirectory(prefix="ms-harness-pilot-") as directory:
        temporary = Path(directory)
        fixture = temporary / "project"
        for name in FILES:
            target = fixture / name
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(project/name, target)
        old = temporary / "baseline/harness.py"
        old.parent.mkdir()
        old.write_bytes(baseline)
        shutil.copytree(source/"skills/bootstrap-harness/templates", old.parent/"templates")
        preserved = {name: (fixture/name).read_bytes() for name in PRESERVED}

        def run(label, executable, command, expected, *extra):
            result = subprocess.run(
                [sys.executable, "-B", "-X", "utf8", str(executable), command,
                 "--root", str(fixture), *extra],
                capture_output=True, text=True, encoding="utf-8", check=False,
            )
            report["runs"].append({"id": label, "arguments": [command, "--root", "<temporary-project>", *extra],
                                   "exit": result.returncode,
                                   "stdout": result.stdout, "stderr": result.stderr})
            if result.returncode != expected:
                raise RuntimeError(f"{label}: expected exit {expected}, got {result.returncode}")

        run("intact-project", engine, "check", 0)
        task = fixture/"docs/tasks/active.md"
        task.unlink()
        run("baseline-misses-absence", old, "check", 0)
        run("baseline-cannot-restore", old, "sync", 0, "--write")
        if task.exists():
            raise RuntimeError("Baseline unexpectedly restored the missing task card")
        run("candidate-rejects-absence", engine, "check", 1)
        run("candidate-restores", engine, "sync", 0, "--write")
        if not task.read_text(encoding="utf-8").strip():
            raise RuntimeError("Restored task card is blank")
        run("restored-project", engine, "check", 0)
        if any((fixture/name).read_bytes() != content for name, content in preserved.items()):
            raise RuntimeError("Sync changed existing project support documents")
        report["preserved_documents"] = list(PRESERVED)
        report["result"] = "passed"
    print(json.dumps(report, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()

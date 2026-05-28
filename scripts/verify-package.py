#!/usr/bin/env python3
"""
verify-package.py — Pack a project (or use an existing .nupkg) and verify it
matches the Vendor/Bundle/Analyzer contract declared in its csproj, including
full transitive binary closure, per-TFM for multi-targeted libraries.

Modes:
  Pack-and-verify (no positional nupkg):
    ./verify-package.py --csproj src/Pkg/Pkg.csproj
    ./verify-package.py --csproj src/Pkg/Pkg.csproj --output-dir ./out
    By default packs to a unique temp directory.

  Verify an existing nupkg:
    ./verify-package.py path/to/Pkg.1.2.3.nupkg --csproj src/Pkg/Pkg.csproj

Flags:
  --configuration <name>   pack configuration (default: Release)
  --output-dir <dir>       pack into this dir instead of a temp dir
  --spec <file>            additional assertions (forbid-dep, require-file, forbid-file)
  --no-transitive          skip transitive closure checks
  --list                   print package contents
"""

from __future__ import annotations

import argparse
import json
import re
import subprocess
import sys
import tempfile
import zipfile
from dataclasses import dataclass, field
from pathlib import Path
from xml.etree import ElementTree as ET


class Color:
    R = "\033[31m"; G = "\033[32m"; Y = "\033[33m"; D = "\033[2m"; X = "\033[0m"
    @classmethod
    def off(cls):
        cls.R = cls.G = cls.Y = cls.D = cls.X = ""

if not sys.stdout.isatty():
    Color.off()


@dataclass
class Report:
    passes: int = 0
    fails: list[str] = field(default_factory=list)

    def ok(self, msg: str) -> None:
        print(f"{Color.G}\u2713{Color.X} {msg}")
        self.passes += 1

    def bad(self, msg: str, detail: list[str] | None = None) -> None:
        print(f"{Color.R}\u2717{Color.X} {msg}")
        if detail:
            for d in detail:
                print(f"    {d}")
        self.fails.append(msg)

    def note(self, msg: str) -> None:
        print(f"{Color.D}\u00b7 {msg}{Color.X}")


# ---------- csproj parsing ----------

@dataclass
class CsprojReference:
    kind: str
    identity: str
    assembly_name: str
    raw_include: str
    csproj_path: Path | None
    vendor: bool = False
    bundle: bool = False
    analyzer: bool = False
    private_assets: bool = False


@dataclass
class CsprojContract:
    refs: list[CsprojReference]
    csproj_path: Path

    @property
    def vendored_packages(self):
        return [r for r in self.refs if r.kind == "package" and r.vendor]

    @property
    def normal_packages(self):
        return [r for r in self.refs if r.kind == "package" and not r.vendor and not r.private_assets]

    @property
    def bundled_projects(self):
        return [r for r in self.refs if r.kind == "project" and r.bundle]

    @property
    def analyzer_projects(self):
        return [r for r in self.refs if r.kind == "project" and r.analyzer]


def _strip_ns(tag: str) -> str:
    return tag.split("}", 1)[1] if "}" in tag else tag


def _flag(elem: ET.Element, name: str) -> bool:
    attr = elem.get(name)
    if attr is not None:
        return attr.strip().lower() == "true"
    for c in elem:
        if _strip_ns(c.tag) == name:
            return (c.text or "").strip().lower() == "true"
    return False


def _private_assets_all(elem: ET.Element) -> bool:
    """Check if a reference has PrivateAssets="all" (case-insensitive)."""
    attr = elem.get("PrivateAssets")
    if attr is not None:
        return attr.strip().lower() == "all"
    for c in elem:
        if _strip_ns(c.tag) == "PrivateAssets":
            return (c.text or "").strip().lower() == "all"
    return False


def _resolve_assembly_name(csproj_path: Path) -> str | None:
    """Read <AssemblyName> from a csproj. Returns None if not set."""
    if not csproj_path.is_file():
        return None
    try:
        root = ET.parse(csproj_path).getroot()
        for elem in root.iter():
            if _strip_ns(elem.tag) == "AssemblyName" and elem.text:
                return elem.text.strip()
    except ET.ParseError:
        pass
    return None


def _suppressed_package_ids(assets_path: Path) -> set[str]:
    """Read project.assets.json's project spec to find packages with suppressParent=All.
    These are PrivateAssets="all" dependencies that shouldn't flow to consumers."""
    if not assets_path.is_file():
        return set()
    data = json.loads(assets_path.read_text(encoding="utf-8"))
    suppressed: set[str] = set()
    project = data.get("project", {})
    for _tfm, fwdata in project.get("frameworks", {}).items():
        for pkg_id, dep_info in fwdata.get("dependencies", {}).items():
            if isinstance(dep_info, dict) and dep_info.get("suppressParent", "").lower() == "all":
                suppressed.add(pkg_id.lower())
    return suppressed


def parse_csproj(path: Path) -> CsprojContract:
    if not path.is_file():
        raise SystemExit(f"csproj not found: {path}")
    csproj_dir = path.parent.resolve()
    root = ET.parse(path).getroot()
    refs: list[CsprojReference] = []

    for elem in root.iter():
        tag = _strip_ns(elem.tag)
        if tag == "PackageReference":
            ident = elem.get("Include") or elem.get("Update")
            if not ident:
                continue
            refs.append(CsprojReference(
                kind="package",
                identity=ident,
                assembly_name=ident,
                raw_include=ident,
                csproj_path=None,
                vendor=_flag(elem, "Vendor"),
                private_assets=_private_assets_all(elem),
            ))
        elif tag == "ProjectReference":
            inc = elem.get("Include")
            if not inc:
                continue
            inc_norm = inc.replace("\\", "/")
            proj_path = (csproj_dir / inc_norm).resolve()
            proj_name = Path(inc_norm).stem
            asm_name = _resolve_assembly_name(proj_path) or proj_name
            refs.append(CsprojReference(
                kind="project",
                identity=proj_name,
                assembly_name=asm_name,
                raw_include=inc,
                csproj_path=proj_path,
                bundle=_flag(elem, "Bundle"),
                analyzer=_flag(elem, "Analyzer"),
                private_assets=_private_assets_all(elem),
            ))

    return CsprojContract(refs=refs, csproj_path=path.resolve())


# ---------- project.assets.json parsing ----------

FRAMEWORK_PACKAGE_PREFIXES = (
    "Microsoft.NETCore.App",
    "Microsoft.AspNetCore.App",
    "Microsoft.WindowsDesktop.App",
    "NETStandard.Library",
    "Microsoft.NETCore.Platforms",
    "Microsoft.NETCore.Targets",
)


def _is_framework_lib(name: str) -> bool:
    return any(name == p or name.startswith(p + ".") for p in FRAMEWORK_PACKAGE_PREFIXES)


def find_assets_file(csproj_path: Path) -> Path:
    candidate = csproj_path.parent / "obj" / "project.assets.json"
    if not candidate.is_file():
        raise SystemExit(
            f"project.assets.json not found: {candidate}\n"
            f"Run `dotnet restore` (or `dotnet pack`) on {csproj_path.name} first."
        )
    return candidate


@dataclass
class ResolvedFile:
    package_id: str
    sub_path: str                    # path as it appears in the source package, e.g. 'lib/net8.0/Foo.dll'
    filename: str

    @property
    def is_native(self) -> bool:
        return self.sub_path.startswith("runtimes/")

    def expected_path_under_lib(self, parent_tfm: str) -> str | None:
        """For a non-native file, the expected path inside the parent nupkg
        under lib/<parent_tfm>/. Strips the source 'lib/<src_tfm>/' prefix and
        re-roots under the parent's TFM. Returns None for native files."""
        if self.is_native:
            return None
        parts = self.sub_path.split("/", 2)
        if len(parts) >= 3 and parts[0] == "lib":
            # parts[2] is the in-lib relative path: 'Foo.dll' or 'fr/Foo.resources.dll'
            return f"lib/{parent_tfm}/{parts[2]}"
        # Fallback for unusual layouts (compile/* etc)
        return f"lib/{parent_tfm}/{self.filename}"


@dataclass
class ResolvedPackage:
    package_id: str
    version: str
    dependencies: list[str]
    files: list[ResolvedFile]


@dataclass
class AssetsGraph:
    """Resolved package graph for a single TFM."""
    tfm: str
    packages: dict[str, ResolvedPackage]   # keyed by lowercased package id

    def closure(self, root_ids: list[str]) -> set[str]:
        out: set[str] = set()
        stack = [r.lower() for r in root_ids]
        while stack:
            cur = stack.pop()
            if cur in out:
                continue
            if cur not in self.packages:
                continue
            pkg = self.packages[cur]
            if _is_framework_lib(pkg.package_id):
                continue
            out.add(cur)
            for dep in pkg.dependencies:
                if dep.lower() not in out:
                    stack.append(dep.lower())
        return out


@dataclass
class MultiTfmAssets:
    """All RID-less per-TFM graphs from one project.assets.json."""
    graphs: dict[str, AssetsGraph]        # keyed by TFM string, e.g. 'net10.0'

    @property
    def tfms(self) -> list[str]:
        return sorted(self.graphs.keys())


def _make_resolved_file(package_id: str, sub_path: str) -> ResolvedFile:
    sub_path_norm = sub_path.replace("\\", "/")
    filename = Path(sub_path_norm).name
    return ResolvedFile(
        package_id=package_id,
        sub_path=sub_path_norm,
        filename=filename,
    )


def _build_graph(tfm: str, target_data: dict) -> AssetsGraph:
    packages: dict[str, ResolvedPackage] = {}
    for full_id, entry in target_data.items():
        if entry.get("type") != "package":
            continue
        name, _, version = full_id.partition("/")
        deps = list((entry.get("dependencies") or {}).keys())
        files: list[ResolvedFile] = []
        for sub_path in (entry.get("compile") or {}):
            files.append(_make_resolved_file(name, sub_path))
        for sub_path in (entry.get("runtime") or {}):
            files.append(_make_resolved_file(name, sub_path))
        for sub_path in (entry.get("runtimeTargets") or {}):
            files.append(_make_resolved_file(name, sub_path))
        for sub_path in (entry.get("resource") or {}):
            files.append(_make_resolved_file(name, sub_path))

        seen = set()
        unique_files = []
        for f in files:
            if f.sub_path in seen:
                continue
            seen.add(f.sub_path)
            unique_files.append(f)

        packages[name.lower()] = ResolvedPackage(
            package_id=name, version=version, dependencies=deps, files=unique_files,
        )
    return AssetsGraph(tfm=tfm, packages=packages)


def load_all_assets(assets_path: Path) -> MultiTfmAssets:
    data = json.loads(assets_path.read_text(encoding="utf-8"))
    targets: dict[str, dict] = data.get("targets", {})
    if not targets:
        raise SystemExit(f"{assets_path} has no targets — was the project restored?")
    graphs: dict[str, AssetsGraph] = {}
    for tfm_key, target_data in targets.items():
        # Skip RID-qualified targets (e.g. 'net10.0/linux-x64'); we use RID-less per-TFM.
        if "/" in tfm_key:
            continue
        graphs[tfm_key] = _build_graph(tfm_key, target_data)
    if not graphs:
        # Edge case: all targets RID-qualified. Fall back to the first one.
        first = next(iter(targets))
        graphs[first.split("/", 1)[0]] = _build_graph(first, targets[first])
    return MultiTfmAssets(graphs=graphs)


# ---------- TFM matching ----------

def best_match_tfm(parent_tfm: str, candidate_tfms: list[str]) -> str | None:
    """Pick the most appropriate candidate TFM for a given parent TFM.
    Exact match first, then netstandard fallback for .NET 5+, else first available.
    Not a full NuGet compatibility implementation; covers common cases."""
    if not candidate_tfms:
        return None
    if parent_tfm in candidate_tfms:
        return parent_tfm

    # .NET 5+ TFMs accept netstandard2.1 and netstandard2.0
    m = re.match(r"^net(\d+)\.(\d+)$", parent_tfm)
    if m and int(m.group(1)) >= 5:
        for fallback in ("netstandard2.1", "netstandard2.0"):
            if fallback in candidate_tfms:
                return fallback

    # .NET Framework consumes netstandard2.0
    if parent_tfm.startswith("net4") and "netstandard2.0" in candidate_tfms:
        return "netstandard2.0"

    return candidate_tfms[0]


# ---------- nupkg inspection ----------

@dataclass
class PackageInspection:
    entries: list[str]
    entries_lower: set[str]
    declared_deps: set[str]
    nuspec_text: str

    def has_path(self, path: str) -> bool:
        return path.lower() in self.entries_lower

    def lib_tfms(self) -> list[str]:
        """TFMs the package ships under lib/."""
        tfms: set[str] = set()
        for e in self.entries:
            if e.startswith("lib/"):
                parts = e.split("/", 2)
                if len(parts) >= 3:        # need lib/<tfm>/<file>
                    tfms.add(parts[1])
        return sorted(tfms)


def inspect_package(nupkg: Path) -> PackageInspection:
    if not nupkg.is_file():
        raise SystemExit(f"nupkg not found: {nupkg}")
    with zipfile.ZipFile(nupkg) as z:
        entries = sorted(n for n in z.namelist() if not n.endswith("/"))
        nuspec_name = next((n for n in entries if n.endswith(".nuspec") and "/" not in n), None)
        if not nuspec_name:
            raise SystemExit(f"no .nuspec at package root in {nupkg}")
        nuspec_text = z.read(nuspec_name).decode("utf-8")

    dep_ids = {
        m.group(1).lower()
        for m in re.finditer(r'<dependency\b[^>]*\bid="([^"]+)"', nuspec_text)
    }
    return PackageInspection(
        entries=entries,
        entries_lower={e.lower() for e in entries},
        declared_deps=dep_ids,
        nuspec_text=nuspec_text,
    )


# ---------- closure checks ----------

def _is_real_payload(rf: ResolvedFile) -> bool:
    if rf.filename == "_._":
        return False
    if rf.sub_path.startswith("ref/"):
        return False
    return True


@dataclass
class ClosureCheckResult:
    checked_managed: int = 0
    missing_managed: list[str] = field(default_factory=list)
    native_files: list[ResolvedFile] = field(default_factory=list)   # to dedupe & check globally


def check_managed_closure_for_tfm(
    *,
    parent_tfm: str,
    closure_pkg_ids: set[str],
    graph: AssetsGraph,
    public_dep_ids: set[str],
    pkg: PackageInspection,
) -> ClosureCheckResult:
    out = ClosureCheckResult()
    for pkg_id_lower in sorted(closure_pkg_ids):
        if pkg_id_lower in public_dep_ids:
            continue
        resolved = graph.packages.get(pkg_id_lower)
        if not resolved:
            continue
        for rf in resolved.files:
            if not _is_real_payload(rf):
                continue
            if rf.is_native:
                out.native_files.append(rf)
                continue
            expected = rf.expected_path_under_lib(parent_tfm)
            assert expected is not None
            out.checked_managed += 1
            if not pkg.has_path(expected):
                out.missing_managed.append(f"{expected} (from {rf.package_id})")
    return out


def check_native_files(pkg: PackageInspection, natives: list[ResolvedFile]) -> tuple[int, list[str]]:
    """Dedupe natives by their sub_path and verify each ships at exactly that path."""
    seen: set[str] = set()
    unique: list[ResolvedFile] = []
    for rf in natives:
        key = rf.sub_path.lower()
        if key in seen:
            continue
        seen.add(key)
        unique.append(rf)
    missing = [f"{rf.sub_path} (from {rf.package_id})" for rf in unique if not pkg.has_path(rf.sub_path)]
    return len(unique), missing


# ---------- universal checks ----------

PDB_RE = re.compile(r"\.pdb$", re.IGNORECASE)


def run_universal_checks(pkg: PackageInspection, r: Report) -> None:
    pdbs = [e for e in pkg.entries if PDB_RE.search(e)]
    if pdbs:
        r.bad("found .pdb files (should be stripped)", pdbs)
    else:
        r.ok("no .pdb files in package")

    if any(e.startswith("lib/") for e in pkg.entries):
        r.ok("lib/ directory present")
    else:
        r.bad("no lib/ directory — package appears empty")


# ---------- csproj-driven checks ----------

def run_csproj_checks(
    pkg: PackageInspection,
    contract: CsprojContract,
    r: Report,
    *,
    check_transitive: bool,
) -> None:
    parent_tfms = pkg.lib_tfms()
    if not parent_tfms:
        r.bad("no lib/<tfm>/ entries found in package; cannot run TFM-aware checks")
        return
    r.note(f"package targets TFM(s): {', '.join(parent_tfms)}")

    main_assets: MultiTfmAssets | None = None
    if check_transitive:
        try:
            main_assets = load_all_assets(find_assets_file(contract.csproj_path))
        except SystemExit as e:
            r.bad(str(e))
            return
        r.note(f"main project assets resolved for TFM(s): {', '.join(main_assets.tfms)}")

    public_dep_ids = {p.identity.lower() for p in contract.normal_packages}

    # ---- Vendored packages ----
    for vp in contract.vendored_packages:
        if vp.identity.lower() in pkg.declared_deps:
            r.bad(f"vendored package '{vp.identity}' is declared as a dependency (should be hidden)")
        else:
            r.ok(f"vendored package '{vp.identity}' correctly absent from nuspec dependencies")

        if not check_transitive or main_assets is None:
            continue

        all_natives: list[ResolvedFile] = []
        for tfm in parent_tfms:
            graph = main_assets.graphs.get(tfm)
            if graph is None:
                r.bad(f"vendored '{vp.identity}' [tfm={tfm}] — assets file has no target for this TFM")
                continue
            closure = graph.closure([vp.identity])
            if not closure:
                r.bad(f"vendored '{vp.identity}' [tfm={tfm}] — not in resolved graph (typo or restore not run?)")
                continue
            result = check_managed_closure_for_tfm(
                parent_tfm=tfm,
                closure_pkg_ids=closure,
                graph=graph,
                public_dep_ids=public_dep_ids,
                pkg=pkg,
            )
            all_natives.extend(result.native_files)
            if result.missing_managed:
                r.bad(
                    f"vendored '{vp.identity}' [tfm={tfm}] — missing {len(result.missing_managed)} managed file(s):",
                    result.missing_managed,
                )
            else:
                r.ok(f"vendored '{vp.identity}' [tfm={tfm}] — full managed closure present ({result.checked_managed} file(s))")

        if all_natives:
            count, missing = check_native_files(pkg, all_natives)
            if missing:
                r.bad(f"vendored '{vp.identity}' [native] — missing {len(missing)} RID-scoped file(s):", missing)
            else:
                r.ok(f"vendored '{vp.identity}' [native] — {count} RID-scoped file(s) present")

    # ---- Bundled projects ----
    for bp in contract.bundled_projects:
        # NuGet derives the dependency ID from AssemblyName (or project name as fallback).
        bp_dep_names = {bp.identity.lower(), bp.assembly_name.lower()}
        leaked = bp_dep_names & pkg.declared_deps
        if leaked:
            r.bad(f"bundled project '{bp.identity}' is declared as a dependency '{next(iter(leaked))}' (should be hidden)")
        else:
            r.ok(f"bundled project '{bp.identity}' correctly absent from nuspec dependencies")

        # The bundled project's own DLL must ship under every parent TFM.
        for tfm in parent_tfms:
            expected = f"lib/{tfm}/{bp.assembly_name}.dll"
            if pkg.has_path(expected):
                r.ok(f"bundled '{bp.identity}' [tfm={tfm}] — own binary present at {expected}")
            else:
                r.bad(f"bundled '{bp.identity}' [tfm={tfm}] — own binary missing at {expected}")

        if not check_transitive or bp.csproj_path is None or not bp.csproj_path.is_file():
            continue

        try:
            bp_assets_path = find_assets_file(bp.csproj_path)
            bp_assets = load_all_assets(bp_assets_path)
        except SystemExit as e:
            r.bad(f"bundled '{bp.identity}' — cannot resolve closure: {e}")
            continue

        # Packages with suppressParent=All (PrivateAssets="all") in the bundled
        # project don't flow to consumers and shouldn't be in the closure.
        bp_suppressed = _suppressed_package_ids(bp_assets_path)

        candidate_tfms = bp_assets.tfms
        r.note(f"bundled '{bp.identity}' assets resolved for TFM(s): {', '.join(candidate_tfms)}")
        if bp_suppressed:
            r.note(f"bundled '{bp.identity}' suppressed (PrivateAssets=all): {', '.join(sorted(bp_suppressed))}")

        all_natives: list[ResolvedFile] = []
        for tfm in parent_tfms:
            picked = best_match_tfm(tfm, candidate_tfms)
            if picked is None:
                r.bad(f"bundled '{bp.identity}' [parent_tfm={tfm}] — no matching TFM in bundled assets")
                continue
            if picked != tfm:
                r.note(f"bundled '{bp.identity}' [parent_tfm={tfm}] — using bundled TFM '{picked}' as compatible match")
            graph = bp_assets.graphs[picked]
            # Closure over every package the bundled project depends on, excluding
            # packages suppressed via PrivateAssets="all".
            all_pkg_ids = [k for k in graph.packages.keys() if k not in bp_suppressed]
            closure = graph.closure(all_pkg_ids)
            closure -= bp_suppressed
            result = check_managed_closure_for_tfm(
                parent_tfm=tfm,
                closure_pkg_ids=closure,
                graph=graph,
                public_dep_ids=public_dep_ids,
                pkg=pkg,
            )
            all_natives.extend(result.native_files)
            if result.missing_managed:
                r.bad(
                    f"bundled '{bp.identity}' closure [tfm={tfm}] — missing {len(result.missing_managed)} managed file(s):",
                    result.missing_managed,
                )
            else:
                r.ok(f"bundled '{bp.identity}' closure [tfm={tfm}] — full closure present ({result.checked_managed} file(s))")

        if all_natives:
            count, missing = check_native_files(pkg, all_natives)
            if missing:
                r.bad(f"bundled '{bp.identity}' [native] — missing {len(missing)} RID-scoped file(s):", missing)
            else:
                r.ok(f"bundled '{bp.identity}' [native] — {count} RID-scoped file(s) present")

    # ---- Analyzer projects ----
    for ap in contract.analyzer_projects:
        ap_dep_names = {ap.identity.lower(), ap.assembly_name.lower()}
        leaked = ap_dep_names & pkg.declared_deps
        if leaked:
            r.bad(f"analyzer project '{ap.identity}' is declared as a dependency '{next(iter(leaked))}' (should be hidden)")
        else:
            r.ok(f"analyzer project '{ap.identity}' correctly absent from nuspec dependencies")

        # Check both project name and assembly name for leaked DLLs.
        forbidden = [e for e in pkg.entries if Path(e).name.lower() in
                     {f"{ap.identity}.dll".lower(), f"{ap.assembly_name}.dll".lower()}]
        if forbidden:
            r.bad(f"analyzer project '{ap.identity}' assembly leaked into package", forbidden)
        else:
            r.ok(f"analyzer project '{ap.identity}' assembly correctly absent from package")

    # ---- Normal package references must be declared as deps ----
    for np in contract.normal_packages:
        if np.identity.lower() in pkg.declared_deps:
            r.ok(f"public package dependency '{np.identity}' declared in nuspec")
        else:
            r.bad(
                f"public package '{np.identity}' is NOT in nuspec dependencies — "
                "accidentally flagged Vendor=\"true\" or set PrivateAssets=\"all\"?"
            )


# ---------- spec file ----------

def run_spec_file(pkg: PackageInspection, spec_path: Path, r: Report) -> None:
    if not spec_path.is_file():
        raise SystemExit(f"spec file not found: {spec_path}")
    for raw in spec_path.read_text().splitlines():
        line = raw.split("#", 1)[0].strip()
        if not line:
            continue
        try:
            rule, arg = line.split(None, 1)
        except ValueError:
            r.bad(f"malformed spec line: {raw!r}")
            continue
        if rule == "forbid-dep":
            if arg.lower() in pkg.declared_deps:
                r.bad(f"[spec] dependency '{arg}' should not be declared")
            else:
                r.ok(f"[spec] dependency '{arg}' correctly absent")
        elif rule == "require-file":
            pat = re.compile(arg)
            if any(pat.search(e) for e in pkg.entries):
                r.ok(f"[spec] file matching /{arg}/ is present")
            else:
                r.bad(f"[spec] no file matches required pattern /{arg}/")
        elif rule == "forbid-file":
            pat = re.compile(arg)
            hits = [e for e in pkg.entries if pat.search(e)]
            if hits:
                r.bad(f"[spec] forbidden pattern /{arg}/ matched files", hits)
            else:
                r.ok(f"[spec] no files match forbidden pattern /{arg}/")
        else:
            r.bad(f"[spec] unknown rule: {rule}")


# ---------- pack ----------

def pack_project(csproj_path: Path, output_dir: Path, configuration: str) -> Path:
    """Run `dotnet pack` and return the produced .nupkg (excluding .symbols.nupkg)."""
    if not csproj_path.is_file():
        raise SystemExit(f"csproj not found: {csproj_path}")
    output_dir.mkdir(parents=True, exist_ok=True)

    # Snapshot existing .nupkg files so we can detect what pack produced.
    pre_existing = {p.name for p in output_dir.glob("*.nupkg")}

    cmd = [
        "dotnet", "pack", str(csproj_path),
        "--configuration", configuration,
        "--output", str(output_dir),
        "--nologo",
        "--verbosity", "minimal",
    ]
    print(f"{Color.D}\u00b7 packing: {' '.join(cmd)}{Color.X}")
    result = subprocess.run(cmd, text=True, capture_output=True)
    if result.stdout:
        # Surface dotnet's own output so the user sees what happened.
        sys.stdout.write(result.stdout)
    if result.returncode != 0:
        if result.stderr:
            sys.stderr.write(result.stderr)
        raise SystemExit(f"dotnet pack failed (exit code {result.returncode})")

    produced = [
        p for p in output_dir.glob("*.nupkg")
        if p.name not in pre_existing and not p.name.endswith(".symbols.nupkg")
    ]
    if not produced:
        raise SystemExit(
            f"dotnet pack succeeded but no new .nupkg appeared in {output_dir}. "
            "Is <IsPackable> true and does the project produce a package?"
        )
    if len(produced) > 1:
        # Pick the newest deterministically and inform the user.
        produced.sort(key=lambda p: p.stat().st_mtime, reverse=True)
        print(f"{Color.D}\u00b7 multiple .nupkg produced; using newest: {produced[0].name}{Color.X}")
    return produced[0]


# ---------- main ----------

def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("nupkg", type=Path, nargs="?",
                    help="path to an existing .nupkg to verify; if omitted, --csproj is packed")
    ap.add_argument("--csproj", type=Path,
                    help="csproj of the project to pack/verify (required if nupkg not given)")
    ap.add_argument("--configuration", default="Release",
                    help="dotnet pack configuration when packing (default: Release)")
    ap.add_argument("--output-dir", type=Path,
                    help="directory to pack into (default: a unique temp dir)")
    ap.add_argument("--spec", type=Path, help="optional spec file with extra assertions")
    ap.add_argument("--no-transitive", action="store_true",
                    help="skip transitive closure checks")
    ap.add_argument("--list", action="store_true", help="print package contents")
    args = ap.parse_args()

    if args.nupkg is None and args.csproj is None:
        ap.error("either a positional nupkg path or --csproj must be given")

    r = Report()

    if args.nupkg is not None:
        nupkg_path = args.nupkg
        if args.output_dir is not None:
            r.note("--output-dir is ignored when a positional nupkg is provided")
    else:
        if args.output_dir is not None:
            out_dir = args.output_dir.resolve()
            out_dir.mkdir(parents=True, exist_ok=True)
        else:
            out_dir = Path(tempfile.mkdtemp(prefix="verify-package-"))
        r.note(f"pack output directory: {out_dir}")
        nupkg_path = pack_project(args.csproj, out_dir, args.configuration)
        r.note(f"packed: {nupkg_path}")

    r.note(f"inspecting {nupkg_path}")
    pkg = inspect_package(nupkg_path)

    if pkg.declared_deps:
        r.note(f"nuspec declares {len(pkg.declared_deps)} dependency id(s): {', '.join(sorted(pkg.declared_deps))}")
    else:
        r.note("nuspec declares no dependencies")

    run_universal_checks(pkg, r)

    if args.csproj:
        r.note(f"deriving expectations from {args.csproj}")
        contract = parse_csproj(args.csproj)
        run_csproj_checks(pkg, contract, r, check_transitive=not args.no_transitive)

    if args.spec:
        r.note(f"applying spec: {args.spec}")
        run_spec_file(pkg, args.spec, r)

    if args.list:
        r.note("package contents:")
        for e in pkg.entries:
            print(f"    {e}")

    print()
    if not r.fails:
        print(f"{Color.G}{r.passes} passed, 0 failed{Color.X}")
        return 0
    print(f"{Color.Y}{r.passes} passed, {len(r.fails)} failed{Color.X}")
    return 1


if __name__ == "__main__":
    sys.exit(main())

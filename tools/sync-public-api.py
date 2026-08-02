#!/usr/bin/env python3
"""Bring a project's PublicAPI.Unshipped.txt in line with its public surface.

PublicApiAnalyzers fails the build until every public member is declared in
PublicAPI.Unshipped.txt, which is the point: the file is the reviewable record of
what a package exposes. Writing it by hand for a milestone's worth of new API is
not, so this derives it from the build.

The analyzer's own diagnostics are the source. RS0016 quotes the exact line a
missing member needs; RS0017 quotes a line that no longer matches any symbol,
left behind by a rename or a removal. Both are applied until the build is clean.

Only to the unshipped file. An entry that has gone stale in PublicAPI.Shipped.txt
is a promise a release made and no longer keeps; this reports it and stops,
because removing one belongs in a diff someone read on purpose.

Usage:
    tools/sync-public-api.py src/ArxisStudio.Markup
    tools/sync-public-api.py src/ArxisStudio.Markup.Xaml PublicAPI.Unshipped.txt

Three things this has to get right, each of which cost an afternoon to find:

  * `dotnet format analyzers --diagnostics RS0016` looks like the official way to
    do this and does not work here. It applies one batch of fixes and then stops
    converging, reporting the same remaining diagnostics on every further pass.

  * Building a project builds its ProjectReferences too, and their diagnostics
    land in the same output. Entries are filtered by the project that reported
    them, or a package's members end up declared in a different package's file.

  * A `const`'s signature contains the quoted value, so a regex that stops at the
    first inner quote truncates it. The capture runs to the last quote before the
    analyzer's help URL instead. Anchoring on the URL rather than on any English
    text also keeps this working under a localised toolchain.
"""
import io
import os
import re
import shutil
import subprocess
import sys

MAXIMUM_PASSES = 8

# RS0016: <text> "<signature>" <text> (<help url>) [<reporting project>]
MISSING = re.compile(r'RS0016: [^"]*"(.+)"[^"]*\(https://[^)]*\)\s*\[([^\]]+)\]')

# RS0017 is the mirror image: a declared entry with no symbol behind it any more.
STALE = re.compile(r'RS0017: [^"]*"(.+)"[^"]*\(https://[^)]*\)')


def find_dotnet():
    """Finds the SDK, preferring whatever is on PATH."""
    return shutil.which("dotnet") or os.path.expanduser("~/.dotnet/dotnet")


def build(project):
    """Builds a project and returns everything it printed."""
    env = dict(os.environ, DOTNET_CLI_TELEMETRY_OPTOUT="1", DOTNET_NOLOGO="1")
    result = subprocess.run(
        [find_dotnet(), "build", project, "-c", "Release"],
        capture_output=True, text=True, env=env, timeout=600)

    return result.stdout + result.stderr


def sync(project, api_file):
    """Applies the analyzer's own diagnostics until the build stops reporting them."""
    target = os.path.basename(os.path.abspath(project)) + ".csproj"

    for attempt in range(1, MAXIMUM_PASSES):
        output = build(project)

        missing = sorted({
            match.group(1) for match in MISSING.finditer(output)
            if os.path.basename(match.group(2).strip()) == target
        })
        stale = {match.group(1) for match in STALE.finditer(output)}

        if not missing and not stale:
            print("%s: clean after %d pass(es)" % (project, attempt - 1))

            return True

        existing = io.open(api_file, encoding="utf-8").read().splitlines()
        header = [line for line in existing if line.startswith("#")] or ["#nullable enable"]
        declared = set(line for line in existing if line and not line.startswith("#"))

        # A stale entry that is not in this file is in PublicAPI.Shipped.txt, and taking it out
        # of there is removing something a release promised. That is a decision to make on
        # purpose, in a diff someone reads — not a thing for this to do while converging.
        shipped_stale = sorted(stale - declared)

        if shipped_stale:
            print(
                "%s: declared as shipped, but nothing answers to it any more:\n  %s\n"
                "Removing one of these is a breaking change. Edit PublicAPI.Shipped.txt by hand."
                % (project, "\n  ".join(shipped_stale)),
                file=sys.stderr)

            return False

        io.open(api_file, "w", encoding="utf-8").write(
            "\n".join(header + sorted((declared | set(missing)) - stale)) + "\n")

        print("pass %d: +%d added, -%d stale" % (attempt, len(missing), len(stale)))

    print("%s: did not converge in %d passes" % (project, MAXIMUM_PASSES), file=sys.stderr)

    return False


def main(arguments):
    if not arguments:
        print(__doc__, file=sys.stderr)

        return 2

    project = arguments[0].rstrip("/")
    api_file = arguments[1] if len(arguments) > 1 else os.path.join(project, "PublicAPI.Unshipped.txt")

    if not os.path.exists(api_file):
        io.open(api_file, "w", encoding="utf-8").write("#nullable enable\n")

    return 0 if sync(project, api_file) else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))

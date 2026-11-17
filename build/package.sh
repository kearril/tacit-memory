#!/usr/bin/env bash
# Builds the release package for one runtime: a self-contained publish with the license files, archived, with its
# SHA-256.
#
#   build/package.sh <runtime> <output>
#
# Publishes src/TacitMemory.Cli self-contained for <runtime> (win-x64, osx-arm64 or linux-x64) into <output>/tacit,
# writes THIRD-PARTY-NOTICES.txt beside the executable and copies LICENSE and NOTICE there, then archives the folder's
# contents as tacit-<version>-<runtime>.zip on Windows or .tar.gz elsewhere, which keeps the executable bit, with a
# .sha256 file beside it. <output> is emptied first. The version is the one the built executable reports, which MinVer
# takes from the git tags, so the package is built on the platform it is for: macOS also signs the executable there,
# without which Apple silicon will not run it.
#
# Prints folder=, archive= and version= lines, ready for $GITHUB_OUTPUT; the tools' own output goes to stderr. Needs
# bash 3.2 or later, the .NET 11 SDK, Bun, jq and, on Windows, Git Bash.
set -euo pipefail

if [ $# -ne 2 ]; then
    echo "usage: build/package.sh <runtime> <output>" >&2
    exit 2
fi
runtime=$1
case $runtime in
    win-x64) executable=tacit.exe vector=vec0.dll ;;
    osx-arm64) executable=tacit vector=vec0.dylib ;;
    linux-x64) executable=tacit vector=vec0.so ;;
    *) echo "unknown runtime '$runtime': win-x64, osx-arm64 or linux-x64" >&2; exit 2 ;;
esac

# Git Bash hands Windows programs converted paths, but a path printed for a workflow step must be one Windows reads.
windows=false
if command -v cygpath > /dev/null; then windows=true; fi
native() { if $windows; then cygpath -m "$1"; else printf '%s\n' "$1"; fi; }

root=$(cd "$(dirname "$0")/.." && pwd)
mkdir -p "$2"
output=$(cd "$2" && pwd)
rm -rf "${output:?}"/*
publish=$output/tacit
mkdir -p "$publish"

dotnet publish "$root/src/TacitMemory.Cli" -c Release -r "$runtime" --self-contained true -o "$publish" -nologo >&2

# The notices list what the package carries: the NuGet packages of the CLI project with their whole dependency graph,
# the web packages the frontend build recorded, and the parts that come from neither. Only licenses whose full text the
# notices carry are allowed (SQLite's blessing is a public-domain dedication and has none to carry).
nuget_json=$output/nuget-licenses.json
# dotnet finds the local tool manifest from the working directory, so the tool runs from the repository root.
(
    cd "$root"
    dotnet tool restore > /dev/null
    dotnet tool run nuget-license \
        -i "$root/src/TacitMemory.Cli/TacitMemory.Cli.csproj" \
        -t \
        -a 'MIT;Apache-2.0;blessing' \
        -ignore 'MinVer' \
        -override "$root/build/nuget-license-overrides.json" \
        -o Json \
        -fo "$nuget_json"
) >&2

sqlite_vec_mit=$root/lib/sqlite-vec/LICENSE-MIT
if ! grep -q 'Permission is hereby granted' "$sqlite_vec_mit"; then
    echo "lib/sqlite-vec/LICENSE-MIT no longer holds the MIT permission text." >&2
    exit 1
fi

# jq on Windows may end lines with CR LF; the notices use LF only.
jq_text() { jq "$@" | tr -d '\r'; }
heading() { printf '\n%s\n' "$1"; printf '%*s\n' "${#1}" '' | tr ' ' '='; printf '\n'; }
trim_end() { sed -e :a -e '/^[[:space:]]*$/{$d;N;ba' -e '}' "$1"; }

{
    printf 'TacitMemory third-party notices\n\n'
    printf 'TacitMemory is licensed under the Apache License 2.0 (see LICENSE). It ships with the components listed below,\n'
    printf 'each under its own license. The full text of each license follows the lists.\n'

    heading '.NET runtime and ASP.NET Core'
    printf 'The package is self-contained: it carries the .NET runtime and ASP.NET Core.\n'
    printf 'License: MIT. Copyright (c) .NET Foundation and Contributors.\n'
    printf 'https://github.com/dotnet/runtime\n'
    printf 'https://github.com/dotnet/aspnetcore\n'
    printf 'Their own third-party notices: https://github.com/dotnet/runtime/blob/main/THIRD-PARTY-NOTICES.TXT and\n'
    printf 'https://github.com/dotnet/aspnetcore/blob/main/THIRD-PARTY-NOTICES.txt\n'

    heading "sqlite-vec ($vector)"
    printf 'Version 0.1.9. Dual licensed under MIT or Apache-2.0; used here under MIT.\n'
    printf 'https://github.com/asg017/sqlite-vec\n\n'
    trim_end "$sqlite_vec_mit"

    heading 'NuGet packages'
    jq_text -r 'sort_by(.PackageId | ascii_downcase)[]
        | "\(.PackageId) \(.PackageVersion)", "  License: \(.License)",
          (.Copyright // empty | select(. != "") | "  \(.)"),
          (.PackageProjectUrl // empty | select(. != "") | "  \(.)")' "$nuget_json"

    heading 'Web interface packages'
    jq_text -r 'sort_by(.name | ascii_downcase)[]
        | "\(.name) \(.version)", "  License: \(.license)", (.homepage // empty | select(. != "") | "  \(.)")' \
        "$root/web/licenses/third-party.json"

    heading 'License texts of the web interface packages'
    jq_text -r 'def trim_end: sub("\\s+$"; "");
        sort_by(.name | ascii_downcase)[]
        | "---- \(.name) \(.version) ----", "",
          (if (.licenseText // "") != "" then .licenseText | trim_end
           elif .license == "MIT" or .license == "Apache-2.0"
           then "\(.license); the package ships no license file, so the \(.license) text at the end applies."
           else error("\(.name) is under \(.license) but ships no license text to include.") end),
          (if (.noticeText // "") != "" then "", (.noticeText | trim_end) else empty end),
          ""' "$root/web/licenses/third-party.json"

    heading 'MIT License'
    printf 'Applies to the packages above listed under MIT, with the copyright statement given beside each.\n\n'
    # The MIT text without its title and copyright line, for the packages whose metadata carries only a copyright statement.
    sed -n '/Permission is hereby granted/,$p' "$sqlite_vec_mit" | trim_end /dev/stdin

    heading 'Apache License 2.0'
    printf 'Applies to the packages above listed under Apache-2.0.\n\n'
    awk 'started { print; next } /[^[:space:]]/ { sub(/^[[:space:]]+/, ""); started = 1; print }' "$root/LICENSE" |
        trim_end /dev/stdin
} > "$publish/THIRD-PARTY-NOTICES.txt"

cp "$root/LICENSE" "$root/NOTICE" "$publish/"
rm "$nuget_json"

version=$("$publish/$executable" --version | tr -d '\r')
name=tacit-$version-$runtime
if $windows; then
    # Windows' own bsdtar writes zip; the tar that comes with Git Bash cannot.
    archive=$output/$name.zip
    (cd "$publish" && "$(cygpath -u "$SYSTEMROOT")/System32/tar.exe" -a -c -f "$(cygpath -w "$archive")" -- *)
else
    archive=$output/$name.tar.gz
    (cd "$publish" && tar -czf "$archive" -- *)
fi

if command -v sha256sum > /dev/null; then
    hash=$(sha256sum "$archive" | cut -d ' ' -f 1)
else
    hash=$(shasum -a 256 "$archive" | cut -d ' ' -f 1)
fi
printf '%s  %s\n' "$hash" "$(basename "$archive")" > "$archive.sha256"

printf 'folder=%s\narchive=%s\nversion=%s\n' "$(native "$publish")" "$(native "$archive")" "$version"

#!/usr/bin/env bash
# Runs every R&T behaviour suite without RimWorld, Unity, Harmony or a game install.
#
# The suites exist because the 0.7 rules layers (Integration/, Placement/, Sizing/, Economy/) are
# deliberately dependency-free: no Find, no Harmony, no Unity, no TechLevel. That is what lets them
# be compiled against the hand-written doubles in RimWorldStubs.cs and run anywhere mono exists.
# Anything that genuinely needs the game is not tested here and is not pretended to be.
#
#   Ubuntu/WSL:  sudo apt-get install -y mono-mcs mono-runtime
#   Then:        Tests/run-tests.sh
#
# Exits non-zero if any suite fails to build or fails an assertion. Each binary is removed before
# its build so a compile failure can never be masked by a stale binary reporting a stale pass —
# that has happened here and produced a green run from a build that did not occur.
set -u

cd "$(dirname "$0")/.." || exit 1

SRC=Source
OUT="${TMPDIR:-/tmp}/rt-tests"
mkdir -p "$OUT"

MCS_FLAGS="-target:exe -langversion:latest -nowarn:0169,0414,0649,0219"
failures=0

run_suite() {
    name=$1; shift
    binary="$OUT/$name.exe"
    rm -f "$binary"

    if ! mcs $MCS_FLAGS -out:"$binary" "$@"; then
        echo "BUILD FAILED: $name"
        failures=$((failures + 1))
        return
    fi

    if ! mono "$binary"; then
        failures=$((failures + 1))
    fi
}

run_suite integration \
    Tests/RimWorldStubs.cs Tests/IntegrationTests.cs \
    $SRC/Integration/*.cs

run_suite placement \
    Tests/RimWorldStubs.cs Tests/PlacementTests.cs \
    $SRC/Integration/*.cs $SRC/Placement/*.cs

# 0.8 outpost-seeding rules: tier -> allowance and terrain -> archetype. Both pure, so this needs
# only the Sizing tables and the WorldObjectKind enum they read.
run_suite outpostrules \
    Tests/RimWorldStubs.cs Tests/OutpostRulesTests.cs \
    $SRC/Integration/WorldObjectKind.cs $SRC/Sizing/*.cs

# 0.8 demographics core: the deterministic per-tile seed + RNG + weighted picking. Pure, no game.
run_suite demographics \
    Tests/DemographicsRulesTests.cs \
    $SRC/Demographics/DemographicsRules.cs

run_suite resource \
    Tests/RimWorldStubs.cs Tests/ResourceTests.cs \
    $SRC/Integration/*.cs $SRC/Economy/*.cs

# The residency suite moved to the Living World companion mod (0.8) along with the residency code it
# exercises — see LivingWorld/Tests/run-tests.sh.

# Not a suite: a type-check over the impure files that cannot be behaviour-tested without a running
# game, but whose signatures can still be held to the shapes they will really meet. This is what
# catches a patch calling a method that no longer exists — the failure mode a mod cannot see until
# the game loads it.
echo
echo "== type-check (impure files, signatures only) =="
rm -f "$OUT/typecheck.dll"
if mcs -target:library -langversion:latest -nowarn:0169,0414,0649,0219,0067 -out:"$OUT/typecheck.dll" \
    Tests/RimWorldStubs.cs Tests/RimWorldStubsExt.cs \
    $SRC/Integration/*.cs $SRC/Placement/*.cs $SRC/Economy/*.cs \
    $SRC/WorldObjectPlacementUtility.cs $SRC/OutpostPlacementUtility.cs \
    $SRC/RegionalOwnershipUtility.cs \
    $SRC/GeographicProvince.cs $SRC/IRegionDemographicProvider.cs \
    $SRC/ProvinceAdjacency.cs \
    $SRC/Patches/RegionsAndTerritories_EmpiresPatch.cs \
    $SRC/Patches/Patch_TileFinder_IsValidTileForNewSettlement.cs \
    $SRC/Patches/Patch_WorldInspectPane_TileInspectString.cs; then
    echo "  type-check clean"
else
    echo "  TYPE-CHECK FAILED"
    failures=$((failures + 1))
fi

echo
if [ "$failures" -eq 0 ]; then
    echo "ALL SUITES PASSED"
    exit 0
fi

echo "$failures SUITE(S) FAILED"
exit 1

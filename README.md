# Regions and Societies (Core, Map Mode Framework edition)
A comprehensive layer for creating world population and resource calculations

## Roadmap

Where the mod is and where it's headed — describe the people, let them live and change,
refine what they are, then run an economy on them. Detail lives in the
[issue tracker](https://github.com/Regions-and-societies/Core-MMF/issues) and
[milestones](https://github.com/Regions-and-societies/Core-MMF/milestones).

![Regions and Societies development roadmap](About/Roadmap.png)

## Release provenance

Every release ships `Assemblies/CHECKSUMS.sha256`, generated from the final release build
by `harness/release-manifest.ps1` — run after the last compile and before the tag, and
committed on the release branch so the tag carries it. `harness/verify-binaries.ps1`
verifies any copy of the mod (repo or deployed folder) against that manifest and must pass
clean at cut time. Never generate the manifest retroactively: a manifest written from a
dev build is a fabricated record. See issue #4.

# Demographic model — calibration simulator

A standalone Node port of the locked demographic model (see `../DEMOGRAPHIC_MODEL.md`). It exists to calibrate
weights and see the emergent shapes before/while the C# is built — it is NOT shipped in the mod.

- `graph.js` — the model: the per-cohort influence graph step, the three suppression mechanisms, wealth
  (income/cost/assets), health vitals, geographic scale + food, the added indicators, and reproduction/inheritance.
- `run.js` — scenarios (Empire, tribal, pirate, no-Biotech, two conquests) over ~2/10/100-yr horizons; emits Markdown.
- `sample-output.md` — a captured run.

Run: `node run.js > out.md` (Node 18+; no dependencies). Native tick = one demographic year.
Weights are first-pass; the STRUCTURE is locked. Tune here, then port the tuned values to the Defs.

'use strict';
// Demographic influence graph — standalone calibration simulator (v2 design).
// Native tick = one demographic year. Horizons reported at ~2 yr (short; 100 days = 1.7 yr),
// 10 yr, 100 yr. Pure: no game. Purpose = see the SHAPES and find bad weights before any C#.

// ---------- helpers ----------
const clamp = (x, lo, hi) => Math.max(lo, Math.min(hi, x));
const sum = a => a.reduce((s, v) => s + v, 0);
function normalize(dist) { const t = sum(Object.values(dist)); if (t <= 0) return dist; for (const k in dist) dist[k] /= t; return dist; }
const EDU_TIERS = ['Illiterate','Primary','Secondary','Undergrad','Postgrad'];
// tilt a base education pyramid up (access>0.5) or down (<0.5) toward higher tiers, renormalised
function shiftedPyramid(base, access) {
  const k = (access - 0.5) * 4;                          // -2..+2
  const out = {}; let s = 0;
  EDU_TIERS.forEach((t, i) => { const w = Math.max(1e-4, (base[t] ?? 0.01)) * Math.exp(k * (i - 2) / 2); out[t] = w; s += w; });
  EDU_TIERS.forEach(t => out[t] /= s);
  return out;
}
function normTriple(e, m, u) { const s = e + m + u; return { Elite: e/s, Middle: m/s, Underclass: u/s }; }
function curve(kind, x, threshold) {
  if (kind === 'Saturating') return x <= 0 ? x : x / (1 + x);          // diminishing past ~0.5
  if (kind === 'Threshold')  return x < (threshold ?? 0.5) ? 0 : x;    // no effect until threshold
  return x;                                                            // Linear
}

// ---------- factor definitions (region-level) ----------
// Each mutable factor: velocity, inertia, deadband, releaseBand, maxStep, and inbound edges.
// A distribution factor's edges name a targetTier (which slice to push); scalar edges push the level.
// curve/threshold/lagYears/mode optional. mode: Stress(default) | Ceiling | Suppress.
const F = {
  Wealth: {
    kind: 'scalar', velocity: 0.5, inertia: 0.2, deadband: 0.02, releaseBand: 0.01, maxStep: 0.10,
    edges: [
      ['EmploymentRate', null, 0.12],
      ['Sector', 'Manufacturing', 0.10, 'Saturating'],
      ['Sector', 'Services', 0.06, 'Saturating'],
      ['Education', 'Secondary', 0.10, 'Linear', null, 5],
      ['Balance', null, 0.12],
      ['DrugBurdenAgg', null, -0.55],          // income diverted to the dependency
      ['Roads', null, 0.05],
      ['Conflict', null, -0.40],
      ['BiomeFertility', null, 0.04],
      ['Crime', null, -0.25],                  // crime destroys and diverts wealth
      ['Health', null, 0.06],                  // a healthy workforce is a productive one
    ],
  },
  Urbanisation: {
    kind: 'scalar', velocity: 0.35, inertia: 0.3, deadband: 0.02, releaseBand: 0.01, maxStep: 0.06,
    edges: [
      ['Wealth', null, 0.30],
      ['Sector', 'Manufacturing', 0.30],
      ['Sector', 'Services', 0.30],
      ['Roads', null, 0.20],
      ['Sector', 'Agriculture', -0.30],
    ],
  },
  EmploymentRate: {
    kind: 'scalar', velocity: 0.5, inertia: 0.2, deadband: 0.02, releaseBand: 0.01, maxStep: 0.10,
    edges: [
      ['Education', 'Secondary', 0.40],
      ['Balance', null, 0.30],
      ['Wealth', null, 0.20],
      ['Roads', null, 0.20],
      ['DrugBurdenAgg', null, 0.30],           // coerced participation — cannot drop out
      ['Conflict', null, -0.30],
      ['Health', null, 0.25],                  // a sick population works less
      ['Crime', null, -0.25],                  // crime displaces legitimate work
    ],
  },
  Health: {
    kind: 'scalar', velocity: 0.3, inertia: 0.4, deadband: 0.02, releaseBand: 0.01, maxStep: 0.06,
    edges: [
      ['Wealth', null, 0.30],
      ['Education', 'Undergrad', 0.30],        // medical expertise (the Undergrad+ economic role)
      ['Sector', 'Services', 0.20],            // healthcare sub-sector proxy
      ['FoodSelfSuff', null, 0.35],            // nutrition — the new food/carrying-capacity read
      ['DrugBurdenAgg', null, -0.25],
      ['Crime', null, -0.20],
      ['Conflict', null, -0.30],
      ['Urbanisation', null, -0.10],           // density spreads disease (mild)
    ],
  },
  Crime: {
    kind: 'scalar', velocity: 0.35, inertia: 0.4, deadband: 0.02, releaseBand: 0.01, maxStep: 0.08,
    edges: [
      ['Balance', null, -0.45],                // the payoff: a hollowed middle breeds crime
      ['Inequality', null, 0.40],              // a polarised SES (extremes over a thin middle)
      ['SES', 'Subsistence', 0.35],            // poverty / desperation
      ['EmploymentRate', null, -0.30],         // idle hands
      ['DrugBurdenAgg', null, 0.30],           // crime to fund the dependency
      ['Education', 'Secondary', -0.25],       // an educated, skilled middle
      ['FactionRigidity', null, 0.20],         // a rigid, unjust order
      ['Conflict', null, 0.30],
    ],
  },
  Education: {
    kind: 'dist', tiers: ['Illiterate','Primary','Secondary','Undergrad','Postgrad'],
    velocity: 0.08, inertia: 0.6, deadband: 0.04, releaseBand: 0.02, maxStep: 0.03,
    edges: [
      ['Wealth', null, 0.30, 'Linear', null, 0, 'Secondary'],
      ['Urbanisation', null, 0.30, 'Linear', null, 0, 'Secondary'],
      ['Urbanisation', null, 0.20, 'Linear', null, 0, 'Undergrad'],
      ['Strata', 'Underclass', 0.40, 'Linear', null, 0, 'Illiterate'],
      ['Strata', 'Underclass', 0.30, 'Linear', null, 0, 'Primary'],
      ['Strata', 'Middle', 0.50, 'Linear', null, 0, 'Secondary'],
      ['Strata', 'Elite', 0.30, 'Linear', null, 0, 'Undergrad'],
      ['Strata', 'Elite', 0.20, 'Linear', null, 0, 'Postgrad'],
      ['Conflict', null, -0.20, 'Linear', null, 0, 'Secondary'],
      // slavery ceiling: enslaved share held at Primary (cumulative Secondary+ bounded)
      ['SlaveryEnslavedShare', null, 1.0, 'Linear', null, 0, 'Secondary', 'Ceiling', 'Primary'],
    ],
  },
  SES: {
    kind: 'dist', tiers: ['Subsistence','Modest','Prosperous','Affluent'],
    velocity: 0.4, inertia: 0.3, deadband: 0.02, releaseBand: 0.01, maxStep: 0.08,
    edges: [
      ['Wealth', null, 0.50, 'Linear', null, 0, 'Prosperous'],
      ['Wealth', null, -0.40, 'Linear', null, 0, 'Subsistence'],
      ['Strata', 'Underclass', 0.50, 'Linear', null, 0, 'Subsistence'],
      ['Strata', 'Middle', 0.30, 'Linear', null, 0, 'Modest'],
      ['Strata', 'Middle', 0.30, 'Linear', null, 0, 'Prosperous'],
      ['Strata', 'Elite', 0.50, 'Linear', null, 0, 'Affluent'],
      ['DrugBurdenAgg', null, 0.30, 'Linear', null, 0, 'Subsistence'],  // net poorer despite working
    ],
  },
  Strata: {
    kind: 'dist', tiers: ['Elite','Middle','Underclass'],
    velocity: 0.06, inertia: 0.7, deadband: 0.04, releaseBand: 0.02, maxStep: 0.03,
    edges: [
      ['FactionRigidity', null, 0.50, 'Linear', null, 0, 'Elite'],
      ['FactionRigidity', null, 0.50, 'Linear', null, 0, 'Underclass'],
      ['FactionRigidity', null, -0.60, 'Linear', null, 0, 'Middle'],
      ['Education', 'Secondary', 0.40, 'Linear', null, 5, 'Middle'],
      ['Education', 'Postgrad', 0.20, 'Saturating', null, 5, 'Elite'],
      ['Wealth', null, 0.30, 'Saturating', null, 0, 'Middle'],
      ['Urbanisation', null, 0.20, 'Linear', null, 0, 'Middle'],
      ['Conflict', null, -0.20, 'Linear', null, 0, 'Middle'],
      ['MigrationAgg', null, 0.15, 'Linear', null, 0, 'Middle'],
    ],
  },
  Sector: {
    kind: 'dist', tiers: ['Agriculture','Extraction','Manufacturing','Services','Military','Public'],
    velocity: 0.4, inertia: 0.3, deadband: 0.02, releaseBand: 0.01, maxStep: 0.06,
    edges: [
      ['Balance', null, 0.30, 'Threshold', 0.20, 0, 'Manufacturing'],
      ['Urbanisation', null, 0.30, 'Linear', null, 0, 'Services'],
      ['BiomeFertility', null, 0.40, 'Linear', null, 0, 'Agriculture'],
      ['Education', 'Undergrad', 0.20, 'Linear', null, 0, 'Manufacturing'],
      ['FactionRigidity', null, 0.30, 'Linear', null, 0, 'Public'],   // rigid states are bureaucratic
      ['Conflict', null, 0.40, 'Linear', null, 0, 'Military'],
    ],
  },
};

// Derived region-level factors (no state; recomputed each step).
const DERIVED = {
  Balance: r => clamp(2.2 * r.Strata.Middle * (r.Education.Secondary + r.Education.Undergrad), 0, 1),
  DrugBurdenAgg: r => sum(r.xenos.map(x => x.share * x.drugBurden)),
  MigrationAgg: r => sum(r.xenos.map(x => x.share * x.migNet)),
  SlaveryEnslavedShare: r => r.SlaveryStance * r.Strata.Underclass, // enslaved = underclass where slavery accepted
  // food nutrition read for Health: 2x+ self-sufficiency = well fed, 1x = break-even, <0.5x = malnourished
  FoodSelfSuff: r => clamp(foodCapacity(r) / Math.max(1, r.Population) / 2, 0, 1),
  // SES polarisation: extremes (Subsistence+Affluent) over a thin middle (Modest+Prosperous)
  Inequality: r => clamp((r.SES.Subsistence + r.SES.Affluent) - (r.SES.Modest + r.SES.Prosperous) + 0.4, 0, 1),
};

// Context region-level (set at seed, some decay).
const CONTEXT = ['FactionRigidity','SlaveryStance','Conflict','Roads','BiomeFertility'];

// ---------- geographic scale (derive real area from world size; feed density + food) ----------
// RimWorld's "planet" framing is not physically self-consistent (a full world is ~100k tiles yet the
// rendered map is only 275x275 m). We DECOUPLE: total tiles come from the real world (TilesCount /
// coverage), but per-tile physical scale is a documented, tunable constant chosen so population density
// and agricultural carrying capacity come out playable. Default ~4 km^2/tile (~2 km across) — the size
// at which the game's ~450-per-tile settlement cap can actually feed itself at realistic yields.
const GEO = {
  km2PerTile: 4.0,
  arableFraction: fert => clamp(0.05 + 0.35 * fert, 0.02, 0.45),      // poor biome ~5% arable, rich ~40%
  // people one km^2 of arable land can feed per year — subsistence farming feeds far fewer than industrial.
  peoplePerArableKm2: r => 50 + 450 * clamp(r.Education.Secondary + r.Education.Undergrad + r.Education.Postgrad, 0, 1),
};
function regionAreaKm2(r) { return (r.geo?.tiles || 1) * GEO.km2PerTile; }
function foodCapacity(r) { return regionAreaKm2(r) * GEO.arableFraction(r.BiomeFertility) * GEO.peoplePerArableKm2(r); }
function carryingCapacity(r) {
  // land feeds its own people; trade access lets a region exceed local food (import), imports capped.
  const local = foodCapacity(r);
  const importMult = 1 + 0.8 * r.Roads * (r.Sector?.Services || 0);   // roads + a trade/services sector
  return Math.max(50, local * importMult);
}
function geoReport(r) {
  const area = regionAreaKm2(r), food = foodCapacity(r), K = carryingCapacity(r);
  return { areaKm2: area, food: Math.round(food), K: Math.round(K), density: r.Population / area, selfSuff: food / Math.max(1, r.Population) };
}

// ---------- one demographic-year step ----------
function readSource(r, name, tier) {
  if (name in DERIVED) return DERIVED[name](r);
  const v = r[name];
  if (typeof v === 'number') return v;
  if (v && tier != null) return v[tier];
  return 0;
}
function readSourceLagged(r, name, tier, lag) {
  if (lag && r.history.length >= lag) { const past = r.history[r.history.length - lag]; const pv = past[name]; if (pv != null) return (tier != null && typeof pv === 'object') ? pv[tier] : pv; }
  return readSource(r, name, tier);
}

function stepScalar(r, key) {
  const def = F[key]; let target = r.baseline[key];
  for (const [src, tier, w, cv, th, lag] of def.edges) {
    const raw = readSourceLagged(r, src, tier, lag || 0);
    target += w * curve(cv || 'Linear', raw, th);
  }
  target = clamp(target, 0, 1);
  const gap = target - r[key];
  const st = r.state[key];
  st.moving = st.moving ? Math.abs(gap) > def.releaseBand : Math.abs(gap) > def.deadband;
  st.v = def.inertia * st.v + def.velocity * gap;
  const step = st.moving ? clamp(st.v, -def.maxStep, def.maxStep) : 0;
  r[key] = clamp(r[key] + step, 0, 1);
  r._t[key] = target;
}

function stepDist(r, key) {
  const def = F[key]; const push = {}; def.tiers.forEach(t => push[t] = 0);
  const ceilings = [];
  for (const e of def.edges) {
    const [src, tier, w, cv, th, lag, targetTier, mode, spill] = e;
    if (mode === 'Ceiling') { ceilings.push({ from: targetTier, bound: 1 - w * readSource(r, src, tier), spill }); continue; }
    const raw = readSourceLagged(r, src, tier, lag || 0);
    const tt = targetTier || def.tiers[0];
    push[tt] += w * curve(cv || 'Linear', raw, th);
  }
  // target = current baseline shifted by net push, renormalized; chase per tier with shared hysteresis.
  const target = {}; def.tiers.forEach(t => target[t] = clamp(r.baseline[key][t] + push[t], 0, 1)); normalize(target);
  const st = r.state[key]; let maxGap = 0; def.tiers.forEach(t => maxGap = Math.max(maxGap, Math.abs(target[t] - r[key][t])));
  st.moving = st.moving ? maxGap > def.releaseBand : maxGap > def.deadband;
  if (st.moving) {
    def.tiers.forEach(t => {
      const gap = target[t] - r[key][t];
      st.v[t] = def.inertia * st.v[t] + def.velocity * gap;
      r[key][t] = clamp(r[key][t] + clamp(st.v[t], -def.maxStep, def.maxStep), 0, 1);
    });
    normalize(r[key]);
  }
  // ceilings: enforce cumulative share at/above `from` <= bound; spill the excess down.
  for (const c of ceilings) {
    const idx = def.tiers.indexOf(c.from); if (idx < 0) continue;
    let above = 0; for (let i = idx; i < def.tiers.length; i++) above += r[key][def.tiers[i]];
    if (above > c.bound && above > 0) {
      const scale = c.bound / above, excess = above - c.bound;
      for (let i = idx; i < def.tiers.length; i++) r[key][def.tiers[i]] *= scale;
      r[key][c.spill] += excess;
    }
  }
  r._t[key] = target;
}

// Per-xenotype sub-layer: standing (from surrounding ideology), drug burden, birth, migration, fight/flight.
function stepXenos(r) {
  // --- region INFRASTRUCTURE (shared stage): city development level -> sanitation coverage; wealth = purity ---
  // dev level 0-5 from urbanisation + wealth (a settlement-development tier). Water treatment REACH scales with it:
  // L0/1 untreated well ~0, L2 settlement core, L3 radius 1, L4 radius 3, L5 whole region. Purity = wealth.
  r.devLevel = clamp(0.4 * r.Urbanisation + 0.6 * r.Wealth, 0, 1) * 5;
  const coverage = clamp((r.devLevel - 1) / 4, 0, 1);       // L1->0, L3->0.5, L5->1.0
  r.sanitation = coverage * r.Wealth;                        // treated share x purity -> disease suppression
  r.Pollution = r.Pollution ?? 0.1;                          // environmental quality (Biotech pollution etc.)
  for (const x of r.xenos) {
    // dynamic per-faction acceptance drift: a xenotype grows more accepted the more common it becomes
    // (familiarity), pulled from its initial ideological standing. Slow (ideology-paced).
    const familiar = (x.baseInit ?? x.basePreference) + 0.5 * clamp(x.share * 2 - 0.2, -0.3, 0.5);
    x.basePreference += 0.05 * (familiar - x.basePreference);
    x.standing = clamp(x.basePreference + r.ideoTolerance, -1, 1);       // contextual: tolerant surround lifts it
    x.birthMult = clamp(1 + 0.95 * x.standing, 0.05, 1);                 // hated -> ~0.05 (floor), preferred -> 1
    // birth target: age + natalism + xeno fertility - wealth - education - conflict, then standing-suppressed
    let birth = 0.02 + 0.40 * r.baseline.ageWorking + 0.30 * r.natalism + 0.30 * x.fertility
      - 0.20 * curve('Saturating', r.Wealth) - 0.20 * r.Education.Undergrad - 0.15 * r.Conflict
      + 0.15 * r.Strata.Underclass;
    x.birth = clamp(birth, 0, 0.06) * x.birthMult;   // per working adult per yr; logistic cap applied in moveStocks
    x.fight  = clamp(0.6 * x.drugBurden + 0.5 * r.Sector.Public + 0.7 * r.Sector.Military + 0.3 * x.standing + 0.3 * r.Strata.Elite, 0, 1.5);
    x.flight = clamp(-0.5 * x.standing + 0.3 * r.Wealth - 0.5 * x.drugBurden + 0.2 * r.baseline.ageWorking + 0.4 * r.Conflict + 0.3 * r.Crime, 0, 1.5);
    // steady net migration: out driven by flight, damped by drug lock-in; small in from opportunity
    const out = 0.03 * x.flight * (1 - x.drugBurden);
    const inc = 0.02 * r.Wealth * (x.standing > 0 ? 1 : 0.3);
    x.migNet = inc - out;
    computeVitals(r, x);
  }
}

// Per-cohort computation, in dependency order: (1) socioeconomics -> (2) per-cohort health access + crime
// -> (3) vital statistics. Everything here is PER XENOTYPE; the region rows are aggregates of these.
function computeVitals(r, x) {
  const food = DERIVED.FoodSelfSuff(r);                   // 0.5 = break-even, <0.5 = malnourished
  const lifespan = x.lifespan || 80;

  // --- (1) SOCIOECONOMICS: this cohort's OWN wealth, education, slave share ---
  // region education attainment 0..1 (Illiterate..Postgrad)
  const eduAtt = r.Education.Primary*0.25 + r.Education.Secondary*0.5 + r.Education.Undergrad*0.75 + r.Education.Postgrad*1;
  const standFac = (x.standing + 1) / 2;                          // -1..1 -> 0..1
  // wealth is stratified by xenotype: standing lifts it, drug dependency drains it, slaves floored
  x.wealth = clamp(r.Wealth * (0.45 + 0.55 * standFac) * (1 - 0.55 * x.drugBurden), 0, 1);
  x.eduIndex = clamp(eduAtt * (0.55 + 0.45 * standFac), 0, 1);
  // slave share WITHIN this xenotype: only where slavery is accepted; hated & drug-dependent enslaved most
  x.slaveShare = r.SlaveryStance * clamp(0.05 + 0.60 * Math.max(0, -x.standing) + 0.25 * x.drugBurden, 0, 0.9);
  // slaves are held down: their own wealth/education floored (the ceiling mechanism, per cohort)
  if (x.slaveShare > 0) { x.freeWealth = x.wealth; x.wealth *= (1 - 0.6 * x.slaveShare); x.eduIndex *= (1 - 0.5 * x.slaveShare); }

  // --- wealth decomposed: income (from a SECTOR source) - cost of living -> assets (silver) ---
  const P = r.Wealth;                                          // regional prosperity = the expectations driver
  const stand = (x.standing + 1) / 2, emp = r.EmploymentRate;
  // income source: which sector this cohort's labour sits in, by education/standing (the forestry point)
  let source, wageMult;
  if (x.eduIndex > 0.55)      { source = 'services/public'; wageMult = 1.45; }   // clerks, admins, medics
  else if (x.eduIndex > 0.38) { source = 'manufacturing';   wageMult = 1.10; }   // skilled trades
  else if (x.drugBurden > 0 || stand < 0.4) { source = 'extraction/labour'; wageMult = 0.75; }
  else                        { source = 'agriculture';     wageMult = 0.90; }   // e.g. forestry, farming
  x.incomeSource = source;
  x.income = (2 + 16 * x.eduIndex) * wageMult * emp * (0.6 + 0.4 * stand) * (1 - 0.4 * x.slaveShare); // silver/day
  // cost of living: subsistence + expectations treadmill (rises with regional wealth) + role + education + drug
  const rolePremium = stand > 0.72 ? 3 * P : 0;               // nobles / royalty / clergy keep up appearances
  x.costLiving = (1.5 + 4 * P) * (0.7 + 0.5 * x.eduIndex) + rolePremium + x.drugBurden * 4;
  x.netDaily = x.income - x.costLiving;
  x.assets = Math.max(0, (x.assets || 0) + x.netDaily * 30);  // ~a month's net accrues per year; no debt (floor 0)

  // --- EDUCATION distribution PER XENOTYPE (each cohort schools differently) ---
  // schooling access: preferred/high-standing get more, slaves and drug-dependent get less; then per-cohort ceiling
  const access = clamp(0.5 + 0.5 * x.standing - 0.7 * x.slaveShare - 0.25 * x.drugBurden, 0, 1);
  x.education = shiftedPyramid(r.baseline.Education, access);
  if (x.slaveShare > 0) {                                     // slavery ceiling, per cohort: cap Secondary+ , spill to Primary
    let above = x.education.Secondary + x.education.Undergrad + x.education.Postgrad;
    const bound = 1 - 0.9 * x.slaveShare;
    if (above > bound && above > 0) { const sc = bound/above, ex = above-bound; ['Secondary','Undergrad','Postgrad'].forEach(t=>x.education[t]*=sc); x.education.Primary += ex; }
  }
  // --- STRATIFICATION PER XENOTYPE (Elite / Middle / Underclass) ---
  const elite = clamp(0.04 + 0.28 * Math.max(0, x.standing) + 0.20 * x.eduIndex, 0, 0.6);
  const under = clamp(0.18 + 0.50 * Math.max(0, -x.standing) + 0.40 * x.slaveShare + 0.25 * x.drugBurden, 0, 0.85);
  x.strata = normTriple(elite, Math.max(0.05, 1 - elite - under), under);
  // --- SEX (biological): female fraction ~0.5; war kills more males -> female surplus ---
  x.femaleFrac = clamp(0.50 + 0.15 * r.Conflict, 0.30, 0.70);
  // --- GENDER identity: cisgender majority + a gender-diverse minority; ideology acceptance affects openness ---
  const accept = clamp(0.5 + 0.5 * r.ideoTolerance, 0, 1);
  x.genderDiverse = clamp(0.05 * accept, 0, 0.10);

  // --- (2a) HEALTHCARE ACCESS (region medicine modulated by cohort income; elite get better care) ---
  const regionMed = clamp(0.25 + 0.75 * r.Wealth + 0.4 * r.Education.Undergrad + 0.3 * r.Sector.Services, 0, 1);
  x.healthEnv = clamp(regionMed * (0.8 + 0.25 * clamp(x.income / 6, 0, 1)), 0, 1);

  // --- (2b) HOUSING, FREEDOM, CONTENTMENT (mood / life satisfaction), SUBSTANCE USE ---
  x.housing = clamp(0.2 + 0.45 * clamp(x.income / 8, 0, 1) + 0.35 * (r.devLevel / 5), 0, 1) * (1 - 0.5 * x.slaveShare);
  const freedom = clamp(1 - x.slaveShare - 0.5 * Math.max(0, -x.standing), 0, 1);   // SPI personal freedom, folded
  const afford = clamp(0.5 + x.netDaily / Math.max(1, x.costLiving), 0, 1);          // can they afford the lifestyle?
  x.contentment = clamp(0.12 + 0.25 * afford + 0.20 * x.healthEnv + 0.15 * x.housing + 0.15 * freedom
    + 0.10 * ((x.standing + 1) / 2) - 0.20 * r.Pollution - 0.15 * (r.Crime || 0), 0, 1);
  const availability = clamp(0.15 + 0.4 * r.Wealth + 0.3 * r.Sector.Services, 0, 1); // drugs easier to get in richer/urban regions
  x.substanceUse = clamp(0.05 + 0.45 * (1 - x.contentment) + 0.2 * (1 - emp) + 0.25 * availability - 0.2 * freedom, 0, 0.85);

  // --- (2c) CRIME: now also fed by substance use and discontent (links crime <-> health <-> mood) ---
  x.crime = clamp(0.02 + 0.35 * x.strata.Underclass + 0.25 * (1 - emp) + 0.25 * x.drugBurden + 0.30 * x.substanceUse
    + 0.20 * (1 - x.contentment) + 0.30 * Math.max(0, -x.standing) - 0.30 * x.eduIndex + 0.30 * r.Conflict, 0, 1);

  // --- (3) VITALS: mortality = SUM of cause-specific hazards (disease now cut by sanitation, raised by pollution & substance use) ---
  const env = x.healthEnv;
  const hz = {
    'old age':      0.011 * (80 / lifespan),                // scales inverse to genetic lifespan
    'malnutrition': 0.10 * clamp(0.5 - food, 0, 0.5) / 0.5,
    'disease':      0.032 * (1 - env) * (1 + 0.5 * r.Urbanisation) * (1 - 0.6 * (r.sanitation || 0)) * (1 + 0.8 * r.Pollution),
    'violence':     0.05 * x.crime,
    'war':          0.06 * r.Conflict,
    'addiction':    0.06 * (x.drugBurden + 0.5 * x.substanceUse) * (1 - env),  // genetic dependency + non-genetic use
    'xenophobia':   0.06 * Math.max(0, -x.standing),
  };
  const exo = hz.malnutrition + hz.disease + hz.violence + hz.war + hz.addiction + hz.xenophobia;
  x.mortHazard = hz['old age'] + exo;
  x.hazards = hz;
  const healthyLE = lifespan * (0.55 + 0.45 * env);
  x.lifeExp = Math.max(15, Math.round(healthyLE * (1 - clamp(exo * 4, 0, 0.78))));
  x.imr = Math.round(1000 * clamp(0.02 + 0.25 * (1 - env) + 0.30 * clamp(0.5 - food, 0, 0.5) / 0.5 + (x.fragility || 0), 0.004, 0.45));
  x.leadingCause = Object.entries(hz).sort((a, b) => b[1] - a[1])[0][0];

  // --- (4) DEPENDENCY RATIO (age approx): young from birth rate, elders from life expectancy ---
  const child = clamp(x.birth * 6, 0.10, 0.45), elder = clamp((x.lifeExp - 40) / 220, 0.02, 0.28);
  x.ageChild = child; x.ageElder = elder;
  x.dependency = (child + elder) / Math.max(0.2, 1 - child - elder);   // (children+elders) per working-age adult
}

// A fresh cohort born from cross-xenotype reproduction (or a baseliner throwback).
function newCohort(r, name, template) {
  const t = template || {};
  return { name, share: 0, pop: 0, basePreference: name === 'Hybrid' ? -0.15 : 0.1, baseInit: name === 'Hybrid' ? -0.15 : 0.1,
    fertility: 0.45, lifespan: t.lifespan || 80, fragility: t.fragility || 0.02, drugBurden: 0,
    heritable: name !== 'Sanguophage', standing: 0, birth: 0, migNet: 0, fight: 0, flight: 0, birthMult: 1, assets: 0 };
}

// POPULATION is per-cohort (each cohort its own headcount); region Population = the sum. Births are assigned
// to child cohorts by a reproduction/inheritance model: germline xenotypes breed true, cross-xenotype pairings
// yield Hybrids, and non-heritable (implanted) xenotypes like Sanguophage never breed true.
function moveStocks(r) {
  if (r.xenos[0].pop == null) { const P = r.Population || 1000; r.xenos.forEach(x => x.pop = P * (x.share ?? 0)); }
  const K = carryingCapacity(r); r.K = K;
  const totalPop = sum(r.xenos.map(x => x.pop)) || 1;
  const logistic = clamp(1 - totalPop / K, -0.5, 1);

  // gross births / deaths / migration per cohort
  for (const x of r.xenos) {
    x._births = Math.max(0, x.pop * x.birth * (1 - (x.imr || 0) / 1000) * Math.max(0, logistic));
    x._deaths = x.pop * (x.mortHazard || 0.012);
    x._mig    = x.pop * x.migNet;
  }
  // assign births to CHILD cohorts by mating (endogamy vs exogamy) + inheritance
  const born = {}; const add = (n, v) => { born[n] = (born[n] || 0) + v; };
  const endogamy = clamp(0.55 + 0.4 * (1 - (r.ideoTolerance + 1) / 2), 0.35, 0.95); // xenophobic -> stay separate
  const totPop = sum(r.xenos.map(x => x.pop)) || 1;
  for (const x of r.xenos) {
    const B = x._births; if (B <= 0) continue;
    add(x.heritable ? x.name : 'Baseliner', B * endogamy);       // within-group; implanted -> baseliner child
    const exo = B * (1 - endogamy);
    for (const y of r.xenos) {
      if (y === x || y.pop <= 0) continue;
      const n = exo * (y.pop / totPop); if (n <= 0) continue;
      let child;
      if (!x.heritable && !y.heritable) child = 'Baseliner';
      else if (x.heritable && y.heritable && x.name !== y.name) child = (x.name === 'Baseliner' || y.name === 'Baseliner') ? (x.name === 'Baseliner' ? y.name : x.name) : 'Hybrid';
      else child = x.heritable ? x.name : y.name;
      add(child, n);
    }
  }
  // apply deaths + migration, then add births; spawn Hybrid/Baseliner cohorts as needed
  for (const x of r.xenos) x.pop = Math.max(0, x.pop - x._deaths + x._mig);
  for (const [name, n] of Object.entries(born)) {
    let c = r.xenos.find(z => z.name === name);
    if (!c) { c = newCohort(r, name); r.xenos.push(c); }
    c.pop += n;
  }
  // prune vanished cohorts (keep it bounded); recompute region population + shares
  r.xenos = r.xenos.filter(x => x.pop > 0.5 || x.share > 0.001);
  const P = sum(r.xenos.map(x => x.pop)); r.Population = Math.max(1, P);
  r.xenos.forEach(x => x.share = P > 0 ? x.pop / P : 0);
  // region social indicators are now AGGREGATES of the cohorts (no longer independent nodes)
  r.Crime = sum(r.xenos.map(x => x.share * (x.crime || 0)));
  r.Health = sum(r.xenos.map(x => x.share * (x.healthEnv || 0.5)));
  r.Contentment = sum(r.xenos.map(x => x.share * (x.contentment || 0)));
  r.SubstanceUse = sum(r.xenos.map(x => x.share * (x.substanceUse || 0)));
  r.Housing = sum(r.xenos.map(x => x.share * (x.housing || 0)));
  r.Dependency = sum(r.xenos.map(x => x.share * (x.dependency || 0)));
  r.aggBirth = sum(r.xenos.map(x => x.share * x.birth)); r.aggGrowth = (P - totalPop) / totalPop;
}

function stepYear(r) {
  r.history.push(snapshotForLag(r)); if (r.history.length > 12) r.history.shift();
  // conflict decays
  r.Conflict = Math.max(0, r.Conflict - r.conflictDecay);
  r._t = {};
  stepXenos(r);
  moveStocks(r);                                    // sets per-cohort pop + region Crime/Health aggregates
  // region shared drivers (Crime & Health are now cohort aggregates, not stepped here)
  ['Wealth','Urbanisation','EmploymentRate'].forEach(k => stepScalar(r, k));
  ['Education','SES','Strata','Sector'].forEach(k => stepDist(r, k));
  r.year++;
}
function snapshotForLag(r) {
  const s = {}; for (const k of Object.keys(F)) s[k] = (F[k].kind === 'dist') ? { ...r[k] } : r[k]; return s;
}

module.exports = { F, DERIVED, CONTEXT, stepYear, clamp, sum, normalize, geoReport, GEO };

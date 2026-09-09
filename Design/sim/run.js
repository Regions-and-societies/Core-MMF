'use strict';
const { F, DERIVED, stepYear, sum, geoReport } = require('./graph');

// difficulty-scaled player-colony representative multiplier: hardest = 1:1, each easier step +>=1.
// (RimWorld renders ~275x275 and caps world-tile pop ~450, so 1:1 makes the colony negligible.)
const DIFFICULTY_MULT = { LosingIsFun:1, BloodAndDust:2, StriveToSurvive:3, AdventureStory:4, CommunityBuilder:5, Peaceful:6 };
function colonyRepresentativePop(pawnCount, difficulty) { return pawnCount * (DIFFICULTY_MULT[difficulty] ?? 3); }

// ---------- seed a region ----------
function seedStrata(rigidity, repression) {
  const elite = 0.05 + 0.06 * rigidity;
  const under = Math.min(0.85, 0.25 + 0.40 * rigidity + 0.25 * repression);
  return { Elite: elite, Middle: Math.max(0.05, 1 - elite - under), Underclass: under };
}
function eduPyramid(tech) { // Illiterate,Primary,Secondary,Undergrad,Postgrad
  const t = { 2:[0.50,0.38,0.10,0.02,0.00], 3:[0.35,0.40,0.20,0.05,0.00], 4:[0.10,0.25,0.45,0.17,0.03], 5:[0.03,0.12,0.45,0.30,0.10] }[tech] || [0.10,0.25,0.45,0.17,0.03];
  return { Illiterate:t[0], Primary:t[1], Secondary:t[2], Undergrad:t[3], Postgrad:t[4] };
}
function region(cfg) {
  const strata = seedStrata(cfg.rigidity, cfg.slavery ? 0.4 : 0.1);
  const r = {
    name: cfg.name, year: 0, history: [], _t: {},
    Population: 1000,
    Wealth: cfg.wealth, Urbanisation: cfg.urban, EmploymentRate: 0.6, Health: 0.5, Crime: 0.2,
    Education: eduPyramid(cfg.tech), SES: { Subsistence:0.4, Modest:0.35, Prosperous:0.2, Affluent:0.05 },
    Strata: { ...strata },
    Sector: { Agriculture:0.4, Extraction:0.1, Manufacturing:0.15, Services:0.2, Military:0.05, Public:0.1 },
    FactionRigidity: cfg.rigidity, SlaveryStance: cfg.slavery ? 1 : 0,
    Conflict: cfg.conflict || 0, Roads: cfg.roads ?? 0.3, BiomeFertility: cfg.fertility ?? 0.5,
    Pollution: cfg.pollution ?? 0.1,
    geo: { tiles: cfg.tiles ?? 20 },
    conflictDecay: 0.33,                        // ~3-yr decay
    natalism: cfg.natalism ?? 0, ideoTolerance: cfg.tolerance ?? 0,
    xenos: cfg.xenos.map(x => ({ lifespan:80, fragility:0, ...x, standing:0, birth:0, migNet:0, fight:0, flight:0, birthMult:1, assets:0 })),
  };
  r.baseline = {
    Wealth: cfg.wealth, Urbanisation: cfg.urban, EmploymentRate: 0.6, Health: 0.5, Crime: 0.2,
    Education: { ...r.Education }, SES: { ...r.SES }, Strata: { ...strata }, Sector: { ...r.Sector },
    ageWorking: 0.55,
  };
  r.state = {};
  for (const k of Object.keys(F)) r.state[k] = (F[k].kind === 'dist') ? { moving:false, v: Object.fromEntries(F[k].tiers.map(t=>[t,0])) } : { moving:false, v:0 };
  return r;
}

// ---------- xenotype library (values a real build reads from each XenotypeDef's genes at runtime) ----------
// lifespan <- Longevity/Ageless genes; drugBurden <- ChemicalDependency gene; fragility <- frailty genes.
// A region's roster is whatever XenotypesAvailableFor returns for its faction — modded xenotypes included,
// DLC-absent ones absent. Hardcoded here ONLY to drive the sim.
// heritable = germline (inherited by birth). Sanguophage is xenogene/implanted -> NOT bred true (real RimWorld).
const XENO = {
  Baseliner:   { lifespan:80,   fragility:0.00, drugBurden:0.00, heritable:true },
  Genie:       { lifespan:80,   fragility:0.00, drugBurden:0.00, heritable:true },
  Highmate:    { lifespan:80,   fragility:0.02, drugBurden:0.00, heritable:true },
  Sanguophage: { lifespan:1000, fragility:0.00, drugBurden:0.00, heritable:false },  // implanted, not born
  Hussar:      { lifespan:80,   fragility:0.02, drugBurden:0.35, heritable:true },
  Waster:      { lifespan:70,   fragility:0.05, drugBurden:0.30, heritable:true },
  Pigskin:     { lifespan:70,   fragility:0.03, drugBurden:0.00, heritable:true },
  Neanderthal: { lifespan:85,   fragility:0.00, drugBurden:0.00, heritable:true },
  Yttakin:     { lifespan:78,   fragility:0.00, drugBurden:0.00, heritable:true },
  Dirtmole:    { lifespan:75,   fragility:0.02, drugBurden:0.00, heritable:true },
  Impid:       { lifespan:78,   fragility:0.02, drugBurden:0.00, heritable:true },
  Hybrid:      { lifespan:80,   fragility:0.02, drugBurden:0.00, heritable:true },  // cross-xenotype child
};
// build a cohort from the library + per-scenario share/standing (basePreference = ideology's view of it)
function xeno(name, share, basePreference, fertility) {
  const g = XENO[name] || XENO.Baseliner;
  return { name, share, basePreference, baseInit: basePreference, fertility: fertility ?? 0.45, ...g };
}

// ---------- scenarios (rosters = the faction's real xenotype set) ----------
const scenarios = {
  Empire:        region({ name:'Empire (rigid, slavery accepted) — full roster', tech:4, rigidity:0.8, slavery:true, wealth:0.4, urban:0.5, natalism:0, tolerance:0.2, tiles:28, fertility:0.5,
                          xenos:[ xeno('Baseliner',0.70,0.4,0.5), xeno('Highmate',0.08,0.8,0.4), xeno('Genie',0.08,0.5,0.4), xeno('Sanguophage',0.02,0.7,0.3), xeno('Hussar',0.07,0.2,0.35), xeno('Waster',0.05,-0.6,0.45) ] }),
  TribalMixed:   region({ name:'Tribal (open, no slavery) — mixed roster', tech:2, rigidity:0.2, slavery:false, wealth:0.15, urban:0.1, natalism:1, tolerance:0.3, fertility:0.6, tiles:16,
                          xenos:[ xeno('Baseliner',0.55,0.2,0.6), xeno('Neanderthal',0.3,0.3,0.6), xeno('Yttakin',0.15,0.1,0.55) ] }),
  PirateRough:   region({ name:'Pirate/rough outlander — drug-dependent roster', tech:4, rigidity:0.5, slavery:true, wealth:0.32, urban:0.3, natalism:0, tolerance:0, tiles:10, fertility:0.3, pollution:0.35,
                          xenos:[ xeno('Baseliner',0.4,0.1,0.5), xeno('Hussar',0.25,0.1,0.35), xeno('Waster',0.2,-0.3,0.45), xeno('Pigskin',0.15,-0.5,0.5) ] }),
  NoBiotech:     region({ name:'No Biotech installed — collapses to ONE cohort (= old region model)', tech:4, rigidity:0.4, slavery:false, wealth:0.4, urban:0.4, natalism:0, tolerance:0.2, tiles:18, fertility:0.5,
                          xenos:[ xeno('Baseliner',1.0,0.2,0.5) ] }),
};

// ---------- run + report (Markdown) ----------
const HORIZONS = [2, 10, 100];   // ~100 days(1.7yr)->nearest, 10 yr, 100 yr
const p0 = x => Math.round(100 * x) + '%';
const d1 = x => x.toFixed(1);
const distMd = d => Object.values(d).map(v => Math.round(100*v)).join('/');   // e.g. 14/47/25/12/2
const md = [];
const say = s => md.push(s);

function snap(r) {
  return {
    yr: r.year, Pop: Math.round(r.Population), W: r.Wealth, Bal: DERIVED.Balance(r), Health: r.Health, Crime: r.Crime,
    Drug: DERIVED.DrugBurdenAgg(r), geo: geoReport(r),
    devLevel: r.devLevel, sanitation: r.sanitation, pollution: r.Pollution,
    Content: r.Contentment, Subst: r.SubstanceUse, Housing: r.Housing, Dep: r.Dependency,
    cohorts: r.xenos.map(x => ({
      name: x.name, share: x.share, edu: {...x.education}, strata: {...x.strata},
      female: x.femaleFrac, genderDiv: x.genderDiverse, income: x.income, cost: x.costLiving,
      net: x.netDaily, assets: Math.round(x.assets), src: x.incomeSource, slaves: x.slaveShare,
      LE: x.lifeExp, IMR: x.imr, cause: x.leadingCause,
      content: x.contentment, housing: x.housing, subst: x.substanceUse, dep: x.dependency,
    })),
    slaveSummary: (() => {
      const total = sum(r.xenos.map(x => x.share * x.slaveShare));
      if (total <= 0) return null;
      const comp = r.xenos.map(x => [x.name, x.share * x.slaveShare / total]).filter(e => e[1] > 0.005).sort((a,b)=>b[1]-a[1]);
      return { total, comp };
    })(),
  };
}

function runScenario(name, r, shockAtYear, shockFn) {
  const checkpoints = {}; const maxYear = Math.max(...HORIZONS); let shockNote = null;
  for (let y = 0; y <= maxYear; y++) {
    if (shockAtYear != null && y === shockAtYear) shockNote = shockFn(r);
    if (HORIZONS.includes(y)) checkpoints[y] = snap(r);
    if (y < maxYear) stepYear(r);
  }
  say(`\n## ${r.name}\n`);
  if (shockNote) say(`> ${shockNote}\n`);
  say(`**Region (population-weighted aggregates):**\n`);
  say(`| yr | Wealth | Balance | Health | Crime | Contentment | Substance use | Housing | Dependency | dev level | sanitation | pollution | Pop | density /km² | food self-suff |`);
  say(`|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|`);
  for (const y of HORIZONS) { const c = checkpoints[y];
    say(`| ${y} | ${p0(c.W)} | ${p0(c.Bal)} | ${p0(c.Health)} | ${p0(c.Crime)} | ${p0(c.Content)} | ${p0(c.Subst)} | ${p0(c.Housing)} | ${c.Dep.toFixed(2)} | ${c.devLevel.toFixed(1)} | ${p0(c.sanitation)} | ${p0(c.pollution)} | ${c.Pop} | ${c.geo.density.toFixed(1)} | ${c.geo.selfSuff.toFixed(2)}x |`);
  }
  for (const y of HORIZONS) { const c = checkpoints[y];
    say(`\n**Cohorts @ yr ${y}** (${c.geo.areaKm2.toFixed(0)} km², ${r.geo.tiles} tiles):\n`);
    say(`| xenotype | share | education I/P/S/U/Pg | strata E/M/U | female | gender-div | income/d | cost/d | net/d | assets | slaves | life exp | infant mort | leading cause |`);
    say(`|---|---|---|---|---|---|---|---|---|---|---|---|---|---|`);
    for (const x of c.cohorts) {
      const net = (x.net>=0?'+':'') + d1(x.net);
      say(`| ${x.name} | ${p0(x.share)} | ${distMd(x.edu)} | ${distMd(x.strata)} | ${p0(x.female)} | ${p0(x.genderDiv)} | ${d1(x.income)} | ${d1(x.cost)} | ${net} | ${x.assets} | ${p0(x.slaves)} | ${x.LE}y | ${x.IMR}/1000 | ${x.cause} |`);
    }
    if (c.slaveSummary) say(`\n_Slavery: ${p0(c.slaveSummary.total)} of the population enslaved — of those: ${c.slaveSummary.comp.map(([n,f])=>`${n} ${p0(f)}`).join(', ')}._`);
    say(`\n_Social indicators @ yr ${y}:_\n`);
    say(`| xenotype | contentment | housing | substance use | dependency ratio |`);
    say(`|---|---|---|---|---|`);
    for (const x of c.cohorts) say(`| ${x.name} | ${p0(x.content)} | ${p0(x.housing)} | ${p0(x.subst)} | ${x.dep.toFixed(2)} |`);
  }
}

// edge audit: every factor -> its inbound sources (region-level graph)
function edgeAudit() {
  say(`# Demographic influence graph — simulation run\n`);
  say(`Native tick = one demographic year; horizons at ~2 yr (short), 10 yr, 100 yr. Per-cohort model: education, stratification, sex, gender, wealth (income/cost/assets) and health vitals are all tracked per xenotype; region rows are population-weighted aggregates.\n`);
  say(`## Edge audit (region-level graph): target ← sources\n`);
  say('```');
  const sources = new Set();
  for (const [k, def] of Object.entries(F)) {
    const ins = def.edges.map(e => e[0] + (e[1] ? '.' + e[1] : '') + (e[7] === 'Ceiling' ? '(ceil)' : ''));
    ins.forEach(i => sources.add(i.split('(')[0]));
    say(`  ${k.padEnd(15)} <- ${ins.join(', ')}`);
  }
  say('```');
  say(`\n_Derived:_ ${Object.keys(DERIVED).join(', ')}`);
  say(`_Per-xenotype resolved:_ education, stratification, sex, gender, wealth (income/cost/assets), standing, birth, migration, fight/flight, health vitals.`);
}

// conquest shock (user decision 1): the region ADOPTS THE CONQUEROR'S IDEOLOGY overall — swap the owning
// faction's Context (rigidity, slavery). May be a BIGGER impact, not just relaxation. The elite takes the
// shock; who dies vs flees is the derived fight/flight split. (A later policy hook adds slave-cap decisions.)
function makeConquest(conqueror) {
  return function (r) {
    // fight/flight split of the shocked elite, from current derived pressures (pop-weighted over xenos)
    let fight = 0, flight = 0;
    for (const x of r.xenos) { fight += x.share * x.fight; flight += x.share * x.flight; }
    const fleeFrac = flight / Math.max(0.01, fight + flight);
    const eliteLoss = 0.18;
    const fled = eliteLoss * fleeFrac, died = eliteLoss * (1 - fleeFrac);
    r.Strata.Elite = Math.max(0.01, r.Strata.Elite - eliteLoss);
    r.Strata.Middle += eliteLoss * 0.5; r.Strata.Underclass += eliteLoss * 0.5;
    const t = r.Strata.Elite + r.Strata.Middle + r.Strata.Underclass; r.Strata.Elite/=t; r.Strata.Middle/=t; r.Strata.Underclass/=t;
    r.Population *= (1 - died - fled);
    r.Conflict = 1.0;
    // adopt the conqueror's ideology overall
    r.FactionRigidity = conqueror.rigidity; r.SlaveryStance = conqueror.slavery ? 1 : 0;
    r.ideoTolerance = conqueror.tolerance;
    return `**CONQUEST at yr ${r.year} by ${conqueror.name}:** elite −18% (${(died*100).toFixed(0)}% died, ${(fled*100).toFixed(0)}% fled) → region adopts rigidity ${r.FactionRigidity}, slavery ${r.SlaveryStance}, tolerance ${r.ideoTolerance}.`;
  };
}

// ---------- main ----------
edgeAudit();
runScenario('Empire', scenarios.Empire);
runScenario('TribalMixed', scenarios.TribalMixed);
runScenario('PirateRough', scenarios.PirateRough);
runScenario('NoBiotech', scenarios.NoBiotech);
// Two conquests of the same Empire: a LIBERAL conqueror (relaxes -> middle regrows) and a HARSHER one (more rigid -> worse).
const liberalConqueror = { name:'Union (liberal, no slavery)', rigidity:0.25, slavery:false, tolerance:0.4 };
const harsherConqueror = { name:'Imperium (harsher, slaving)', rigidity:0.95, slavery:true, tolerance:-0.2 };
const conqueredRoster = () => [ xeno('Baseliner',0.70,0.4,0.5), xeno('Highmate',0.08,0.8,0.4), xeno('Genie',0.08,0.5,0.4), xeno('Sanguophage',0.02,0.7,0.3), xeno('Hussar',0.07,0.2,0.35), xeno('Waster',0.05,-0.6,0.45) ];
const conqueredCfg = () => ({ name:'Empire CONQUERED at yr 5', tech:4, rigidity:0.8, slavery:true, wealth:0.4, urban:0.5, tolerance:0.2, tiles:28, fertility:0.5, xenos: conqueredRoster() });
runScenario('EmpireConquered-Liberal', region({ ...conqueredCfg(), name:'Empire conquered by a LIBERAL union at yr 5' }), 5, makeConquest(liberalConqueror));
runScenario('EmpireConquered-Harsher', region({ ...conqueredCfg(), name:'Empire conquered by a HARSHER imperium at yr 5' }), 5, makeConquest(harsherConqueror));

// flush markdown
console.log(md.join('\n'));

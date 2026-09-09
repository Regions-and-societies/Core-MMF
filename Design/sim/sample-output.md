# Demographic influence graph — simulation run

Native tick = one demographic year; horizons at ~2 yr (short), 10 yr, 100 yr. Per-cohort model: education, stratification, sex, gender, wealth (income/cost/assets) and health vitals are all tracked per xenotype; region rows are population-weighted aggregates.

## Edge audit (region-level graph): target ← sources

```
  Wealth          <- EmploymentRate, Sector.Manufacturing, Sector.Services, Education.Secondary, Balance, DrugBurdenAgg, Roads, Conflict, BiomeFertility, Crime, Health
  Urbanisation    <- Wealth, Sector.Manufacturing, Sector.Services, Roads, Sector.Agriculture
  EmploymentRate  <- Education.Secondary, Balance, Wealth, Roads, DrugBurdenAgg, Conflict, Health, Crime
  Health          <- Wealth, Education.Undergrad, Sector.Services, FoodSelfSuff, DrugBurdenAgg, Crime, Conflict, Urbanisation
  Crime           <- Balance, Inequality, SES.Subsistence, EmploymentRate, DrugBurdenAgg, Education.Secondary, FactionRigidity, Conflict
  Education       <- Wealth, Urbanisation, Urbanisation, Strata.Underclass, Strata.Underclass, Strata.Middle, Strata.Elite, Strata.Elite, Conflict, SlaveryEnslavedShare(ceil)
  SES             <- Wealth, Wealth, Strata.Underclass, Strata.Middle, Strata.Middle, Strata.Elite, DrugBurdenAgg
  Strata          <- FactionRigidity, FactionRigidity, FactionRigidity, Education.Secondary, Education.Postgrad, Wealth, Urbanisation, Conflict, MigrationAgg
  Sector          <- Balance, Urbanisation, BiomeFertility, Education.Undergrad, FactionRigidity, Conflict
```

_Derived:_ Balance, DrugBurdenAgg, MigrationAgg, SlaveryEnslavedShare, FoodSelfSuff, Inequality
_Per-xenotype resolved:_ education, stratification, sex, gender, wealth (income/cost/assets), standing, birth, migration, fight/flight, health vitals.

## Empire (rigid, slavery accepted) — full roster

**Region (population-weighted aggregates):**

| yr | Wealth | Balance | Health | Crime | Contentment | Substance use | Housing | Dependency | dev level | sanitation | pollution | Pop | density /km² | food self-suff |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 2 | 53% | 14% | 68% | 24% | 60% | 21% | 57% | 0.84 | 2.6 | 19% | 10% | 1014 | 9.1 | 4.95x |
| 10 | 61% | 6% | 85% | 10% | 74% | 10% | 73% | 1.01 | 3.3 | 35% | 10% | 1213 | 10.8 | 4.53x |
| 100 | 62% | 6% | 87% | 6% | 78% | 8% | 76% | 1.04 | 3.3 | 36% | 10% | 4620 | 41.2 | 1.20x |

**Cohorts @ yr 2** (112 km², 28 tiles):

| xenotype | share | education I/P/S/U/Pg | strata E/M/U | female | gender-div | income/d | cost/d | net/d | assets | slaves | life exp | infant mort | leading cause |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Baseliner | 70% | 3/14/44/29/9 | 27/53/20 | 50% | 3% | 3.9 | 4.4 | -0.5 | 14 | 5% | 62y | 98/1000 | disease |
| Highmate | 8% | 1/8/38/36/16 | 38/42/20 | 50% | 3% | 4.5 | 4.5 | +0.0 | 52 | 5% | 63y | 114/1000 | disease |
| Genie | 8% | 3/13/43/31/10 | 30/50/20 | 50% | 3% | 4.0 | 4.4 | -0.4 | 22 | 5% | 62y | 97/1000 | disease |
| Sanguophage | 2% | 2/10/40/34/14 | 35/45/20 | 50% | 3% | 4.3 | 4.4 | -0.2 | 41 | 5% | 785y | 95/1000 | disease |
| Hussar | 7% | 9/24/45/18/3 | 21/47/32 | 50% | 3% | 2.8 | 4.3 | -1.5 | 0 | 14% | 56y | 127/1000 | violence |
| Waster | 4% | 38/35/23/3/0 | 8/32/60 | 50% | 3% | 1.6 | 3.9 | -2.3 | 0 | 37% | 38y | 165/1000 | violence |
| Hybrid | 0% | 10/25/45/16/3 | 10/70/20 | 50% | 3% | 3.0 | 2.9 | +0.2 | 6 | 5% | 61y | 124/1000 | disease |

_Slavery: 7% of the population enslaved — of those: Baseliner 50%, Waster 22%, Hussar 14%, Highmate 6%, Genie 6%, Sanguophage 1%._

_Social indicators @ yr 2:_

| xenotype | contentment | housing | substance use | dependency ratio |
|---|---|---|---|---|
| Baseliner | 61% | 58% | 20% | 0.85 |
| Highmate | 67% | 62% | 17% | 0.87 |
| Genie | 62% | 59% | 19% | 0.85 |
| Sanguophage | 65% | 61% | 18% | 1.78 |
| Hussar | 51% | 50% | 26% | 0.76 |
| Waster | 34% | 38% | 42% | 0.32 |
| Hybrid | 62% | 54% | 19% | 0.84 |

**Cohorts @ yr 10** (112 km², 28 tiles):

| xenotype | share | education I/P/S/U/Pg | strata E/M/U | female | gender-div | income/d | cost/d | net/d | assets | slaves | life exp | infant mort | leading cause |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Baseliner | 70% | 3/13/43/31/10 | 30/50/20 | 50% | 3% | 5.8 | 5.2 | +0.6 | 100 | 5% | 72y | 56/1000 | old age |
| Highmate | 10% | 1/8/38/36/16 | 39/41/20 | 50% | 3% | 6.5 | 5.2 | +1.2 | 296 | 5% | 73y | 74/1000 | old age |
| Genie | 9% | 3/13/43/31/10 | 30/50/20 | 50% | 3% | 5.8 | 5.2 | +0.6 | 120 | 5% | 72y | 56/1000 | old age |
| Sanguophage | 2% | 2/10/41/34/13 | 35/45/20 | 50% | 3% | 6.2 | 5.2 | +0.9 | 226 | 5% | 907y | 54/1000 | disease |
| Hussar | 7% | 10/25/45/17/3 | 20/47/32 | 50% | 3% | 4.0 | 4.7 | -0.7 | 0 | 14% | 65y | 91/1000 | violence |
| Waster | 2% | 38/35/23/3/0 | 8/30/62 | 50% | 3% | 2.3 | 4.3 | -2.0 | 0 | 38% | 44y | 136/1000 | violence |
| Hybrid | 1% | 11/26/44/16/3 | 10/70/20 | 50% | 3% | 4.4 | 3.3 | +1.1 | 235 | 5% | 70y | 88/1000 | old age |

_Slavery: 6% of the population enslaved — of those: Baseliner 55%, Hussar 15%, Waster 13%, Highmate 8%, Genie 7%, Sanguophage 1%, Hybrid 1%._

_Social indicators @ yr 10:_

| xenotype | contentment | housing | substance use | dependency ratio |
|---|---|---|---|---|
| Baseliner | 75% | 74% | 9% | 1.02 |
| Highmate | 80% | 77% | 7% | 1.04 |
| Genie | 75% | 74% | 9% | 1.02 |
| Sanguophage | 78% | 76% | 8% | 1.78 |
| Hussar | 62% | 61% | 16% | 0.90 |
| Waster | 40% | 45% | 36% | 0.31 |
| Hybrid | 75% | 66% | 9% | 0.99 |

**Cohorts @ yr 100** (112 km², 28 tiles):

| xenotype | share | education I/P/S/U/Pg | strata E/M/U | female | gender-div | income/d | cost/d | net/d | assets | slaves | life exp | infant mort | leading cause |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Baseliner | 58% | 2/10/41/34/13 | 34/46/20 | 50% | 3% | 6.2 | 5.3 | +0.9 | 2426 | 5% | 73y | 51/1000 | old age |
| Highmate | 19% | 1/8/38/36/16 | 39/41/20 | 50% | 3% | 6.6 | 5.3 | +1.2 | 3697 | 5% | 73y | 71/1000 | old age |
| Genie | 18% | 2/12/42/32/11 | 32/48/20 | 50% | 3% | 6.0 | 5.3 | +0.7 | 1921 | 5% | 73y | 51/1000 | old age |
| Sanguophage | 1% | 2/11/42/33/12 | 33/47/20 | 50% | 3% | 6.1 | 5.3 | +0.8 | 2565 | 5% | 915y | 51/1000 | disease |
| Hussar | 3% | 11/26/45/16/3 | 19/49/32 | 50% | 3% | 3.9 | 4.7 | -0.8 | 0 | 14% | 66y | 89/1000 | violence |
| Hybrid | 2% | 12/27/44/15/2 | 9/69/22 | 50% | 3% | 4.3 | 3.3 | +1.0 | 2983 | 6% | 69y | 86/1000 | old age |

_Slavery: 5% of the population enslaved — of those: Baseliner 55%, Highmate 18%, Genie 17%, Hussar 7%, Hybrid 3%, Sanguophage 1%._

_Social indicators @ yr 100:_

| xenotype | contentment | housing | substance use | dependency ratio |
|---|---|---|---|---|
| Baseliner | 78% | 76% | 8% | 1.04 |
| Highmate | 81% | 78% | 6% | 1.04 |
| Genie | 77% | 75% | 8% | 1.04 |
| Sanguophage | 78% | 76% | 8% | 1.78 |
| Hussar | 62% | 61% | 17% | 0.92 |
| Hybrid | 74% | 65% | 10% | 0.94 |

## Tribal (open, no slavery) — mixed roster

**Region (population-weighted aggregates):**

| yr | Wealth | Balance | Health | Crime | Contentment | Substance use | Housing | Dependency | dev level | sanitation | pollution | Pop | density /km² | food self-suff |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 2 | 26% | 20% | 43% | 25% | 58% | 17% | 40% | 0.71 | 0.9 | 0% | 10% | 950 | 14.8 | 2.17x |
| 10 | 41% | 40% | 63% | 9% | 73% | 6% | 59% | 0.86 | 1.7 | 7% | 10% | 973 | 15.2 | 3.60x |
| 100 | 42% | 40% | 64% | 8% | 75% | 6% | 60% | 0.88 | 1.8 | 8% | 10% | 1877 | 29.3 | 1.86x |

**Cohorts @ yr 2** (64 km², 16 tiles):

| xenotype | share | education I/P/S/U/Pg | strata E/M/U | female | gender-div | income/d | cost/d | net/d | assets | slaves | life exp | infant mort | leading cause |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Baseliner | 54% | 33/42/19/6/0 | 22/60/18 | 50% | 3% | 2.5 | 2.4 | +0.1 | 3 | 0% | 51y | 163/1000 | disease |
| Neanderthal | 30% | 30/42/21/8/0 | 24/58/18 | 50% | 3% | 2.6 | 2.4 | +0.2 | 7 | 0% | 54y | 163/1000 | disease |
| Yttakin | 15% | 37/42/16/5/0 | 18/64/18 | 50% | 3% | 2.4 | 1.8 | +0.6 | 30 | 0% | 50y | 164/1000 | disease |
| Hybrid | 0% | 45/40/12/3/0 | 11/71/18 | 50% | 3% | 2.2 | 1.8 | +0.4 | 12 | 0% | 50y | 185/1000 | disease |

_Social indicators @ yr 2:_

| xenotype | contentment | housing | substance use | dependency ratio |
|---|---|---|---|---|
| Baseliner | 56% | 40% | 18% | 0.69 |
| Neanderthal | 58% | 41% | 17% | 0.74 |
| Yttakin | 63% | 40% | 15% | 0.68 |
| Hybrid | 59% | 39% | 17% | 0.68 |

**Cohorts @ yr 10** (64 km², 16 tiles):

| xenotype | share | education I/P/S/U/Pg | strata E/M/U | female | gender-div | income/d | cost/d | net/d | assets | slaves | life exp | infant mort | leading cause |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Baseliner | 52% | 30/42/20/7/0 | 26/56/18 | 50% | 3% | 4.8 | 3.8 | +1.0 | 169 | 0% | 61y | 113/1000 | disease |
| Neanderthal | 32% | 28/42/22/9/0 | 28/54/18 | 50% | 3% | 4.9 | 3.8 | +1.1 | 204 | 0% | 66y | 112/1000 | disease |
| Yttakin | 16% | 36/42/17/5/0 | 20/62/18 | 50% | 3% | 4.4 | 2.5 | +1.9 | 395 | 0% | 60y | 115/1000 | disease |
| Hybrid | 1% | 46/39/12/3/0 | 11/71/18 | 50% | 3% | 3.9 | 2.5 | +1.4 | 282 | 0% | 61y | 139/1000 | disease |

_Social indicators @ yr 10:_

| xenotype | contentment | housing | substance use | dependency ratio |
|---|---|---|---|---|
| Baseliner | 72% | 59% | 7% | 0.84 |
| Neanderthal | 74% | 60% | 6% | 0.92 |
| Yttakin | 77% | 57% | 5% | 0.82 |
| Hybrid | 74% | 54% | 6% | 0.84 |

**Cohorts @ yr 100** (64 km², 16 tiles):

| xenotype | share | education I/P/S/U/Pg | strata E/M/U | female | gender-div | income/d | cost/d | net/d | assets | slaves | life exp | infant mort | leading cause |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Baseliner | 30% | 26/42/23/9/0 | 29/53/18 | 50% | 3% | 5.1 | 3.9 | +1.2 | 3315 | 0% | 62y | 109/1000 | disease |
| Neanderthal | 42% | 23/41/25/12/0 | 33/49/18 | 50% | 3% | 5.3 | 3.9 | +1.4 | 3815 | 0% | 67y | 107/1000 | disease |
| Yttakin | 14% | 35/42/17/5/0 | 21/61/18 | 50% | 3% | 4.5 | 3.8 | +0.7 | 2782 | 0% | 60y | 112/1000 | disease |
| Hybrid | 13% | 45/40/12/3/0 | 13/69/18 | 50% | 3% | 4.0 | 2.6 | +1.5 | 4087 | 0% | 61y | 135/1000 | disease |

_Social indicators @ yr 100:_

| xenotype | contentment | housing | substance use | dependency ratio |
|---|---|---|---|---|
| Baseliner | 75% | 61% | 6% | 0.85 |
| Neanderthal | 77% | 62% | 5% | 0.93 |
| Yttakin | 69% | 58% | 8% | 0.82 |
| Hybrid | 75% | 55% | 6% | 0.84 |

## Pirate/rough outlander — drug-dependent roster

**Region (population-weighted aggregates):**

| yr | Wealth | Balance | Health | Crime | Contentment | Substance use | Housing | Dependency | dev level | sanitation | pollution | Pop | density /km² | food self-suff |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 2 | 38% | 33% | 57% | 41% | 45% | 29% | 44% | 0.62 | 1.8 | 7% | 35% | 903 | 22.6 | 1.74x |
| 10 | 46% | 26% | 69% | 24% | 58% | 17% | 56% | 0.74 | 2.4 | 16% | 35% | 711 | 17.8 | 2.29x |
| 100 | 51% | 26% | 75% | 13% | 69% | 11% | 63% | 0.91 | 2.5 | 19% | 35% | 711 | 17.8 | 2.29x |

**Cohorts @ yr 2** (40 km², 10 tiles):

| xenotype | share | education I/P/S/U/Pg | strata E/M/U | female | gender-div | income/d | cost/d | net/d | assets | slaves | life exp | infant mort | leading cause |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Baseliner | 42% | 9/24/45/18/3 | 13/67/20 | 50% | 3% | 3.4 | 2.5 | +0.9 | 54 | 5% | 57y | 123/1000 | disease |
| Hussar | 26% | 15/30/41/12/2 | 13/55/32 | 50% | 3% | 2.6 | 3.9 | -1.2 | 0 | 14% | 51y | 148/1000 | violence |
| Waster | 19% | 33/35/27/4/0 | 9/39/52 | 50% | 3% | 1.9 | 3.6 | -1.7 | 0 | 30% | 37y | 182/1000 | violence |
| Pigskin | 13% | 38/35/24/3/0 | 8/35/57 | 50% | 3% | 1.7 | 2.4 | -0.7 | 0 | 35% | 36y | 164/1000 | violence |
| Hybrid | 0% | 18/31/39/10/1 | 9/59/31 | 50% | 3% | 2.8 | 2.4 | +0.4 | 11 | 14% | 52y | 147/1000 | disease |

_Slavery: 16% of the population enslaved — of those: Waster 35%, Pigskin 29%, Hussar 22%, Baseliner 13%._

_Social indicators @ yr 2:_

| xenotype | contentment | housing | substance use | dependency ratio |
|---|---|---|---|---|
| Baseliner | 60% | 50% | 19% | 0.78 |
| Hussar | 40% | 44% | 30% | 0.69 |
| Waster | 28% | 37% | 41% | 0.39 |
| Pigskin | 29% | 35% | 44% | 0.27 |
| Hybrid | 49% | 45% | 27% | 0.57 |

**Cohorts @ yr 10** (40 km², 10 tiles):

| xenotype | share | education I/P/S/U/Pg | strata E/M/U | female | gender-div | income/d | cost/d | net/d | assets | slaves | life exp | infant mort | leading cause |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Baseliner | 49% | 8/22/46/20/4 | 16/64/20 | 50% | 3% | 5.0 | 2.8 | +2.2 | 531 | 5% | 64y | 92/1000 | disease |
| Hussar | 28% | 14/29/42/13/2 | 14/53/32 | 50% | 3% | 3.8 | 4.2 | -0.4 | 0 | 14% | 58y | 121/1000 | disease |
| Waster | 13% | 32/35/28/5/0 | 9/41/51 | 50% | 3% | 2.8 | 3.9 | -1.1 | 0 | 29% | 43y | 159/1000 | violence |
| Pigskin | 8% | 38/35/24/3/0 | 8/35/57 | 50% | 3% | 2.4 | 2.7 | -0.3 | 0 | 35% | 40y | 142/1000 | xenophobia |
| Hybrid | 1% | 19/32/38/10/1 | 9/57/34 | 50% | 3% | 3.9 | 2.7 | +1.1 | 267 | 16% | 57y | 121/1000 | disease |

_Slavery: 13% of the population enslaved — of those: Waster 30%, Hussar 29%, Pigskin 21%, Baseliner 19%, Hybrid 1%._

_Social indicators @ yr 10:_

| xenotype | contentment | housing | substance use | dependency ratio |
|---|---|---|---|---|
| Baseliner | 70% | 63% | 9% | 0.88 |
| Hussar | 52% | 54% | 19% | 0.79 |
| Waster | 39% | 45% | 31% | 0.40 |
| Pigskin | 39% | 41% | 34% | 0.27 |
| Hybrid | 61% | 54% | 18% | 0.60 |

**Cohorts @ yr 100** (40 km², 10 tiles):

| xenotype | share | education I/P/S/U/Pg | strata E/M/U | female | gender-div | income/d | cost/d | net/d | assets | slaves | life exp | infant mort | leading cause |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Baseliner | 72% | 6/19/46/23/5 | 20/60/20 | 50% | 3% | 5.4 | 3.0 | +2.3 | 6863 | 5% | 67y | 80/1000 | old age |
| Hussar | 28% | 11/27/44/15/3 | 18/50/32 | 50% | 3% | 4.1 | 4.4 | -0.3 | 0 | 14% | 61y | 110/1000 | violence |
| Hybrid | 0% | 22/33/36/8/1 | 9/53/38 | 50% | 3% | 3.1 | 2.9 | +0.2 | 1109 | 20% | 56y | 118/1000 | violence |

_Slavery: 7% of the population enslaved — of those: Hussar 51%, Baseliner 49%._

_Social indicators @ yr 100:_

| xenotype | contentment | housing | substance use | dependency ratio |
|---|---|---|---|---|
| Baseliner | 74% | 66% | 8% | 0.93 |
| Hussar | 56% | 56% | 18% | 0.84 |
| Hybrid | 52% | 50% | 23% | 0.54 |

## No Biotech installed — collapses to ONE cohort (= old region model)

**Region (population-weighted aggregates):**

| yr | Wealth | Balance | Health | Crime | Contentment | Substance use | Housing | Dependency | dev level | sanitation | pollution | Pop | density /km² | food self-suff |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 2 | 60% | 66% | 78% | 11% | 81% | 10% | 68% | 0.95 | 2.4 | 18% | 10% | 1019 | 14.2 | 5.47x |
| 10 | 76% | 62% | 100% | 1% | 86% | 5% | 91% | 1.18 | 3.7 | 52% | 10% | 1307 | 18.2 | 4.37x |
| 100 | 76% | 62% | 100% | 0% | 88% | 3% | 91% | 1.16 | 3.7 | 52% | 10% | 5947 | 82.6 | 0.96x |

**Cohorts @ yr 2** (72 km², 18 tiles):

| xenotype | share | education I/P/S/U/Pg | strata E/M/U | female | gender-div | income/d | cost/d | net/d | assets | slaves | life exp | infant mort | leading cause |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Baseliner | 100% | 4/17/46/26/7 | 24/58/18 | 50% | 3% | 5.6 | 3.1 | +2.4 | 133 | 0% | 68y | 76/1000 | old age |

_Social indicators @ yr 2:_

| xenotype | contentment | housing | substance use | dependency ratio |
|---|---|---|---|---|
| Baseliner | 81% | 68% | 10% | 0.95 |

**Cohorts @ yr 10** (72 km², 18 tiles):

| xenotype | share | education I/P/S/U/Pg | strata E/M/U | female | gender-div | income/d | cost/d | net/d | assets | slaves | life exp | infant mort | leading cause |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Baseliner | 100% | 4/15/45/28/8 | 26/56/18 | 50% | 3% | 8.2 | 6.4 | +1.8 | 579 | 0% | 80y | 20/1000 | old age |

_Social indicators @ yr 10:_

| xenotype | contentment | housing | substance use | dependency ratio |
|---|---|---|---|---|
| Baseliner | 86% | 91% | 5% | 1.18 |

**Cohorts @ yr 100** (72 km², 18 tiles):

| xenotype | share | education I/P/S/U/Pg | strata E/M/U | female | gender-div | income/d | cost/d | net/d | assets | slaves | life exp | infant mort | leading cause |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Baseliner | 100% | 3/13/43/31/11 | 30/52/18 | 50% | 3% | 8.7 | 6.4 | +2.3 | 6514 | 0% | 79y | 31/1000 | old age |

_Social indicators @ yr 100:_

| xenotype | contentment | housing | substance use | dependency ratio |
|---|---|---|---|---|
| Baseliner | 88% | 91% | 3% | 1.16 |

## Empire conquered by a LIBERAL union at yr 5

> **CONQUEST at yr 5 by Union (liberal, no slavery):** elite −18% (17% died, 1% fled) → region adopts rigidity 0.25, slavery 0, tolerance 0.4.

**Region (population-weighted aggregates):**

| yr | Wealth | Balance | Health | Crime | Contentment | Substance use | Housing | Dependency | dev level | sanitation | pollution | Pop | density /km² | food self-suff |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 2 | 53% | 14% | 68% | 24% | 60% | 21% | 57% | 0.84 | 2.6 | 19% | 10% | 1014 | 9.1 | 4.95x |
| 10 | 64% | 24% | 86% | 4% | 82% | 5% | 80% | 1.03 | 3.2 | 33% | 10% | 1116 | 10.0 | 6.31x |
| 100 | 71% | 44% | 97% | 1% | 92% | 2% | 90% | 1.16 | 3.7 | 48% | 10% | 8588 | 76.7 | 0.99x |

**Cohorts @ yr 2** (112 km², 28 tiles):

| xenotype | share | education I/P/S/U/Pg | strata E/M/U | female | gender-div | income/d | cost/d | net/d | assets | slaves | life exp | infant mort | leading cause |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Baseliner | 70% | 3/14/44/29/9 | 27/53/20 | 50% | 3% | 3.9 | 4.4 | -0.5 | 14 | 5% | 62y | 98/1000 | disease |
| Highmate | 8% | 1/8/38/36/16 | 38/42/20 | 50% | 3% | 4.5 | 4.5 | +0.0 | 52 | 5% | 63y | 114/1000 | disease |
| Genie | 8% | 3/13/43/31/10 | 30/50/20 | 50% | 3% | 4.0 | 4.4 | -0.4 | 22 | 5% | 62y | 97/1000 | disease |
| Sanguophage | 2% | 2/10/40/34/14 | 35/45/20 | 50% | 3% | 4.3 | 4.4 | -0.2 | 41 | 5% | 785y | 95/1000 | disease |
| Hussar | 7% | 9/24/45/18/3 | 21/47/32 | 50% | 3% | 2.8 | 4.3 | -1.5 | 0 | 14% | 56y | 127/1000 | violence |
| Waster | 4% | 38/35/23/3/0 | 8/32/60 | 50% | 3% | 1.6 | 3.9 | -2.3 | 0 | 37% | 38y | 165/1000 | violence |
| Hybrid | 0% | 10/25/45/16/3 | 10/70/20 | 50% | 3% | 3.0 | 2.9 | +0.2 | 6 | 5% | 61y | 124/1000 | disease |

_Slavery: 7% of the population enslaved — of those: Baseliner 50%, Waster 22%, Hussar 14%, Highmate 6%, Genie 6%, Sanguophage 1%._

_Social indicators @ yr 2:_

| xenotype | contentment | housing | substance use | dependency ratio |
|---|---|---|---|---|
| Baseliner | 61% | 58% | 20% | 0.85 |
| Highmate | 67% | 62% | 17% | 0.87 |
| Genie | 62% | 59% | 19% | 0.85 |
| Sanguophage | 65% | 61% | 18% | 1.78 |
| Hussar | 51% | 50% | 26% | 0.76 |
| Waster | 34% | 38% | 42% | 0.32 |
| Hybrid | 62% | 54% | 19% | 0.84 |

**Cohorts @ yr 10** (112 km², 28 tiles):

| xenotype | share | education I/P/S/U/Pg | strata E/M/U | female | gender-div | income/d | cost/d | net/d | assets | slaves | life exp | infant mort | leading cause |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Baseliner | 69% | 1/9/39/36/16 | 36/46/18 | 50% | 3% | 6.8 | 5.2 | +1.6 | 259 | 0% | 73y | 53/1000 | old age |
| Highmate | 10% | 1/7/36/37/18 | 39/43/18 | 50% | 3% | 7.1 | 5.2 | +1.9 | 395 | 0% | 74y | 73/1000 | old age |
| Genie | 10% | 1/9/39/36/15 | 36/46/18 | 50% | 3% | 6.8 | 5.2 | +1.6 | 279 | 0% | 73y | 53/1000 | old age |
| Sanguophage | 2% | 1/7/36/37/18 | 39/43/18 | 50% | 3% | 7.1 | 5.2 | +1.9 | 365 | 0% | 919y | 53/1000 | disease |
| Hussar | 7% | 4/17/46/26/7 | 27/46/27 | 50% | 3% | 5.0 | 6.6 | -1.5 | 0 | 0% | 68y | 81/1000 | old age |
| Waster | 3% | 18/31/39/10/1 | 9/54/37 | 50% | 3% | 3.5 | 4.4 | -0.9 | 0 | 0% | 52y | 124/1000 | violence |
| Hybrid | 1% | 7/21/46/22/5 | 16/66/18 | 50% | 3% | 5.2 | 3.3 | +1.9 | 344 | 0% | 72y | 80/1000 | old age |

_Social indicators @ yr 10:_

| xenotype | contentment | housing | substance use | dependency ratio |
|---|---|---|---|---|
| Baseliner | 84% | 81% | 4% | 1.04 |
| Highmate | 85% | 83% | 3% | 1.06 |
| Genie | 83% | 81% | 4% | 1.04 |
| Sanguophage | 85% | 83% | 3% | 1.78 |
| Hussar | 66% | 71% | 12% | 0.95 |
| Waster | 59% | 63% | 18% | 0.51 |
| Hybrid | 83% | 72% | 5% | 1.02 |

**Cohorts @ yr 100** (112 km², 28 tiles):

| xenotype | share | education I/P/S/U/Pg | strata E/M/U | female | gender-div | income/d | cost/d | net/d | assets | slaves | life exp | infant mort | leading cause |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Baseliner | 52% | 1/7/36/37/18 | 40/42/18 | 50% | 3% | 9.6 | 6.1 | +3.5 | 9642 | 0% | 79y | 29/1000 | old age |
| Highmate | 17% | 1/7/36/37/18 | 40/42/18 | 50% | 3% | 9.6 | 6.1 | +3.5 | 9905 | 0% | 79y | 49/1000 | old age |
| Genie | 17% | 1/8/37/37/17 | 39/43/18 | 50% | 3% | 9.5 | 6.1 | +3.4 | 9128 | 0% | 79y | 29/1000 | old age |
| Sanguophage | 1% | 1/7/36/37/18 | 40/42/18 | 50% | 3% | 9.6 | 6.1 | +3.5 | 9875 | 0% | 982y | 29/1000 | malnutrition |
| Hussar | 6% | 5/17/46/26/7 | 27/46/27 | 50% | 3% | 8.1 | 7.4 | +0.7 | 1928 | 0% | 76y | 49/1000 | old age |
| Waster | 0% | 20/32/37/9/1 | 10/50/40 | 50% | 3% | 3.7 | 4.9 | -1.2 | 0 | 0% | 55y | 101/1000 | violence |
| Hybrid | 7% | 7/21/46/22/5 | 17/65/18 | 50% | 3% | 5.7 | 3.8 | +1.9 | 5465 | 0% | 78y | 51/1000 | old age |

_Social indicators @ yr 100:_

| xenotype | contentment | housing | substance use | dependency ratio |
|---|---|---|---|---|
| Baseliner | 93% | 91% | 1% | 1.16 |
| Highmate | 93% | 91% | 1% | 1.16 |
| Genie | 93% | 91% | 1% | 1.16 |
| Sanguophage | 93% | 91% | 1% | 1.78 |
| Hussar | 81% | 91% | 7% | 1.10 |
| Waster | 61% | 67% | 19% | 0.48 |
| Hybrid | 87% | 78% | 4% | 1.14 |

## Empire conquered by a HARSHER imperium at yr 5

> **CONQUEST at yr 5 by Imperium (harsher, slaving):** elite −18% (17% died, 1% fled) → region adopts rigidity 0.95, slavery 1, tolerance -0.2.

**Region (population-weighted aggregates):**

| yr | Wealth | Balance | Health | Crime | Contentment | Substance use | Housing | Dependency | dev level | sanitation | pollution | Pop | density /km² | food self-suff |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 2 | 53% | 14% | 68% | 24% | 60% | 21% | 57% | 0.84 | 2.6 | 19% | 10% | 1014 | 9.1 | 4.95x |
| 10 | 59% | 6% | 75% | 12% | 74% | 10% | 64% | 0.92 | 3.1 | 29% | 10% | 1025 | 9.1 | 4.33x |
| 100 | 62% | 0% | 85% | 9% | 73% | 10% | 71% | 1.01 | 3.3 | 36% | 10% | 3790 | 33.8 | 1.39x |

**Cohorts @ yr 2** (112 km², 28 tiles):

| xenotype | share | education I/P/S/U/Pg | strata E/M/U | female | gender-div | income/d | cost/d | net/d | assets | slaves | life exp | infant mort | leading cause |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Baseliner | 70% | 3/14/44/29/9 | 27/53/20 | 50% | 3% | 3.9 | 4.4 | -0.5 | 14 | 5% | 62y | 98/1000 | disease |
| Highmate | 8% | 1/8/38/36/16 | 38/42/20 | 50% | 3% | 4.5 | 4.5 | +0.0 | 52 | 5% | 63y | 114/1000 | disease |
| Genie | 8% | 3/13/43/31/10 | 30/50/20 | 50% | 3% | 4.0 | 4.4 | -0.4 | 22 | 5% | 62y | 97/1000 | disease |
| Sanguophage | 2% | 2/10/40/34/14 | 35/45/20 | 50% | 3% | 4.3 | 4.4 | -0.2 | 41 | 5% | 785y | 95/1000 | disease |
| Hussar | 7% | 9/24/45/18/3 | 21/47/32 | 50% | 3% | 2.8 | 4.3 | -1.5 | 0 | 14% | 56y | 127/1000 | violence |
| Waster | 4% | 38/35/23/3/0 | 8/32/60 | 50% | 3% | 1.6 | 3.9 | -2.3 | 0 | 37% | 38y | 165/1000 | violence |
| Hybrid | 0% | 10/25/45/16/3 | 10/70/20 | 50% | 3% | 3.0 | 2.9 | +0.2 | 6 | 5% | 61y | 124/1000 | disease |

_Slavery: 7% of the population enslaved — of those: Baseliner 50%, Waster 22%, Hussar 14%, Highmate 6%, Genie 6%, Sanguophage 1%._

_Social indicators @ yr 2:_

| xenotype | contentment | housing | substance use | dependency ratio |
|---|---|---|---|---|
| Baseliner | 61% | 58% | 20% | 0.85 |
| Highmate | 67% | 62% | 17% | 0.87 |
| Genie | 62% | 59% | 19% | 0.85 |
| Sanguophage | 65% | 61% | 18% | 1.78 |
| Hussar | 51% | 50% | 26% | 0.76 |
| Waster | 34% | 38% | 42% | 0.32 |
| Hybrid | 62% | 54% | 19% | 0.84 |

**Cohorts @ yr 10** (112 km², 28 tiles):

| xenotype | share | education I/P/S/U/Pg | strata E/M/U | female | gender-div | income/d | cost/d | net/d | assets | slaves | life exp | infant mort | leading cause |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Baseliner | 71% | 6/20/46/22/5 | 17/63/20 | 50% | 2% | 4.4 | 3.1 | +1.4 | 227 | 5% | 67y | 81/1000 | old age |
| Highmate | 10% | 3/15/45/29/9 | 26/54/20 | 50% | 2% | 5.0 | 4.8 | +0.2 | 170 | 5% | 68y | 97/1000 | old age |
| Genie | 9% | 7/21/46/22/5 | 17/63/20 | 50% | 2% | 4.4 | 3.1 | +1.4 | 246 | 5% | 67y | 81/1000 | old age |
| Sanguophage | 2% | 5/17/46/26/7 | 22/58/20 | 50% | 2% | 4.8 | 4.7 | +0.0 | 108 | 5% | 837y | 79/1000 | disease |
| Hussar | 6% | 19/32/39/10/1 | 8/58/33 | 50% | 2% | 3.0 | 4.4 | -1.4 | 0 | 15% | 59y | 113/1000 | violence |
| Waster | 2% | 38/35/23/3/0 | 6/9/85 | 50% | 2% | 1.4 | 4.0 | -2.6 | 0 | 62% | 31y | 156/1000 | xenophobia |
| Hybrid | 0% | 31/35/29/5/0 | 8/44/49 | 50% | 2% | 2.3 | 2.9 | -0.6 | 0 | 28% | 52y | 118/1000 | xenophobia |

_Slavery: 7% of the population enslaved — of those: Baseliner 53%, Waster 15%, Hussar 14%, Highmate 7%, Genie 7%, Hybrid 2%, Sanguophage 1%._

_Social indicators @ yr 10:_

| xenotype | contentment | housing | substance use | dependency ratio |
|---|---|---|---|---|
| Baseliner | 77% | 65% | 8% | 0.93 |
| Highmate | 70% | 68% | 11% | 0.95 |
| Genie | 77% | 65% | 8% | 0.93 |
| Sanguophage | 67% | 67% | 12% | 1.78 |
| Hussar | 53% | 54% | 21% | 0.79 |
| Waster | 27% | 34% | 49% | 0.14 |
| Hybrid | 47% | 47% | 30% | 0.39 |

**Cohorts @ yr 100** (112 km², 28 tiles):

| xenotype | share | education I/P/S/U/Pg | strata E/M/U | female | gender-div | income/d | cost/d | net/d | assets | slaves | life exp | infant mort | leading cause |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Baseliner | 61% | 5/17/46/25/7 | 22/58/20 | 50% | 2% | 5.2 | 5.2 | +0.0 | 3018 | 5% | 71y | 59/1000 | old age |
| Highmate | 23% | 3/13/43/31/10 | 30/50/20 | 50% | 2% | 5.8 | 5.2 | +0.5 | 1307 | 5% | 72y | 74/1000 | old age |
| Genie | 16% | 6/19/46/23/5 | 19/61/20 | 50% | 2% | 5.0 | 3.3 | +1.7 | 4629 | 5% | 71y | 61/1000 | old age |
| Sanguophage | 0% | 5/18/46/24/6 | 21/59/20 | 50% | 2% | 5.1 | 3.4 | +1.8 | 4308 | 5% | 896y | 59/1000 | disease |
| Hussar | 0% | 23/33/35/8/1 | 9/53/39 | 50% | 2% | 3.1 | 4.6 | -1.5 | 0 | 19% | 61y | 97/1000 | violence |
| Hybrid | 0% | 34/35/26/4/0 | 8/39/53 | 50% | 2% | 2.4 | 3.2 | -0.8 | 0 | 32% | 53y | 103/1000 | xenophobia |

_Slavery: 5% of the population enslaved — of those: Baseliner 59%, Highmate 22%, Genie 15%, Hybrid 2%, Hussar 2%._

_Social indicators @ yr 100:_

| xenotype | contentment | housing | substance use | dependency ratio |
|---|---|---|---|---|
| Baseliner | 70% | 71% | 11% | 1.00 |
| Highmate | 75% | 74% | 9% | 1.02 |
| Genie | 82% | 70% | 6% | 1.00 |
| Sanguophage | 82% | 70% | 6% | 1.78 |
| Hussar | 53% | 55% | 23% | 0.74 |
| Hybrid | 47% | 48% | 32% | 0.36 |

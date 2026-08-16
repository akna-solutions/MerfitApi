# Merfit Frontend Analysis — Backend Reference

Source repository analyzed: `merfitnativeapp` (React Native / Expo, expo-router ~6.0.24, Expo SDK ~54, React 19 / RN 0.81).
All paths below are relative to `src/` in that repository unless stated otherwise.

This document is a precise extraction of every type, mock data shape, enum, scoring rule, and
navigation/flow behavior needed to design the ASP.NET Core backend (C# domain entities, DTOs,
and REST endpoints) that will replace the frontend's mock data layer. Nothing here is invented;
where the frontend is ambiguous or silent, it is flagged explicitly as **AMBIGUOUS**.

---

## 1. Screens

All screens live under `app/pages/<name>/` and are registered via expo-router file-based routing
(`app/pages/<name>/index.tsx` unless noted).

| Route | File | Purpose |
|---|---|---|
| `/pages/welcome` | `app/pages/welcome/index.tsx` | Landing screen with background video; entry point (`/` redirects here). Buttons: "Get Started" → onboarding, "Login" → login. |
| `/pages/login` | `app/pages/login/index.tsx` → `LoginScreen.tsx` | Email/password login form + "Continue with Google" (both currently mocked/no-op) and link to onboarding sign-up. |
| `/pages/onboarding` | `app/pages/onboarding/index.tsx` → `OnboardingContainer.tsx` | 11-step wizard collecting the user profile, ending in account creation (email/password) and a success screen. |
| `/pages/dashboard` | `app/pages/dashboard/index.tsx` → `DashboardScreen.tsx` | Home tab: today's workout, today's progress, quick stats, goal progress, AI workout upsell, recommended workouts. |
| `/pages/workouts` | `app/pages/workouts/index.tsx` → `WorkoutsScreen.tsx` | Workout library/catalog: search, category filter, advanced filters, featured workout, personalized recommendations, AI plan upsell, full grid. |
| `/pages/workout/active/[id]` | `app/pages/workout/active/[id].tsx` → `ActiveWorkout.tsx` | Dynamic route: live workout session execution (exercise → rest → next exercise → complete → summary). |
| `/pages/progress` | `app/pages/progress/index.tsx` → `ProgressScreen.tsx` | Progress tab: score card (links to leaderboard), current weight, overview stats, weight chart, goal progress, performance trends (Plus), personal insights (Plus), body metrics, weekly activity, recent activity, motivational card. |
| `/pages/nutrition` | `app/pages/nutrition/index.tsx` → `NutritionScreen.tsx` | Nutrition tab: date selector, calorie card, macro overview, AI nutrition plan (Plus), meals by type, water tracker, calorie breakdown, nutrition insight/suggestions. |
| `/pages/leaderboard` | `app/pages/leaderboard/index.tsx` → `LeaderboardScreen.tsx` | Leaderboard: header (rank/points/league), rewards section w/ countdown, period filter, top-10 list, nearby users, advanced insights (Plus), score breakdown (Plus), score chart (Plus), achievements, motivation card. |
| `/pages/profile` | `app/pages/profile/index.tsx` → `ProfileScreen.tsx` | Profile tab: identity, stats, fitness goal, Plus status card, body stats, training profile, preference/settings menu lists, logout. |

Bottom-nav tabs (`home`, `workouts`, `progress`, `nutrition`, `profile`) map to
`/pages/dashboard`, `/pages/workouts`, `/pages/progress`, `/pages/nutrition`, `/pages/profile` respectively.

Additional routes referenced by `router.push(...)` but **not yet implemented** (no matching file under `app/pages/`) — these are placeholders the backend should anticipate:
`/pages/profile/edit`, `/pages/profile/goal`, `/pages/profile/workout-preferences`,
`/pages/profile/nutrition-preferences`, `/pages/profile/notifications`,
`/pages/profile/appearance`, `/pages/profile/units`, `/pages/profile/language`,
`/pages/profile/personal-information`, `/pages/profile/security`, `/pages/profile/privacy`,
`/pages/profile/help`.

---

## 2. Navigation

- Router: **expo-router** (file-based), root layout at `app/_layout.tsx`:
  ```tsx
  <SafeAreaProvider>
    <PlusProvider>
      <Stack screenOptions={{ headerShown: false }} />
    </PlusProvider>
  </SafeAreaProvider>
  ```
  A single un-configured `<Stack>` — there is **no** `Tabs` navigator and no route groups
  (no `(auth)` / `(tabs)` folders). All screens are plain stack routes under `app/pages/*`.
- `app/index.tsx` simply does `<Redirect href="/pages/welcome" />` — the app always boots to Welcome.
- **Bottom tab bar is hand-rolled**, not expo-router `Tabs`: `dashboard/components/BottomNavigation.tsx`
  renders a floating capsule and each screen manages its own `activeTab` state, navigating via
  `router.push(/pages/${tab})`. Tab id → route: `home→dashboard`, `workouts→workouts`,
  `progress→progress`, `nutrition→nutrition`, `profile→profile`.
- **Auth flow (as implemented, all mocked)**: Welcome → (Get Started) → Onboarding (11 steps,
  ends in email/password "Create account", currently `setShowSuccess(true)` locally, no network
  call) → SuccessScreen ("Start Training") → `router.replace('/pages/dashboard')`.
  Alternatively Welcome → (Login) → LoginScreen → `router.replace('/pages/dashboard')` on submit
  (no real request; any valid-looking email + 6+ char password succeeds).
  Logout (Profile screen) → `router.replace('/pages/welcome')`. There is currently **no token
  storage, no session persistence, and no auth guard** on any route — any deep link works whether
  or not onboarding/login occurred.
- **Dynamic route** `/pages/workout/active/[id]`: `[id].tsx` reads `id` and optional `title` from
  `useLocalSearchParams<{ id: string; title?: string }>()` and renders
  `<ActiveWorkout workoutId={id} workoutTitle={title} />`. Both `dashboard` and `workouts`
  navigate here via
  ```ts
  router.push({ pathname: "/pages/workout/active/[id]", params: { id, title } })
  ```
  The `id` is expected to resolve to a workout/session; the mock layer (`workout/mockData.ts`)
  falls back to a generic default session if the id isn't found in its lookup table, so the
  real backend must guarantee a valid session/plan for any workout id reachable from the UI.

---

## 3. User flows

1. **Onboarding completion**: 11 sequential steps build a single `OnboardingData` object client-side
   (see §4). Step 11 ("Create your MERFIT account") validates email format, password length ≥ 6,
   and password === confirmPassword, then calls `handleCreateAccount()` — currently just flips
   `showSuccess`, no request. Real backend needs a `POST` that creates the user account **and**
   persists the full onboarding profile in one flow (register + profile creation), returning
   whatever auth token the app should store, then routes to Dashboard.
2. **Starting a workout**: From Dashboard ("today's workout" card) or Workouts screen (featured /
   grid item) → `router.push('/pages/workout/active/[id]', { id, title })` → `ActiveWorkout`
   fetches/derives a `WorkoutSession` (currently `getWorkoutSession(id, title)` from mock data).
3. **Completing a workout** (see §6 for full mechanics): user logs weight+reps per set → rest timer
   → repeat until all exercises/sets done → `WorkoutComplete` screen (duration, exercise count, set
   count) → `WorkoutSummary` screen (exercises, sets, estimated calories, total volume, any PRs) →
   "Done" → `router.replace('/pages/progress')` (from ActiveWorkout) or `router.replace('/pages/workouts')`
   (from WorkoutComplete's "Done" without viewing summary). Exiting mid-workout via the exit modal
   ("Save & Exit") is a TODO — no persistence exists yet, it just calls `router.back()`.
4. **Logging a meal**: NutritionScreen → tap "add" on a meal-type row → `AddMealModal` opens showing
   `MOCK_FOODS` (id/name/calories) → selecting a food appends a new `MealEntry` to local state
   (`id: "${food.id}-${Date.now()}"`, `type`, `name`, `calories`). No macros/quantity are captured
   per meal item — only a running calorie total per meal name. No network call exists.
5. **Viewing progress**: ProgressScreen renders `ProgressData` (mock) + separately imports
   `CURRENT_USER_ENTRY`/`MOCK_CURRENT_USER` from the **leaderboard** module for the score card —
   i.e. progress screen currently cross-imports leaderboard mock data directly (see §12, flag).
   Time-range selector (`week|month|3months|year`) re-slices `weightHistory` client-side via
   `getWeightHistoryForRange`.
6. **Viewing leaderboard**: `LeaderboardScreen` shows a 450 ms artificial loading skeleton, then
   header, rewards countdown, period filter (`week|month|allTime` — currently **not wired to any
   data change**, see TODO comment), top-10 list, nearby users, Plus-gated sections
   (`ADVANCED_LEADERBOARD`, `DETAILED_SCORE` ×2), achievements, motivation card.
7. **Upgrading to Plus**: Any `PremiumFeature`-wrapped section renders dimmed + `LockedOverlay`;
   tapping it calls `usePlus().openPlusModal(feature)` → `PlusPurchaseModal` shown (global, mounted
   once in `_layout.tsx` via `PlusProvider`) → user picks `monthly`/`yearly` plan → "Continue with
   Plus" → `subscriptionService.purchasePlus(plan)` → on success sets `isPlusUser = true` app-wide.
   "Restore Purchase" calls `subscriptionService.restorePurchases()`. Both currently backed by
   `MockSubscriptionService` (in-memory, `setTimeout`-delayed, no real payment).

---

## 4. Onboarding fields

Source: `app/pages/onboarding/types.ts` (canonical `OnboardingData` shape) + `steps/*.tsx` (exact
option lists/labels shown to the user) + `OnboardingContainer.tsx` (order, validation, step titles).
11 steps total, in this exact order:

| Step | Field(s) set | Type | Validation | UI options (label → stored value) |
|---|---|---|---|---|
| 1. Name | `name` | `string` | non-empty (trimmed) | free text |
| 2. Gender | `gender` | `Gender \| null` = `"male" \| "female"` | required | "Male"→`male`, "Female"→`female` |
| 3. Age | `age` | `string` (digits only, numeric input) | `13 <= age <= 100` (parsed as int) | free text, max 3 digits |
| 4. Height | `height`, `heightUnit`, `heightFeet`, `heightInches` | `height: string`, `heightUnit: "cm"\|"ft_in"`, `heightFeet: string`, `heightInches: string` | if `cm`: `height` non-empty; if `ft_in`: `heightFeet` non-empty | unit toggle "cm" / "ft / in"; cm max 3 digits, feet max 1 digit, inches max 2 digits |
| 5. Weight | `weight`, `weightUnit` | `weight: string`, `weightUnit: "kg"\|"lb"` | `weight` non-empty | unit toggle "kg" / "lb"; max 5 chars, allows decimal |
| 6. Goal | `goal` | `Goal \| null` | required | "Lose weight"→`lose_weight`, "Build muscle"→`build_muscle`, "Get stronger"→`get_stronger`, "Improve fitness"→`improve_fitness`, "Maintain my weight"→`maintain_weight`, "Improve endurance"→`improve_endurance` |
| 7. Activity level | `activityLevel` | `ActivityLevel \| null` | required | "Mostly sedentary" (Little to no exercise)→`sedentary`, "Lightly active" (Light exercise 1-2 days/week)→`light`, "Moderately active" (Moderate exercise 3-4 days/week)→`moderate`, "Very active" (Hard exercise 5-6 days/week)→`active`, "Athlete" (Intense training almost daily)→`athlete` |
| 8. Training experience | `trainingExperience` | `TrainingExperience \| null` | required | "Beginner" (I'm new to structured training)→`beginner`, "Intermediate" (I've been training consistently)→`intermediate`, "Advanced" (I have significant training experience)→`advanced` |
| 9. Training frequency | `trainingDays` | `number \| null` | required (truthy) | "2 days / week"→`2`, "3 days / week"→`3`, "4 days / week"→`4`, "5 days / week"→`5`, "6 days / week"→`6`, "Every day"→`7` |
| 10. Location + equipment | `trainingLocation`, `equipment` | `trainingLocation: TrainingLocation \| null`, `equipment: Equipment[]` | `trainingLocation` required; equipment picker only shown after location chosen | Location: "Gym"→`gym`, "Home"→`home`, "Outdoor"→`outdoor`. Equipment (multi-select, `none` is exclusive of all others): "Dumbbells"→`dumbbells`, "Barbell"→`barbell`, "Resistance bands"→`bands`, "Machines"→`machines`, "Pull-up bar"→`pullup_bar`, "Kettlebell"→`kettlebell`, "No equipment"→`none` |
| 11. Account | `email`, `password`, `confirmPassword` | all `string` | email regex `^[^\s@]+@[^\s@]+\.[^\s@]+$`, password length ≥ 6, `password === confirmPassword` | email/password/confirm password fields + "Continue with Google" (no-op) |

Full `OnboardingData` type (verbatim from `onboarding/types.ts`):
```ts
export type Gender = "male" | "female";
export type HeightUnit = "cm" | "ft_in";
export type WeightUnit = "kg" | "lb";
export type Goal = "lose_weight" | "build_muscle" | "get_stronger" | "improve_fitness" | "maintain_weight" | "improve_endurance";
export type ActivityLevel = "sedentary" | "light" | "moderate" | "active" | "athlete";
export type TrainingExperience = "beginner" | "intermediate" | "advanced";
export type TrainingLocation = "gym" | "home" | "outdoor";
export type Equipment = "dumbbells" | "barbell" | "bands" | "machines" | "pullup_bar" | "kettlebell" | "none";

export type OnboardingData = {
  name: string;
  gender: Gender | null;
  age: string;
  height: string;
  heightUnit: HeightUnit;
  heightFeet: string;
  heightInches: string;
  weight: string;
  weightUnit: WeightUnit;
  goal: Goal | null;
  activityLevel: ActivityLevel | null;
  trainingExperience: TrainingExperience | null;
  trainingDays: number | null;
  trainingLocation: TrainingLocation | null;
  equipment: Equipment[];
  email: string;
  password: string;
  confirmPassword: string;
};
```

**Note (AMBIGUOUS / naming drift)**: `profile/types.ts` and `progress/types.ts` and
`nutrition/types.ts` each define their **own, differently-cased** parallel enums
(`Goal = "Build Muscle" | "Lose Weight" | ...`, `GoalType = "lose_weight" | ...`,
`Experience = "Beginner" | ...`, `ActivityLevel = "Sedentary" | ...`) that are not the same
TypeScript types as onboarding's, even though comments say they "correspond 1:1" to onboarding
data. The backend should pick one canonical representation (snake_case enum keys, as in
onboarding) and translate to display labels in DTOs/view-models rather than mirroring three
divergent casing conventions.

---

## 5. Workout model (catalog / library)

Source: `app/pages/workouts/types.ts`

```ts
export type Difficulty = "Beginner" | "Intermediate" | "Advanced";

export type Category =
  | "Strength" | "Cardio" | "HIIT" | "Mobility" | "Core" | "Upper Body" | "Lower Body";

export type MuscleGroup = "Full Body" | "Upper Body" | "Lower Body" | "Core";

export type Equipment =
  | "No equipment" | "Dumbbells" | "Barbell" | "Machines" | "Resistance bands";

export type Workout = {
  id: string;
  title: string;
  tagline?: string;
  duration: number;          // minutes
  difficulty: Difficulty;
  category: Category;
  muscleGroup: MuscleGroup;
  equipment: Equipment[];
  imageUrl: string;
  featured?: boolean;
};

export type DurationRange = "under20" | "20to40" | "40plus";

export type WorkoutFilters = {
  difficulty: Difficulty[];
  duration: DurationRange[];
  equipment: Equipment[];
  muscleGroup: MuscleGroup[];
};
```

Note: this `Equipment` enum (5 display-label values, catalog-facing) is **distinct** from the
onboarding `Equipment` enum (7 snake_case values, user-inventory-facing) — see §12 mapping note.

`WorkoutFilters` filtering logic (from `WorkoutsScreen.tsx`, useful for backend query-param design):
category exact match; text search on `title` (case-insensitive substring); `difficulty`/`muscleGroup`
membership; `equipment` — workout matches if **any** of its equipment items is in the filter set;
`duration` ranges: `under20` = `duration < 20`, `20to40` = `20 <= duration <= 40`, `40plus` = `duration > 40`.

Session/exercise composition for a given workout (`app/pages/workout/types.ts`):
```ts
export type Exercise = {
  id: string;
  name: string;
  sets: number;
  reps: number;
  restSec: number;
  video: number | string | null; // require() asset id or remote URI; null = no video yet
  imageUrl: string;
};

export type WorkoutSession = {
  id: string;
  title: string;
  exercises: Exercise[];
};
```
Each `Exercise` here carries a **prescribed** `sets`/`reps`/`restSec` (the plan), separate from the
per-set logs recorded during execution (§6).

---

## 6. Workout execution flow

Source: `app/pages/workout/types.ts`, `ActiveWorkout.tsx`, `WorkoutSummary.tsx`.

Core execution types (verbatim):
```ts
export type SetLog = {
  weight: number;
  reps: number;
};

export type ExerciseProgress = {
  currentSet: number;          // 1-indexed
  completedSets: SetLog[];
};

export type WorkoutPhase =
  | "exercise" | "rest" | "paused" | "complete" | "summary";
```

Client-side session state held in `ActiveWorkout.tsx` (not persisted anywhere yet — all `useState`):
- `exerciseIndex: number` — index into `session.exercises`
- `phase: WorkoutPhase`
- `progress: Record<exerciseId, ExerciseProgress>` — initialized to `{ currentSet: 1, completedSets: [] }` per exercise
- `weightInput: string`, `repsInput: string` — current set entry fields (reps defaults to the exercise's prescribed `reps` whenever the exercise changes; weight resets to empty)
- `restRemaining: number` (seconds, counts down every 1000ms while `phase === "rest"`), `restTotalSec` (ref, snapshot of `exercise.restSec` at rest start)
- `elapsedSec: number` — total workout timer, increments every second while phase is NOT `paused`/`complete`/`summary`
- `exitModalVisible: boolean`

Set-completion logic (`handleCompleteSet`):
1. Parses `weight = parseFloat(weightInput) || 0`, `reps = parseInt(repsInput) || exercise.reps`.
2. Appends `{ weight, reps }` to that exercise's `completedSets`.
3. If this was the exercise's last set **and** the exercise was the last in the session → `phase = "complete"`.
4. Otherwise starts rest: `restRemaining = exercise.restSec`, `phase = "rest"`; flags whether the
   next thing is a new exercise (`pendingAdvanceExercise`) or another set of the same exercise.

Rest-timer behavior: counts down 1s/tick; on reaching 0 auto-advances (`handleRestEnd`) — either
increments `exerciseIndex` (moving to next exercise) or increments `currentSet` for the same
exercise, then sets `phase = "exercise"`. `onAdjust(deltaSec)` lets the user add/subtract seconds
from `restRemaining` (never below 0); `onSkip` ends rest immediately.

Pause/resume: `handlePause` snapshots the current `phase` into a ref and sets `phase = "paused"`;
`handleResume` restores it. A `PauseOverlay` is shown; the elapsed timer and rest timer both stop
while paused.

Exit: `ExitWorkoutModal` offers "Continue" (dismiss) or "Save & Exit" — the latter currently just
navigates back with **no persistence** (explicit TODO: *"workout progress'i (exerciseIndex,
progress, elapsedSec) AsyncStorage veya backend'e kaydet"* — save exerciseIndex/progress/elapsedSec
so the user can resume). This is a concrete required backend capability: **resumable/incomplete
workout sessions**.

Completion → `WorkoutComplete` screen fields: `durationLabel` (formatted `elapsedSec` as `m:ss`),
`exerciseCount` (`session.exercises.length`), `setCount` (total completed sets across all exercises).

Summary (`WorkoutSummary.tsx`) — computed entirely client-side from `progress`:
- `setCount` = sum of `completedSets.length` across exercises
- `volume` = sum over all completed sets of `weight * reps`
- `estimatedCalories` = `Math.round(setCount * 17.5)` (**explicitly a placeholder mock formula** — comment says "gerçek hesaplama backend'e taşınacak" i.e. real calculation must move to backend)
- `personalRecords`: for each exercise, compares the completed sets' **top set by weight** against
  `MOCK_PREVIOUS_BEST[exerciseId] = { weight, reps }` (currently a hardcoded 2-entry map for
  `squat` and `bench-press` only); if `topSet.weight > best.weight`, it's a new PR shown as
  `{ exerciseName, weight, reps }`. **The backend must own real "previous best per exercise per
  user" tracking** to make this general.

Props passed into `WorkoutSummary` component (defines exactly what a "completed session" payload
needs to render): `title`, `durationLabel`, `exercises: Exercise[]`, `progress: Record<string,
ExerciseProgress>`, `personalRecords: {exerciseName, weight, reps}[]`, `onDone`.

---

## 7. Nutrition model

Source: `app/pages/nutrition/types.ts`

```ts
export type MealType = "Breakfast" | "Lunch" | "Dinner" | "Snacks";
export const MEAL_TYPES: MealType[] = ["Breakfast", "Lunch", "Dinner", "Snacks"];

export type MealEntry = {
  id: string;
  type: MealType;
  name: string;
  calories: number;
};

export type MacroTarget = {
  consumed: number;
  target: number;
};

export type FoodItem = {
  id: string;
  name: string;
  calories: number;
};

export type NutritionData = {
  hasLoggedFirstMeal: boolean;
  dailyCalories: number;
  macros: {
    protein: MacroTarget;
    carbs: MacroTarget;
    fats: MacroTarget;
  };
  water: {
    consumedL: number;
    targetL: number;
  };
  meals: MealEntry[];
};
```

Notes:
- `MealEntry` has **no timestamp, no date field, no per-food-item breakdown, no quantity/serving
  size, no macro grams** — just a flat name + total calories per logged meal. If per-food macro
  detail is desired server-side, that's new ground the frontend types don't currently model.
- `FoodItem` (the food-search/catalog entity used in "Add Meal") also has only `id`, `name`,
  `calories` — no macros, no serving unit. The mock catalog (`MOCK_FOODS`) has 8 hardcoded foods.
- Water tracking is a single running liters total per day vs. a target (`consumedL`/`targetL`);
  "Add water" increments by a fixed `+0.25L` per tap, capped at `targetL`.
- `dailyCalories` and macro `target`s are stated (in code comments) to be **computed server-side**
  from the onboarding profile (goal/age/height/weight/activityLevel/trainingFrequency) — this is
  explicitly a backend responsibility, not client logic.
- No explicit date field on `NutritionData` itself; `NutritionScreen` keeps a local `selectedDate`
  and a TODO notes that changing the date should re-fetch from the backend (currently a no-op).

---

## 8. Progress model

Source: `app/pages/progress/types.ts`

```ts
export type GoalType =
  | "lose_weight" | "build_muscle" | "get_stronger"
  | "improve_fitness" | "maintain_weight" | "improve_endurance";

export type WeightPoint = { date: string; weight: number };

export type TimeRange = "week" | "month" | "3months" | "year";

export type RecentActivityItem = {
  id: string;
  title: string;
  durationMin: number;
  dateLabel: string; // "Today" / "Yesterday" / "Aug 12"
};

export type BodyMetric = {
  id: string;
  label: string;
  value: string; // pre-formatted, e.g. "78.4 kg", "17.8%"
};

export type ProgressData = {
  hasCompletedFirstWorkout: boolean;

  goalType: GoalType;
  goalLabel: string;
  goalPercent?: number;            // for build_muscle / get_stronger / improve_endurance
  workoutsCompleted?: number;      // for improve_fitness
  workoutsGoal?: number;           // for improve_fitness

  currentWeight: number;
  startingWeight: number;
  targetWeight: number;
  monthlyChange: number;

  workouts: number;
  calories: number;
  streak: number;
  trainingMinutes: number;

  weeklyWorkouts: boolean[];       // Mon..Sun, length 7 — did-they-work-out-that-day
  weightHistory: WeightPoint[];
  bodyMetrics: BodyMetric[];
  recentActivity: RecentActivityItem[];
};
```

Notes:
- `goalPercent` vs. `workoutsCompleted`/`workoutsGoal` are **mutually exclusive presentation modes**
  depending on `goalType` — the `GoalProgress` component branches on this. Backend should compute
  the correct one(s) per user's goal type.
- `BodyMetric.value` is a pre-formatted display string (unit baked in), not a raw number — the
  mock only populates `weight` and `body-fat`; the `id` field suggests this is meant to be an
  open-ended list (backend could add more metric types by `id`).
- `getWeightHistoryForRange` client util slices differently per range (`week`→last 2 points,
  `month`→last 4, `3months`/`year`→all) — this is purely a mock-data hack (fixed slice counts, not
  actual date-range filtering) and should **not** be mirrored server-side; a real API should filter
  `weightHistory` by actual date range for the given `TimeRange`.
- `ProgressScreen` also imports `MOCK_CURRENT_USER`/`CURRENT_USER_ENTRY` directly from the
  leaderboard mock module for its `ScoreCard` (points, weeklyChange, rank) — i.e. progress
  displays leaderboard/score data inline. Backend dashboard/progress aggregation should include
  score summary fields alongside progress fields.

---

## 9. Leaderboard model

Source: `app/pages/leaderboard/types.ts` and `app/pages/leaderboard/scoreRules.ts`

```ts
export type LeaderboardUser = {
  id: string;
  name: string;
  points: number;
  workouts: number;
  isCurrentUser?: boolean;
};

export type LeaderboardEntry = LeaderboardUser & { rank: number };

export type CurrentUserSummary = {
  id: string;
  name: string;
  points: number;
  weeklyChange: number;
  bestRank: number;
  bestRankMonthLabel: string;   // e.g. "August 2026"
  league: string;               // e.g. "Silver"
  leaderboardVisible: boolean;  // opt-out-of-leaderboard flag
};

export type PeriodFilter = "week" | "month" | "allTime";

export type ScoreHistoryPoint = { label: string; points: number };

export type ScoreBreakdownItem = { id: string; label: string; points: number };

export type AchievementIcon = "streak" | "workouts" | "pr" | "weekly";

export type Achievement = {
  id: string;
  title: string;
  description: string;
  icon: AchievementIcon;
  earned: boolean;
};

export type RewardIcon =
  | "watch" | "premium" | "bag" | "shoes" | "training"
  | "shaker" | "apparel" | "membership";

export type Reward = {
  rank: number;      // the leaderboard rank this reward is tied to (1..10 in mock)
  title: string;
  description: string;
  icon: RewardIcon;
};
```

**Scoring rules — verbatim from `scoreRules.ts`** (explicit comment: "İleride backend'e taşınacak" — to be moved to backend):
```ts
export const SCORE_RULES = {
  workoutCompleted: 100,
  personalRecord: 150,
  sevenDayStreak: 200,
  thirtyDayStreak: 500,
  nutritionGoalCompleted: 50,
  weeklyGoalCompleted: 200,
} as const;
```
| Rule key | Points |
|---|---|
| `workoutCompleted` | 100 |
| `personalRecord` | 150 |
| `sevenDayStreak` | 200 |
| `thirtyDayStreak` | 500 |
| `nutritionGoalCompleted` | 50 |
| `weeklyGoalCompleted` | 200 |

Rank is **never hardcoded** in the frontend — mock data explicitly computes it by sorting users by
`points` descending and assigning sequential ranks starting from a given `startRank`
(`withComputedRank` helper). This confirms rank must be a server-computed, always-fresh value
(e.g. a window/rank function over points), not a stored column that can drift.

`SCORE_BREAKDOWN` mock categories (labels only — not point-rule keys, these are aggregate buckets
shown to the user, distinct from `SCORE_RULES`): `Workout Consistency`, `Workout Completion`,
`Progress`, `Nutrition Consistency`, `Streak`, `Challenges` — with mock point totals 3200 / 2400 /
1200 / 900 / 520 / 200 respectively (sums to the mock user's 8420 total points). **AMBIGUOUS**:
the exact formula mapping `SCORE_RULES` event points into these 6 breakdown buckets is not defined
anywhere in the frontend — this is backend design work, not extractable from mocks.

`REWARDS` mock: 10 entries, one per rank 1–10, each with a title/description/icon (smartwatch,
premium subscription vouchers of varying duration, gym bag, shoes voucher, personal training,
protein shaker, apparel voucher, membership voucher). `REWARDS_RESET_AT` is a countdown timestamp
(mock: now + 12 days 8h 42m) — real backend needs a defined reward-period reset schedule (weekly?
monthly? — **AMBIGUOUS**, not specified beyond the mock countdown).

Achievements mock (4 entries, `earned: boolean`): "7 Day Streak", "10 Workouts", "First PR",
"5 Workouts This Week" (partially earned in mock, `earned: false`, description "3 of 5 completed"
— suggesting achievements may need progress-fraction display, not just a boolean, though the type
only has `earned: boolean` today).

`TOTAL_RANKED_USERS = 2280` (mock universe size) is used purely to compute a display "Top X%" —
backend should compute this as `(rank / totalUsersWithScore) * 100`.

---

## 10. Plus features

Source: `shared/plus/types.ts`, `shared/plus/featureRegistry.ts`,
`shared/plus/subscription/SubscriptionService.ts`, `MockSubscriptionService.ts`, `PlusContext.tsx`.

Membership type: `export type Membership = "FREE" | "PLUS";`

**`PlusFeature` enum — verbatim, 7 members**, each gates a specific screen section:
```ts
export type PlusFeature =
  | "ADVANCED_PROGRESS"
  | "AI_WORKOUT"
  | "AI_NUTRITION"
  | "PERSONAL_INSIGHTS"
  | "ADVANCED_ANALYTICS"
  | "DETAILED_SCORE"
  | "ADVANCED_LEADERBOARD";
```

`PLUS_FEATURES` registry (title + description shown in the lock overlay / purchase modal), and
where each is actually used in the UI:

| PlusFeature | Title | Description | Used in |
|---|---|---|---|
| `ADVANCED_PROGRESS` | Advanced Progress | Understand your performance in detail. | *(registered but not referenced by any `<PremiumFeature feature="ADVANCED_PROGRESS">` in the screens read — reserved/unused today)* |
| `AI_WORKOUT` | AI Workout Plan | Get a workout plan built around your goals and progress. | Dashboard `AiWorkoutCard`, Workouts `AiWorkoutPlanCard` |
| `AI_NUTRITION` | AI Nutrition Plan | Get personalized meals and macro targets for your goal. | Nutrition `AiNutritionPlanCard` |
| `PERSONAL_INSIGHTS` | Personal Insights | Get personalized insights based on your progress. | Progress `PersonalInsights` |
| `ADVANCED_ANALYTICS` | Advanced Analytics | Unlock detailed performance trends and analytics. | Progress `PerformanceTrends` |
| `DETAILED_SCORE` | Detailed MERFIT Score | See exactly how your MERFIT score is calculated. | Leaderboard `ScoreBreakdown`, Leaderboard `ScoreChart` |
| `ADVANCED_LEADERBOARD` | Advanced Leaderboard Insights | See your rank history and detailed standing. | Leaderboard `AdvancedLeaderboardInsights` |

`PLUS_BENEFITS` (fixed marketing bullet list shown in purchase modal, not tied 1:1 to `PlusFeature`
keys): "Advanced Progress Analytics", "AI Workout Plans", "AI Nutrition Plans",
"Personal Insights", "Detailed MERFIT Score", "Advanced Analytics".

**`SubscriptionService` interface** (the exact abstraction the backend/store integration must satisfy):
```ts
export type SubscriptionPlan = "monthly" | "yearly";

export type Product = {
  plan: SubscriptionPlan;
  price: string;      // mock — real price should come from App Store/Google Play
  currency: string;
  period: string;      // "per month" | "per year"
  badge?: string;       // e.g. "SAVE 20%"
};

export type SubscriptionStatus = {
  membership: "FREE" | "PLUS";
  renewsAt?: string;
};

export interface SubscriptionService {
  getProducts(): Promise<Product[]>;
  purchasePlus(plan: SubscriptionPlan): Promise<void>;
  restorePurchases(): Promise<SubscriptionStatus>;
  getCurrentSubscription(): Promise<SubscriptionStatus>;
}
```

`MockSubscriptionService` (current implementation, in-memory only, `DO NOT USE FAKE PAYMENT`
comment explicit that this is UI-state testing only):
- `getProducts()` → 200ms delay → returns 2 hardcoded products: `{plan:"monthly", price:"9.99", currency:"USD", period:"per month"}` and `{plan:"yearly", price:"79.99", currency:"USD", period:"per year", badge:"SAVE 20%"}`.
- `purchasePlus(plan)` → 1200ms delay → sets in-memory `membership = "PLUS"` (TODO: real App Store / Google Play purchase flow).
- `restorePurchases()` → 800ms delay → returns current in-memory status (TODO: query real store).
- `getCurrentSubscription()` → 150ms delay → returns current in-memory status.

`PlusContext`/`PlusProvider` (mounted once at app root): on mount calls
`getCurrentSubscription()` to seed `isPlusUser` (comment: "gerçek backend/store bağlandığında bu
ilk senkronizasyon `GET /subscription`'a dönüşecek" — this becomes `GET /subscription`). Exposes
`openPlusModal(feature?)`, `closePlusModal()`, `isPlusUser`, and a dev-only `setPlusUser(bool)`.
Purchase success in the modal calls `onPurchased` → `setIsPlusUser(true)` client-side (no re-fetch).

Implication for backend: needs endpoints mirroring `getProducts` / `purchasePlus` / `restorePurchases`
/ `getCurrentSubscription`, plus real store-receipt validation (StoreKit/Google Play Billing) since
the mock explicitly stands in for that.

---

## 11. Existing mock data

| File | Shape summary |
|---|---|
| `dashboard/mockData.ts` (`MOCK_DASHBOARD_DATA: DashboardData`) | 1 user ("Mert"), `hasCompletedFirstWorkout: true`, today's progress (3/5 workouts, 420 kcal, 6240 steps, 7.3h sleep), 1 today-workout card, 4 quick stats (weight/streak/calories/workouts, each with an icon key), goal progress (78.4→75kg from 84kg start), 3 recommended workouts. |
| `leaderboard/mockData.ts` | `MOCK_CURRENT_USER` (points 8420, weeklyChange +240, bestRank 92, league "Silver"); 10 `TOP_USERS_RAW` + 6 `NEARBY_USERS_RAW` (including the current user), both rank-computed via sort-by-points; `SCORE_BREAKDOWN` (6 categories); `SCORE_HISTORY` (7 daily points, Mon–Sun); `ACHIEVEMENTS` (4); `REWARDS` (10, rank 1–10); `REWARDS_RESET_AT` computed countdown; `TOTAL_RANKED_USERS = 2280`. |
| `nutrition/mockData.ts` | `MOCK_NUTRITION_DATA`: 3200 kcal target, macros (protein 120/160g, carbs 180/300g, fats 55/80g), water 1.5/3L, 3 meals (Breakfast/Lunch/Snacks). `MOCK_FOODS`: 8 foods with id/name/calories only. |
| `profile/mockData.ts` (`MOCK_PROFILE_DATA: ProfileData`) | firstName "Mert", username "@mert", email, age 31, height 180cm, weight 78.4kg, goal "Build Muscle" + description, experience "Beginner", activityLevel "Moderate", trainingDays 4, workoutLocation "Home", equipment ["Dumbbells","Resistance Bands"], stats (workouts 18, streak 12, weight 78.4), unitSystem "metric". |
| `progress/mockData.ts` (`MOCK_PROGRESS_DATA: ProgressData`) | goalType "lose_weight", weight 78.4 current / 82 starting / 75 target, monthlyChange -2.6, workouts 18, calories 6420, streak 12, trainingMinutes 780, weeklyWorkouts 7-bool array, 6-point weightHistory (Jun 1 – Aug 15), 2 bodyMetrics (weight, body-fat%), 3 recentActivity items. Also exports `getWeightHistoryForRange` helper. |
| `workout/mockData.ts` | `MOCK_WORKOUT_SESSIONS`: 3 keyed sessions (`full-body-strength`, `upper-body-power`, `quick-hiit`), each 3-4 exercises with sets/reps/restSec/video(null)/imageUrl. `getWorkoutSession(id, title?)` falls back to a 3-exercise default session for unknown ids. `MOCK_PREVIOUS_BEST`: hardcoded PR baseline for exactly 2 exercises (`squat`, `bench-press`). |
| `workouts/mockData.ts` | `MOCK_WORKOUTS`: 8 workouts spanning all categories/difficulties/muscle groups, 1 flagged `featured`. `MOCK_USER_PROFILE`: hardcoded stand-in for the onboarding profile (goal `build_muscle`, experience `beginner`, location `home`, equipment `[dumbbells, none]`) used by `getPersonalizedWorkouts()` to filter-by-difficulty-and-equipment-match (max 4 results). |

---

## 12. Backend-required fields (consolidated, non-obvious / flagged)

- **`OnboardingData.equipment`** vs **`workouts.Equipment`** vs **`profile.equipment: string[]`** —
  three different equipment vocabularies exist in the frontend (`dumbbells|barbell|bands|machines|
  pullup_bar|kettlebell|none` in onboarding; `"No equipment"|"Dumbbells"|"Barbell"|"Machines"|
  "Resistance bands"` display-cased in the workout catalog; and a loosely-typed `string[]` in
  `ProfileData`). Backend needs one canonical equipment reference table and mapping to whichever
  display casing each surface expects (there's already a hand-rolled `EQUIPMENT_KEY_MAP` in
  `workouts/mockData.ts` doing exactly this translation client-side today).
- **`Exercise.video: number | string | null`** — a `number` here is a bundled React Native asset id
  (`require(...)` result), only meaningful client-side; the backend should only ever emit a URL
  string (or null). Flag this so C# DTOs don't try to model the numeric variant.
- **`WorkoutSummary.imageUrl` / `Workout.imageUrl` / `WorkoutSession` exercise `imageUrl`** — all
  currently point to external Unsplash URLs in mock data; real backend needs owned media storage
  URLs.
- **`CurrentUserSummary.leaderboardVisible: boolean`** — an opt-out-of-leaderboard flag exists in
  the type but no UI toggle for it was found in the screens read; still needs a backing column and
  presumably a settings screen (not yet built — see the `/pages/profile/*` placeholder routes).
  **AMBIGUOUS** where this gets set.
- **`Reward.rank`** — rewards are tied to exact leaderboard rank 1–10 in mock data; real reward
  eligibility computation, and the reset cadence (`REWARDS_RESET_AT`), are undefined beyond "some
  countdown" — needs product decision.
- **`Achievement.earned` is boolean-only** — partial progress (e.g. "3 of 5 completed" for the
  "5 Workouts This Week" mock achievement) is only expressed as free text in `description`, not
  as a structured field — if partial progress is a real requirement, the type needs an additional
  numeric progress field.
- **`WorkoutSummary` PR detection** (`MOCK_PREVIOUS_BEST`) — only 2 exercises hardcoded; the real
  system needs a per-user, per-exercise "personal best" table updated after every session, with
  the comparison currently defined as **top set's weight only** (heaviest single set logged for
  that exercise in the session), reps of that same top set — not 1RM-estimated, not best-volume.
- **`ProgressData.goalPercent` vs `workoutsCompleted`/`workoutsGoal`** — presentation branches by
  `goalType`; the exact formula for `goalPercent` (e.g. how "build_muscle" progress % is computed)
  is not present anywhere in the frontend — pure backend/product logic to design.
- **`NutritionData.dailyCalories` / macro targets** — code comments state these must be derived
  server-side from the onboarding profile (goal/age/height/weight/activityLevel/trainingDays), but
  no formula (e.g. Mifflin-St Jeor, activity multiplier table) is present in the frontend — this is
  backend domain logic to design from scratch; only the *inputs* are given.
- **`NutritionData` has no per-meal food-item list** — only a flat `name`+`calories` per meal
  entry. If the backend wants to store structured food items per meal (recommended), that's new
  modeling beyond what the frontend currently sends/expects; the mobile UI would need updating too
  to actually use it (flag for product/API-design discussion, not purely inferred).
- **`ScoreBreakdown` 6 buckets vs `SCORE_RULES` 6 event keys** — these are two different taxonomies
  (event-triggered point grants vs. display categories) and the mapping between them is not defined
  in the frontend; backend must design how e.g. `workoutCompleted`/`personalRecord` events roll up
  into "Workout Completion"/"Progress" buckets shown in `ScoreBreakdown`.
- **No auth/session code exists anywhere** — no token storage (no AsyncStorage/SecureStore usage
  found), no `Authorization` header pattern, no refresh-token handling. This is 100% new backend +
  frontend-integration work, not extractable from the current app.
- **No pagination anywhere** — all lists (workouts, leaderboard top-10, meals, achievements,
  rewards) are small, fully-loaded arrays. Backend should still paginate list endpoints (esp.
  workouts catalog and leaderboard) since the UI doesn't currently constrain this.

---

## 13. Missing backend functionality (TODOs / mocked behaviors found in code)

Grep across `src/` for backend/API/TODO/mock comments surfaced the following concrete gaps
(file:line references from the repo):

- **Auth is entirely fake.**
  - `login/components/LoginScreen.tsx:29` — `handleLogin` just does `router.replace('/pages/dashboard')`; comment: "mevcut authentication sistemi bağlandığında burada gerçek login isteği atılacak."
  - `login/components/LoginScreen.tsx:35` — Google login button is a no-op; comment: "Google OAuth akışı ... burada tetiklenecek."
  - `onboarding/OnboardingContainer.tsx:49-50` — account creation just flips a local `showSuccess` flag; comment: onboarding profile should be POSTed to the API here.
  - `onboarding/OnboardingContainer.tsx:284` — Google sign-up button in the account step is also a no-op.
  - `profile/ProfileScreen.tsx:79` — logout just navigates to Welcome; comment: real sign-out call needed.
- **Plus/subscriptions are fully mocked**, explicitly marked "DO NOT USE FAKE PAYMENT" in
  `MockSubscriptionService.ts` — `purchasePlus` and `restorePurchases` are `setTimeout`-based
  stand-ins with in-memory state only (no App Store/Google Play/RevenueCat integration).
  `PlusContext.tsx:36` — initial Plus-status sync is explicitly noted to become `GET /subscription`.
- **All screen data is hardcoded mock objects** swapped in via a single `const data = MOCK_..._DATA`
  line, each with a "TODO: Backend/API bağlandığında ... fetch/query" comment, in: `dashboard/
  DashboardScreen.tsx:25`, `nutrition/NutritionScreen.tsx:30`, `profile/ProfileScreen.tsx:30`,
  `progress/ProgressScreen.tsx:35`, `workouts/WorkoutsScreen.tsx:35`, and the leaderboard/workout
  mock modules themselves.
- **Nutrition date navigation is inert** — `NutritionScreen.tsx:87` changing the selected day does
  not refetch anything; the same mock day's data is always shown regardless of date.
- **Leaderboard period filter is inert** — `LeaderboardScreen.tsx:40` switching `week`/`month`/
  `allTime` does not change the displayed data; mock data is period-independent.
- **Workout progress is not persisted** — `ActiveWorkout.tsx:181-182`, "Save & Exit" TODO: exercise
  index / per-set progress / elapsed seconds should be saved (AsyncStorage or backend) to allow
  resuming; currently discarded on exit.
- **Workout calorie estimate is a placeholder formula** — `WorkoutSummary.tsx:34`,
  `estimatedCalories = setCount * 17.5`, comment says real calculation belongs on the backend.
- **Personal-record detection is hardcoded to 2 exercises** — `workout/mockData.ts`
  `MOCK_PREVIOUS_BEST` only has entries for `squat` and `bench-press`; no other exercise can ever
  show a "NEW PR" badge until a real per-user PR store exists.
  needs an image-upload endpoint + client integration.
- **Reward countdown is a fixed mock offset**, not a real reward-cycle end date —
  `leaderboard/mockData.ts:195-198`.
- **Several navigation targets are unbuilt placeholders** (routed to but no screen exists yet — see
  §1's list): manage-subscription, macro detail, meal detail, nutrition suggestions, all body
  metrics, and the entire `/pages/profile/*` settings sub-tree (edit profile, goal, workout/
  nutrition preferences, notifications, appearance, units, language, personal info, security,
  privacy, help). These represent additional screens the backend should expect to eventually serve
  even though no frontend types exist for them yet — **AMBIGUOUS** scope until those screens are built.

---

## 14. Endpoint mapping

Proposed REST endpoints per screen, following `/api/v1/...` convention. Frontend has zero existing
network calls, so these are recommendations derived from what each screen renders/mutates, not a
migration of real calls.

**Auth**
- `POST /api/v1/auth/register` — create account + persist onboarding profile in one call (email, password, full `OnboardingData`) → returns auth token(s).
- `POST /api/v1/auth/login` — email/password → auth token(s).
- `POST /api/v1/auth/login/google` — Google OAuth token exchange.
- `POST /api/v1/auth/refresh` — refresh token.
- `POST /api/v1/auth/logout` — invalidate session/refresh token.

**Profile**
- `GET /api/v1/profile/me` — returns `ProfileData`-shaped payload (identity, body stats, training profile, stats, unit system).
- `PUT /api/v1/profile/me` — update editable profile fields (used by the not-yet-built `/pages/profile/edit`, goal, preferences screens).
- `POST /api/v1/profile/me/avatar` — avatar image upload.

**Onboarding** (may fold into register, or stand alone if profile can be edited later)
- `GET /api/v1/onboarding/options` — optional: server-driven option lists for goal/activity/experience/equipment enums (currently hardcoded client-side; could stay client-side too).

**Workouts (catalog)**
- `GET /api/v1/workouts` — list/catalog with query params for `category`, `difficulty[]`, `muscleGroup[]`, `equipment[]`, `durationRange`, `search`, pagination.
- `GET /api/v1/workouts/featured` — featured workout(s).
- `GET /api/v1/workouts/recommended` — personalized recommendations (server-side equivalent of `getPersonalizedWorkouts`, based on stored profile).
- `GET /api/v1/workouts/{id}` — workout detail (Workout + its exercise plan / `WorkoutSession`).

**Workout sessions (execution)**
- `POST /api/v1/workout-sessions` — start a session for a given workout id → returns session id + prescribed exercises.
- `PATCH /api/v1/workout-sessions/{sessionId}` — save in-progress state (exerciseIndex, per-exercise completed sets, elapsedSec) — powers "Save & Exit"/resume.
- `GET /api/v1/workout-sessions/{sessionId}` — resume an in-progress session.
- `POST /api/v1/workout-sessions/{sessionId}/sets` — log a completed set (`exerciseId`, `weight`, `reps`).
- `POST /api/v1/workout-sessions/{sessionId}/complete` — finalize; server computes duration, calories, volume, PRs, awards `workoutCompleted`/`personalRecord`/streak points per `SCORE_RULES`, returns the summary payload (`WorkoutSummary` shape).
- `GET /api/v1/workout-sessions/{sessionId}/summary` — refetch a completed session's summary.
- `GET /api/v1/workout-sessions/history` — for Progress "Recent Activity".

**Nutrition**
- `GET /api/v1/nutrition/days/{date}` — `NutritionData` for a given date (calories, macros, water, meals) — powers the date selector.
- `POST /api/v1/nutrition/days/{date}/meals` — log a meal (`type`, food selection).
- `DELETE /api/v1/nutrition/meals/{mealEntryId}` — remove a logged meal.
- `POST /api/v1/nutrition/days/{date}/water` — add a water increment.
- `GET /api/v1/nutrition/foods?search=` — food search/catalog (replaces `MOCK_FOODS`).
- `GET /api/v1/nutrition/insights` — nutrition insight/suggestion content (Plus-adjacent, `AI_NUTRITION`).

**Progress**
- `GET /api/v1/progress?range={week|month|3months|year}` — `ProgressData` incl. real date-filtered `weightHistory`.
- `POST /api/v1/progress/weight` — log a new weight point.
- `GET /api/v1/progress/body-metrics` — full body metrics list (for "view all").

**Leaderboard**
- `GET /api/v1/leaderboard?period={week|month|allTime}` — top-N entries for the period.
- `GET /api/v1/leaderboard/me` — `CurrentUserSummary` (points, rank, league, weeklyChange, etc.).
- `GET /api/v1/leaderboard/nearby` — entries ranked around the current user.
- `GET /api/v1/leaderboard/score-history?period=` — for `ScoreChart` (Plus).
- `GET /api/v1/leaderboard/score-breakdown` — for `ScoreBreakdown` (Plus).
- `GET /api/v1/leaderboard/achievements` — achievement list + earned state.
- `GET /api/v1/leaderboard/rewards` — reward list + reset countdown + points-to-reward-zone.
- `PUT /api/v1/leaderboard/visibility` — toggle `leaderboardVisible`.

**Subscriptions (Plus)**
- `GET /api/v1/subscriptions/products` — plan list (monthly/yearly, price, badge).
- `GET /api/v1/subscriptions/me` — current `SubscriptionStatus` (membership, renewsAt).
- `POST /api/v1/subscriptions/purchase` — initiate/confirm purchase (plan) — real implementation must validate a store receipt (StoreKit/Google Play) rather than trust the client.
- `POST /api/v1/subscriptions/restore` — restore purchases from store receipt.

**Notifications** *(no frontend screen built yet, but referenced as a settings menu item)*
- `GET /api/v1/notifications` / `PUT /api/v1/notifications/preferences`.

**Dashboard (aggregation)**
- `GET /api/v1/dashboard` — single aggregate endpoint returning the `DashboardData` shape
  (userName, hasCompletedFirstWorkout, todayProgress, todayWorkout, quickStats, goalProgress,
  recommended) in one round trip, matching how `DashboardScreen` consumes one object today.

**Admin** *(not evidenced in frontend at all — flag as a backend-only concern)*
- `/api/v1/admin/*` — workout/exercise catalog management, reward configuration, score-rule
  configuration (`SCORE_RULES` is explicitly meant to move server-side and likely wants an admin
  surface), user moderation. **AMBIGUOUS**: no frontend admin UI exists to derive requirements from.

---

## 15. Recommended database schema (entity sketch)

Short bullet list — full schema is being designed separately; this only names entities and the
key columns implied by the analysis above.

- **Users** — id, email, password_hash, name, gender, date_of_birth (or age at signup), created_at, membership (`FREE`/`PLUS`), leaderboard_visible.
- **UserProfiles** — user_id (FK), height_cm, weight_kg, unit_system, goal (enum), activity_level (enum), training_experience (enum), training_days_per_week, training_location (enum).
- **UserEquipment** — user_id (FK), equipment (enum, many-per-user) — normalize the onboarding `Equipment[]`.
- **Exercises** — id, name, video_url, image_url, muscle_group, default equipment refs (catalog of exercise definitions, decoupled from any one workout).
- **Workouts** — id, title, tagline, duration_min, difficulty, category, muscle_group, image_url, featured (bool).
- **WorkoutEquipment** — workout_id (FK), equipment (enum, many-per-workout).
- **WorkoutExercises** — workout_id (FK), exercise_id (FK), order_index, prescribed_sets, prescribed_reps, rest_sec.
- **WorkoutSessions** — id, user_id (FK), workout_id (FK), status (in_progress/completed/abandoned), started_at, completed_at, elapsed_sec, current_exercise_index (for resume).
- **WorkoutSessionSets** — session_id (FK), exercise_id (FK), set_number, weight, reps, logged_at.
- **PersonalRecords** — user_id (FK), exercise_id (FK), best_weight, best_reps, achieved_at (replaces the hardcoded `MOCK_PREVIOUS_BEST`).
- **NutritionDays** — user_id (FK), date, daily_calorie_target, protein_target/consumed, carbs_target/consumed, fats_target/consumed, water_target_l, water_consumed_l.
- **Meals** — id, nutrition_day_id (FK), type (Breakfast/Lunch/Dinner/Snacks), name, calories, logged_at.
- **Foods** — id, name, calories (catalog for meal-search/autocomplete; extend with macros if per-food detail is later required).
- **WeightEntries** — user_id (FK), date, weight_kg (backs `weightHistory`/progress chart).
- **BodyMetrics** — user_id (FK), metric_key, value, recorded_at (open-ended metric list, e.g. weight, body-fat%).
- **Scores** — user_id (FK), total_points, weekly_change, best_rank, best_rank_month, league.
- **ScoreEvents** — user_id (FK), rule_key (enum matching `SCORE_RULES` keys), points_awarded, occurred_at (audit trail feeding `Scores.total_points` and `ScoreHistoryPoint`/`ScoreBreakdown`).
- **Achievements** — id, title, description, icon (catalog); **UserAchievements** — user_id (FK), achievement_id (FK), earned_at.
- **Rewards** — id, rank_threshold, title, description, icon; reward_period config (cycle length / reset schedule — TBD per §12).
- **SubscriptionPlans** — plan (monthly/yearly), price, currency, period_label, badge.
- **Subscriptions** — user_id (FK), plan, status, store_receipt_ref, renews_at, started_at, canceled_at.
- **Notifications / NotificationPreferences** — user_id (FK), category, enabled (backing the not-yet-built notifications settings screen).

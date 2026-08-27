# ED:FC Auto Pilot Companion

A small Angular SPA that shows a live, read-only feed of an in-progress
[ED:FC Auto Pilot](../README.md) Auto Pilot run (route/jump plotted,
arrived in system, refueled, panic-mode stop) - reached by scanning the
QR code the desktop app shows next to its Auto Pilot button. See
SPEC.md's [§13 "Companion Site"](../SPEC.md#13-companion-site) for the
full design.

This is a fully separate deployable from the WPF app - its own `npm`
tooling, its own build, deployed together with `docs/` (at `/docs`, this
app at `/app`) via `.github/workflows/pages.yml`. It is never part of the
WPF app's own build or release pipeline.

## Firebase project

Every installation shares a single Firebase project -
`src/environments/environment.ts`/`environment.prod.ts` already carry its
real (public, safe-to-commit) web config, matching the `ProjectId` in
`CompanionSessionPublisher.cs`. Nothing to set up to build, run, or
develop against it. See [SPEC §13](../SPEC.md#13-companion-site) for the
privacy/retention design.

### Running your own fork with a separate Firebase project

Only if you're forking this project to run an independent instance
against your own Firebase project instead:

1. Create a Firebase project (Firestore, Native mode) and copy its public
   web app config (Firebase Console > Project Settings > General >
   "Your apps" > Web app).
2. Paste that config into `src/environments/environment.ts` and
   `environment.prod.ts` in place of the existing values.
3. Set the matching `ProjectId` constant in
   `RouteJumper/Services/Companion/CompanionSessionPublisher.cs` (the
   desktop app's own writer) to the same project id.
4. Publish `firestore.rules` (this folder) to that project's Firestore
   Rules (Firebase Console > Firestore Database > Rules).

## Development server

```bash
npm install
npm start   # ng serve, http://localhost:4200/
```

## Building

```bash
npm run build
```

The Pages deploy workflow instead runs
`ng build --configuration production --base-href /RouteJumper/app/`
directly, so the app's asset URLs resolve correctly once served from
`https://haggisandchips.github.io/RouteJumper/app/`.

## Running unit tests

```bash
npm test
```

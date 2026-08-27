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

There is a single, shared Firebase project every installation of ED:FC
Auto Pilot talks to - `src/environments/environment.ts`/`environment.prod.ts`
already carry its real (public, safe-to-commit - see their own doc
comments) web config, and `CompanionSessionPublisher.cs`'s `ProjectId`
constant already matches it. There's nothing to set up to build, run, or
develop against it - every CMDR's desktop app publishes to the same
project, and every visit to the deployed companion site
(`https://haggisandchips.github.io/RouteJumper/app/`) reads from it,
isolated from each other only by each session's own unguessable id (see
SPEC.md's [§13](../SPEC.md#13-companion-site) for the privacy model).

Housekeeping (deleting old, finished sessions) is handled entirely by the
desktop app itself, not a Firestore/console feature - see §13 for why
(Firestore's own TTL feature turned out to require the paid Blaze plan
even for a single delete) and how it works instead.

### Running your own fork with a separate Firebase project

Only relevant if you're forking this project to run an independent
companion site instance (a different GitHub Pages deployment, a
different desktop app build) against your own Firebase project instead
of the shared one above:

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

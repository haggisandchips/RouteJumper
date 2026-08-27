// Firebase's public web config for the single, shared project every
// installation of ED:FC Auto Pilot (and its companion site) talks to -
// there is no per-user/per-install Firebase project, so this is committed
// as-is rather than left as a placeholder. Safe to commit either way: it
// identifies the project, it does not grant access on its own - access is
// governed entirely by the Firestore security rules (see
// docs/development.md's companion-site section / specs/companion-site.md §13).
//
// Only relevant if you're forking this project to run your own, separate
// companion site instance: swap these for your own Firebase project's web
// app config (Firebase Console > Project Settings > General > "Your apps"
// > Web app) - see app/README.md.
export const environment = {
  production: false,
  firebase: {
    apiKey: "AIzaSyC5CQROklGd-wM_itmojViU087RamIeLtY",
    authDomain: "haggisandchips-routejumper.firebaseapp.com",
    projectId: "haggisandchips-routejumper",
    storageBucket: "haggisandchips-routejumper.firebasestorage.app",
    messagingSenderId: "202391659384",
    appId: "1:202391659384:web:a6efc4ba8a9765ff70de8d",
    measurementId: "G-LZKXHW5Z2F"
  },
};

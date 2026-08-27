// Firebase's public web config - safe to commit. It identifies the project,
// it does not grant access on its own; access is governed entirely by the
// Firestore security rules (see docs/development.md's companion-site
// section / SPEC.md §13).
//
// TODO: replace these placeholder values with your own Firebase project's
// web app config (Firebase Console > Project Settings > General > "Your
// apps" > Web app) before deploying.
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

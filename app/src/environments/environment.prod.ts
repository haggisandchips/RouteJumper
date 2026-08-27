// Same Firebase project as environment.ts - there is only one, no separate
// dev/prod Firebase project. Kept as a distinct file purely so the
// production build config (angular.json's fileReplacements) has somewhere
// to point, ready for the two to diverge later if that's ever needed.
//
// TODO: replace these placeholder values with your own Firebase project's
// web app config (Firebase Console > Project Settings > General > "Your
// apps" > Web app) before deploying.
export const environment = {
  production: true,
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

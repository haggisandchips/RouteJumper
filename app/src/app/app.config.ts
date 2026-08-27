import { ApplicationConfig, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideRouter, withHashLocation } from '@angular/router';
import { routes } from './app.routes';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    // Hash-based routing (/#/session/<uuid>) so deep links work on a static
    // host like GitHub Pages with no server-side rewrite rule needed.
    provideRouter(routes, withHashLocation())
  ]
};

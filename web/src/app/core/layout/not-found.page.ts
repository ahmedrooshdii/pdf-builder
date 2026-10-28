import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Button } from '../../shared/ui/button';

@Component({
  selector: 'app-not-found-page',
  imports: [RouterLink, Button],
  template: `
    <p class="text-sm font-medium text-muted-foreground">404</p>
    <h1 class="text-2xl font-semibold tracking-tight">Page not found</h1>
    <p class="text-muted-foreground">Nothing lives at this address.</p>
    <a appButton variant="outline" routerLink="/" class="mt-4">Back to start</a>
  `,
  host: { class: 'flex flex-col items-center gap-2 py-24 text-center' },
})
export class NotFoundPage {}

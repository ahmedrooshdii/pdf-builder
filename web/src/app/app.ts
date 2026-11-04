import { Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';

/** The root: the sign-in page and the signed-in shell are both routes beneath it. */
@Component({
  selector: 'app-root',
  imports: [RouterOutlet],
  templateUrl: './app.html',
  host: { class: 'contents' },
})
export class App {}

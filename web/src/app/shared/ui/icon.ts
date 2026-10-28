import { Component, computed, inject, input } from '@angular/core';
import { DomSanitizer } from '@angular/platform-browser';
import {
  lucideArrowDown,
  lucideArrowLeft,
  lucideArrowUp,
  lucideArrowUpDown,
  lucideBraces,
  lucideCheck,
  lucideChevronDown,
  lucideChevronLeft,
  lucideChevronRight,
  lucideCircleCheck,
  lucideCircleDot,
  lucideCircleX,
  lucideClock,
  lucideCodeXml,
  lucideCopy,
  lucideCopyPlus,
  lucideDownload,
  lucideExternalLink,
  lucideEye,
  lucideFileJson,
  lucideFileText,
  lucideHardDrive,
  lucideInbox,
  lucideLayoutTemplate,
  lucideList,
  lucideLoaderCircle,
  lucideMaximize2,
  lucideMinimize2,
  lucideLock,
  lucideMoon,
  lucidePalette,
  lucidePlus,
  lucideRefreshCw,
  lucideSave,
  lucideSearch,
  lucideSun,
  lucideTrash2,
  lucideTriangleAlert,
  lucideUpload,
  lucideWandSparkles,
  lucideX,
} from '@ng-icons/lucide';
import { type ClassValue, cn } from './cn';

/**
 * Every icon the app uses, by short name. Importing them one by one keeps the bundle to these
 * ~40 SVGs rather than the whole Lucide set, and the union type turns a typo into a build error.
 */
const ICONS = {
  'arrow-down': lucideArrowDown,
  'arrow-left': lucideArrowLeft,
  'arrow-up-down': lucideArrowUpDown,
  'arrow-up': lucideArrowUp,
  braces: lucideBraces,
  check: lucideCheck,
  'chevron-down': lucideChevronDown,
  'chevron-left': lucideChevronLeft,
  'chevron-right': lucideChevronRight,
  'circle-check': lucideCircleCheck,
  'circle-dot': lucideCircleDot,
  'circle-x': lucideCircleX,
  clock: lucideClock,
  code: lucideCodeXml,
  'copy-plus': lucideCopyPlus,
  copy: lucideCopy,
  download: lucideDownload,
  'external-link': lucideExternalLink,
  eye: lucideEye,
  'file-json': lucideFileJson,
  'file-text': lucideFileText,
  'hard-drive': lucideHardDrive,
  inbox: lucideInbox,
  'layout-template': lucideLayoutTemplate,
  list: lucideList,
  loader: lucideLoaderCircle,
  lock: lucideLock,
  maximize: lucideMaximize2,
  minimize: lucideMinimize2,
  moon: lucideMoon,
  palette: lucidePalette,
  plus: lucidePlus,
  refresh: lucideRefreshCw,
  save: lucideSave,
  search: lucideSearch,
  sun: lucideSun,
  trash: lucideTrash2,
  'triangle-alert': lucideTriangleAlert,
  upload: lucideUpload,
  wand: lucideWandSparkles,
  x: lucideX,
} as const;

export type IconName = keyof typeof ICONS;

/**
 * A Lucide icon as an inline `<svg>` that follows `currentColor` and fills its host.
 *
 * Sized by the host element: 1rem by default (styles.css), or any `size-*` utility, whether set
 * here or by a parent component the way shadcn's `[&_svg]:size-*` rules do (here `[&_app-icon]`).
 */
@Component({
  selector: 'app-icon',
  template: '',
  host: {
    'aria-hidden': 'true',
    '[class]': 'computedClass()',
    '[innerHTML]': 'svg()',
  },
})
export class Icon {
  readonly name = input.required<IconName>();
  readonly userClass = input<ClassValue>('', { alias: 'class' });

  private readonly sanitizer = inject(DomSanitizer);

  /** The markup comes from the icon package at build time, never from user input. */
  protected readonly svg = computed(() => this.sanitizer.bypassSecurityTrustHtml(ICONS[this.name()]));

  protected readonly computedClass = computed(() =>
    cn('pointer-events-none inline-flex shrink-0 [&>svg]:size-full', this.userClass()),
  );
}

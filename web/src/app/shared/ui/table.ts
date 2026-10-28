import { Directive } from '@angular/core';

/*
 * shadcn/ui Table, as attribute directives on the native table elements so the markup stays a
 * real, accessible <table>. Wrap it in a scroll container (appScrollArea) for narrow screens.
 */

@Directive({ selector: 'table[appTable]', host: { 'data-slot': 'table', class: 'w-full caption-bottom text-sm' } })
export class Table {}

@Directive({ selector: 'thead[appTableHeader]', host: { 'data-slot': 'table-header', class: '[&_tr]:border-b' } })
export class TableHeader {}

@Directive({
  selector: 'tbody[appTableBody]',
  host: { 'data-slot': 'table-body', class: '[&_tr:last-child]:border-0' },
})
export class TableBody {}

@Directive({
  selector: 'tr[appTableRow]',
  host: {
    'data-slot': 'table-row',
    class: 'border-b transition-colors hover:bg-muted/50 has-aria-expanded:bg-muted/50 data-[state=selected]:bg-muted',
  },
})
export class TableRow {}

@Directive({
  selector: 'th[appTableHead]',
  host: {
    'data-slot': 'table-head',
    class:
      'h-10 px-2 text-left align-middle font-medium whitespace-nowrap text-foreground [&:has([role=checkbox])]:pr-0',
  },
})
export class TableHead {}

@Directive({
  selector: 'td[appTableCell]',
  host: { 'data-slot': 'table-cell', class: 'p-2 align-middle whitespace-nowrap [&:has([role=checkbox])]:pr-0' },
})
export class TableCell {}

export const TABLE = [Table, TableHeader, TableBody, TableRow, TableHead, TableCell] as const;

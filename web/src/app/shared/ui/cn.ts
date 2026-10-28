import { type ClassValue, clsx } from 'clsx';
import { twMerge } from 'tailwind-merge';

/**
 * Joins class lists and lets later Tailwind utilities override earlier ones, so a caller's
 * `class="px-2"` beats a component's built-in `px-4` instead of both landing on the element.
 */
export function cn(...inputs: ClassValue[]): string {
  return twMerge(clsx(inputs));
}

export type { ClassValue };

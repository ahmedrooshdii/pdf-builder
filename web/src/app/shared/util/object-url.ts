import { Observable, OperatorFunction, switchMap } from 'rxjs';

/**
 * Turns each blob into an object URL that lives exactly as long as the subscription: the URL is
 * revoked when the next blob arrives or the subscriber goes away, so a page that shows previews
 * never leaks the memory behind them.
 */
export function toObjectUrl(): OperatorFunction<Blob, string> {
  return switchMap(
    (blob) =>
      new Observable<string>((subscriber) => {
        const url = URL.createObjectURL(blob);
        subscriber.next(url);
        return () => URL.revokeObjectURL(url);
      }),
  );
}

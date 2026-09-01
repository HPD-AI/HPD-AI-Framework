/** Returns the next bounded row for the Studio grid's roving focus. */
export function nextStudioGridFocusIndex(
  current: number,
  rowCount: number,
  key: string,
): number {
  if (rowCount <= 0) return 0;
  if (key === 'Home') return 0;
  if (key === 'End') return rowCount - 1;
  if (key === 'ArrowDown') return Math.min(rowCount - 1, current + 1);
  if (key === 'ArrowUp') return Math.max(0, current - 1);
  return Math.max(0, Math.min(rowCount - 1, current));
}

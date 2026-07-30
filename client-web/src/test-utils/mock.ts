/**
 * テストで配列の要素を取り出す補助。
 * 無い場合は「何件目が無いのか」が分かる形で失敗させる。
 */
export function at<T>(items: readonly T[], index: number): T {
  const item = items[index]
  if (item === undefined) {
    throw new Error(`${index} 番目の要素がありません(実際は ${items.length} 件)。`)
  }
  return item
}

/**
 * モックの呼び出し履歴を取り出す補助。
 * 呼ばれていない場合は理由の分かる形で失敗させる。
 */
export function nthCall<T extends unknown[]>(calls: readonly T[], index: number): T {
  const call = calls[index]
  if (call === undefined) {
    throw new Error(`${index + 1} 回目の呼び出しがありません(実際は ${calls.length} 回)。`)
  }
  return call
}

export function lastCall<T extends unknown[]>(calls: readonly T[]): T {
  return nthCall(calls, calls.length - 1)
}

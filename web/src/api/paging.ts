/** 서버 페이지 목록 전체 읽기 (페이지 크기 상한 500) */
export async function fetchAllPages<T>(load: (page: number, pageSize: number) => Promise<{ items: T[]; total: number }>, pageSize = 500): Promise<T[]> {
  const all: T[] = []
  for (let page = 1; ; page++) {
    const r = await load(page, pageSize)
    all.push(...r.items)
    if (r.items.length < pageSize || all.length >= r.total) return all
  }
}
